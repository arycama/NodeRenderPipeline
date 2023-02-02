using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using NodeGraph;
using TerrainGraph;

[ExecuteAlways, RequireComponent(typeof(Terrain))]
public class TerrainRenderer : MonoBehaviour, ITerrainRenderer
{
    private static readonly List<int> quadtreeIds = new();

    [SerializeField, Pow2(1024)] private int cellCount = 32;
    [SerializeField] private TerrainGraph.TerrainGraph terrainGraph;
    [SerializeField, Pow2(128)] private int patchVertices = 32;
    [SerializeField, Range(1, 128)] private float edgeLength = 64;

    private bool heightmapDirty, isInitialized;

    private ComputeBuffer patchDataBuffer, indirectArgsBuffer, lodIndirectArgsBuffer;

    private GraphicsBuffer indexBuffer;
    private RenderTexture minMaxHeight;
    private Terrain terrain;

    private int VerticesPerTileEdge => patchVertices + 1;
    private int QuadListIndexCount => patchVertices * patchVertices * 4;
    private int Resolution => terrain.terrainData.heightmapResolution;

    private RenderTexture heightmap;
    private RenderTexture normalMap;

    public RenderTargetIdentifier Heightmap => heightmap;
    public RenderTargetIdentifier NormalMap => normalMap;


    private Action<CommandBuffer> heightmapUpdated;

    public event Action<CommandBuffer> HeightmapUpdated
    {
        add
        {
            heightmapUpdated += value;
            if (isInitialized)
            {
                var command = CommandBufferPool.Get();
                value.Invoke(command);
                Graphics.ExecuteCommandBuffer(command);
                CommandBufferPool.Release(command);
            }
        }
        remove
        {
            heightmapUpdated -= value;
        }
    }

    private void OnEnable()
    {
        terrain = GetComponent<Terrain>();

        var resolution = terrain.terrainData.heightmapResolution;
        minMaxHeight = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RGFloat)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            name = "Terrain Min Max Height Map",
            useMipMap = true,
        }.Created();

        heightmap = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.R16)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            name = "Terrain Height Map",
            useMipMap = true,
        }.Created();

        normalMap = new RenderTexture(resolution, resolution, 0, GraphicsFormat.R8G8_SNorm)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            name = "Terrain Normal Map",
            useMipMap = true,
        }.Created();

        heightmapDirty = true;

        if(terrainGraph != null)
            terrainGraph.AddListener(OnGraphModified, 0);

        indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, QuadListIndexCount, sizeof(ushort));

        int index = 0;
        var pIndices = new ushort[QuadListIndexCount];
        for (var y = 0; y < patchVertices; y++)
        {
            var rowStart = y * VerticesPerTileEdge;

            for (var x = 0; x < patchVertices; x++)
            {
                // Can do a checkerboard flip to avoid directioanl artifacts, but will mess with the tessellation code
                //var flip = (x & 1) == (y & 1);

                //if(flip)
                //{
                pIndices[index++] = (ushort)(rowStart + x);
                pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge);
                pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge + 1);
                pIndices[index++] = (ushort)(rowStart + x + 1);
                //}
                //else
                //{
                //    pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge);
                //    pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge + 1);
                //    pIndices[index++] = (ushort)(rowStart + x + 1);
                //    pIndices[index++] = (ushort)(rowStart + x);
                //}
            }
        }

        indexBuffer.SetData(pIndices);
        CullTerrainNode.Render += Cull;
        DrawTerrainNode.Render += Render;

        lodIndirectArgsBuffer = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments) { name = "Terrain Indirect Args" };
        indirectArgsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments) { name = "Terrain Draw Args" };
        patchDataBuffer = new ComputeBuffer(cellCount * cellCount, sizeof(uint), ComputeBufferType.Structured) { name = "Terrain Patch Data" };
    }

    private void OnGraphModified()
    {
        heightmapDirty = true;
    }

    private void OnDisable()
    {
        CullTerrainNode.Render -= Cull;
        DrawTerrainNode.Render -= Render;

        indexBuffer.Release();
        lodIndirectArgsBuffer.Release();
        indirectArgsBuffer.Release();
        patchDataBuffer.Release();

        DestroyImmediate(minMaxHeight);
        DestroyImmediate(heightmap);
        DestroyImmediate(normalMap);

        if(terrainGraph != null)
            terrainGraph.RemoveListener(OnGraphModified);

        isInitialized = false;
    }

    private void UpdateHeightmap(CommandBuffer command)
    {
        if (terrainGraph == null)
            return;

        // Update heightmap from graph
        using var nodes = ScopedPooledList<BaseNode>.Get();
        foreach (var node in terrainGraph.Nodes)
            if (node is HeightmapOutputNode)
                nodes.Value.Add(node);

        if (nodes.Value.Count > 0)
        {
            terrainGraph.Generate(terrain, nodes, Resolution, command);
        }
        else
        {
            var computeShader1 = Resources.Load<ComputeShader>("HeightmapOutputNode");
            command.SetComputeTextureParam(computeShader1, 0, "Input", terrain.terrainData.heightmapTexture);
            command.SetComputeTextureParam(computeShader1, 0, "Result", heightmap);
            command.SetComputeFloatParam(computeShader1, "_Min", 0f);
            command.SetComputeFloatParam(computeShader1, "_Max", 0.5f);
            command.DispatchNormalized(computeShader1, 0, Resolution, Resolution, 1);
        }

        // Generate min/max for terrain.
        minMaxHeight.Resize(Resolution, Resolution);
        GraphicsUtilities.GenerateMinMaxHiZ(command, Resolution, Resolution, heightmap, minMaxHeight, minMaxHeight, true, terrain.terrainData.size.y, terrain.GetPosition().y);

        // Update normal map
        var scale = terrain.terrainData.heightmapScale;
        var height = terrain.terrainData.size.y;
        var computeShader = Resources.Load<ComputeShader>("Output/NormalMapOutputNode");
        command.SetComputeTextureParam(computeShader, 0, "_Input", heightmap);
        command.SetComputeTextureParam(computeShader, 0, "_Result", normalMap);
        command.SetComputeIntParam(computeShader, "_Resolution", Resolution);
        command.SetComputeVectorParam(computeShader, "_Scale", new Vector2(height / (8f * scale.x), height / (8f * scale.z)));
        command.DispatchNormalized(computeShader, 0, Resolution, Resolution, 1);
        command.GenerateMips(heightmap);
        command.GenerateMips(normalMap);

        heightmapDirty = false;
        isInitialized = true;
        heightmapUpdated?.Invoke(command);
    }

    public void Cull(CommandBuffer command, Vector3 cameraPosition, Vector4[] cullingPlanes, int cullingPlanesCount)
    {
        if (heightmapDirty)
            UpdateHeightmap(command);

        // TODO: Move this?
        var size = terrain.terrainData.size;
        var position = terrain.GetPosition() - cameraPosition.Y0();
        var terrainRemapHalfTexel = GraphicsUtilities.HalfTexelRemap(position.XZ(), size.XZ(), Vector2.one * terrain.terrainData.heightmapResolution);

        command.SetGlobalVector("_TerrainScaleOffset", new Vector4(1f / size.x, 1f / size.z, -position.x / size.x, -position.z / size.z));
        command.SetGlobalVector("_TerrainRemapHalfTexel", terrainRemapHalfTexel);
        command.SetGlobalFloat("_TerrainHeightScale", size.y);
        command.SetGlobalFloat("_TerrainHeightOffset", position.y);
        command.SetGlobalTexture("_TerrainNormalMap", normalMap);
        command.SetGlobalTexture("_TerrainHeightmapTexture", heightmap);

        // Cull patches
        CullQuadtreePatches(cameraPosition, cullingPlanes, command, size, cullingPlanesCount);
    }

    public void Render(CommandBuffer command, string passName, Vector3 cameraPosition)
    {
        var material = terrain.materialTemplate;
        if (material == null)
            return;

        var passIndex = material.FindPass(passName);
        if (passIndex == -1)
            return;

        var size = terrain.terrainData.size;
        var position = terrain.GetPosition() - cameraPosition.Y0();

        var propertyBlock = GenericPool<MaterialPropertyBlock>.Get();
        propertyBlock.Clear();

        propertyBlock.SetBuffer("_PatchData", patchDataBuffer);
        propertyBlock.SetTexture("_TerrainHeightmapTexture", heightmap);
        propertyBlock.SetTexture("_TerrainHolesTexture", terrain.terrainData.holesTexture);
        propertyBlock.SetTexture("_TerrainHeightmapTexture", heightmap);
        propertyBlock.SetTexture("_TerrainNormalMap", normalMap);

        propertyBlock.SetInt("_VerticesPerEdge", VerticesPerTileEdge);
        propertyBlock.SetInt("_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
        propertyBlock.SetFloat("_RcpVerticesPerEdge", 1f / VerticesPerTileEdge);
        propertyBlock.SetFloat("_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));
        propertyBlock.SetVector("_PatchScaleOffset", new Vector4(size.x / cellCount, size.z / cellCount, position.x, position.z));
        propertyBlock.SetFloat("_InvCellCount", 1f / cellCount);

        propertyBlock.SetFloat("_MaxLod", Mathf.Log(cellCount, 2));
        command.DrawProceduralIndirect(indexBuffer, Matrix4x4.identity, material, passIndex, MeshTopology.Quads, indirectArgsBuffer, 0, propertyBlock);

        GenericPool<MaterialPropertyBlock>.Release(propertyBlock);
    }

    private void CullQuadtreePatches(Vector3 cameraPosition, Vector4[] cullingPlanes, CommandBuffer command, Vector3 size, int cullingPlanesCount)
    {
        var indirectArgs = ListPool<int>.Get();
        indirectArgs.Add(QuadListIndexCount); // index count per instance
        indirectArgs.Add(0); // instance count (filled in later)
        indirectArgs.Add(0); // start index location
        indirectArgs.Add(0); // base vertex location
        indirectArgs.Add(0); // start instance location
        command.SetBufferData(indirectArgsBuffer, indirectArgs);
        ListPool<int>.Release(indirectArgs);

        var compute = Resources.Load<ComputeShader>("QuadtreeCull");

        command.SetComputeBufferParam(compute, 0, "_IndirectArgs", indirectArgsBuffer);
        command.SetComputeBufferParam(compute, 0, "_PatchDataWrite", patchDataBuffer);
        command.SetComputeVectorArrayParam(compute, "_CullingPlanes", cullingPlanes);
        command.SetComputeTextureParam(compute, 0, "_TerrainHeights", minMaxHeight);

        var positionOffset = new Vector4(size.x, size.z, terrain.GetPosition().x - cameraPosition.x, terrain.GetPosition().z - cameraPosition.z);
        command.SetComputeVectorParam(compute, "_TerrainPositionOffset", positionOffset);

        command.SetComputeFloatParam(compute, "_EdgeLength", edgeLength * patchVertices);
        command.SetComputeIntParam(compute, "_CullingPlanesCount", cullingPlanesCount);
        command.SetComputeIntParam(compute, "_MipCount", minMaxHeight.mipmapCount - 1);

        // We can do 32x32 cells in a single pass, larger counts need to be broken up into several passes
        var maxPassesPerDispatch = 6;
        var totalPassCount = (int)Mathf.Log(cellCount, 2f) + 1;
        var dispatchCount = Mathf.Ceil(totalPassCount / (float)maxPassesPerDispatch);

        var tempLodId = Shader.PropertyToID("_QuadtreeLodTemp");
        if(dispatchCount > 1)
        {
            // If more than one dispatch, we need to write lods out to a temp texture first. Otherwise they are done via shared memory so no texture is needed
            var tempLodDesc = new RenderTextureDescriptor(cellCount, cellCount, GraphicsFormat.R16_UInt, 0) { enableRandomWrite = true };
            command.GetTemporaryRT(tempLodId, tempLodDesc);
        }

        var previousId = 0;
        for (var i = 0; i < dispatchCount; i++)
        {
            var isFirstPass = i == 0; // Also indicates whether this is -not- the first pass
            if (!isFirstPass)
            {
                command.SetComputeTextureParam(compute, 0, "_TempResult", previousId);
            }

            var isFinalPass = i == dispatchCount - 1; // Also indicates whether this is -not- the final pass
            var newId = 0;

            if(!isFinalPass)
            {
                var tempResolution = 1 << ((i + 1) * (maxPassesPerDispatch - 1));

                if(quadtreeIds.Count <= i)
                {
                    newId = Shader.PropertyToID($"_QuadtreeTemp{i}");
                    quadtreeIds.Add(newId);
                }
                else
                {
                    newId = quadtreeIds[i];
                }


                var desc = new RenderTextureDescriptor(tempResolution, tempResolution, GraphicsFormat.R16_UInt, 0) { enableRandomWrite = true };
                command.GetTemporaryRT(newId, desc);
                command.SetComputeTextureParam(compute, 0, "_TempResultWrite", newId);
            }

            if(isFinalPass && !isFirstPass)
            {
                // Final pass writes out lods to a temp texture if more than one pass was used
                command.SetComputeTextureParam(compute, 0, "_LodResult", tempLodId);
            }

            // Do up to 6 passes per dispatch.
            var passCount = Mathf.Min(maxPassesPerDispatch, totalPassCount - i * maxPassesPerDispatch);
            command.SetComputeIntParam(compute, "_PassCount", passCount);
            command.SetComputeIntParam(compute, "_PassOffset", 6 * i);
            command.SetComputeIntParam(compute, "_TotalPassCount", totalPassCount);

            using var firstKeywordScope = command.KeywordScope("FIRST", isFirstPass);
            using var finalKeywordScope = command.KeywordScope("FINAL", isFinalPass);

            var threadCount = 1 << (i * 6 + passCount - 1);
            command.DispatchNormalized(compute, 0, threadCount, threadCount, 1);

            // Assign so it can be used next pass
            if (!isFirstPass)
                command.ReleaseTemporaryRT(previousId);

            previousId = newId;
        }

        if(dispatchCount > 1)
        {
            // If more than one pass needed, we need a second pass to write out lod deltas to the patch data
            // Copy count from indirect draw args so we only dispatch as many threads as needed
            command.SetComputeBufferParam(compute, 1, "_IndirectArgsInput", indirectArgsBuffer);
            command.SetComputeBufferParam(compute, 1, "_IndirectArgs", lodIndirectArgsBuffer);
            command.DispatchCompute(compute, 1, 1, 1, 1);

            command.SetComputeIntParam(compute, "_CellCount", cellCount);
            command.SetComputeBufferParam(compute, 2, "_PatchDataWrite", patchDataBuffer);
            command.SetComputeTextureParam(compute, 2, "_LodInput", tempLodId);
            command.SetComputeBufferParam(compute, 2, "_IndirectArgs", indirectArgsBuffer);
            command.DispatchCompute(compute, 2, lodIndirectArgsBuffer, 0);

            command.ReleaseTemporaryRT(tempLodId);
        }
    }
}
