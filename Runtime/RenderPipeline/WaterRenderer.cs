using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

[ExecuteAlways]
public class WaterRenderer : MonoBehaviour
{
    private static readonly List<WaterRenderer> waterRenderers = new();
    private static readonly IndexedShaderPropertyId quadtreeTempIds = new("_QuadtreeTemp");

    [SerializeField, Pow2(1024)] private int cellCount = 32;
    [SerializeField, Tooltip("Size of the Mesh in World Space")] private int size = 256;
    [SerializeField, Pow2(128)] private int patchVertices = 32;
    [SerializeField, Range(1, 128)] private float edgeLength = 64;
    [SerializeField, Tooltip("Material used to render the Mesh")] private Material material = null;

    private ComputeBuffer patchDataBuffer, indirectArgsBuffer, lodIndirectArgsBuffer;
    private GraphicsBuffer indexBuffer;

    public static List<WaterRenderer> WaterRenderers => waterRenderers;

    private int VerticesPerTileEdge => patchVertices + 1;
    private int QuadListIndexCount => patchVertices * patchVertices * 4;

    private void OnEnable()
    {
        waterRenderers.Add(this);

        lodIndirectArgsBuffer = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments) { name = "Water Indirect Args" };
        indirectArgsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments) { name = "Water Draw Args" };
        patchDataBuffer = new ComputeBuffer(cellCount * cellCount, sizeof(uint), ComputeBufferType.Structured) { name = "Water Patch Data" };

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
    }

    private void OnDisable()
    {
        waterRenderers.Remove(this);

        patchDataBuffer.Release();
        indirectArgsBuffer.Release();
        lodIndirectArgsBuffer.Release();
        indexBuffer.Release();
    }

    public void Cull(CommandBuffer command, Vector3 viewPosition, CullingPlanes cullingPlanes)
    {
        using var indirectArgs = ScopedPooledList<int>.Get();
        indirectArgs.Value.Add(QuadListIndexCount); // index count per instance
        indirectArgs.Value.Add(0); // instance count (filled in later)
        indirectArgs.Value.Add(0); // start index location
        indirectArgs.Value.Add(0); // base vertex location
        indirectArgs.Value.Add(0); // start instance location
        command.SetBufferData(indirectArgsBuffer, indirectArgs.Value);

        var compute = Resources.Load<ComputeShader>("QuadtreeCull");

        command.SetComputeBufferParam(compute, 0, "_IndirectArgs", indirectArgsBuffer);
        command.SetComputeBufferParam(compute, 0, "_PatchDataWrite", patchDataBuffer);
        command.SetComputeVectorArrayParam(compute, "_CullingPlanes", cullingPlanes);

        // Snap to quad-sized increments on largest cell
        var texelSizeX = size / (float)patchVertices;
        var texelSizeZ = size / (float)patchVertices;
        var positionX = Mathf.Floor((viewPosition.x - size * 0.5f) / texelSizeX) * texelSizeX ;
        var positionZ = Mathf.Floor((viewPosition.z - size * 0.5f) / texelSizeZ) * texelSizeZ;
        var positionOffset = new Vector4(size, size, positionX, positionZ);
        command.SetComputeVectorParam(compute, "_TerrainPositionOffset", positionOffset);

        command.SetComputeFloatParam(compute, "_EdgeLength", edgeLength * patchVertices);
        command.SetComputeIntParam(compute, "_CullingPlanesCount", cullingPlanes.Count);

        // We can do 32x32 cells in a single pass, larger counts need to be broken up into several passes
        var maxPassesPerDispatch = 6;
        var totalPassCount = (int)Mathf.Log(cellCount, 2f) + 1;
        var dispatchCount = Mathf.Ceil(totalPassCount / (float)maxPassesPerDispatch);

        using var profilerScope = command.ProfilerScope("Cull and Lod CS");

        var tempLodId = Shader.PropertyToID("_QuadtreeLodTemp");
        if (dispatchCount > 1)
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

            if (!isFinalPass)
            {
                var tempResolution = 1 << ((i + 1) * (maxPassesPerDispatch - 1));
                newId = quadtreeTempIds.GetProperty(i);
                var desc = new RenderTextureDescriptor(tempResolution, tempResolution, GraphicsFormat.R16_UInt, 0) { enableRandomWrite = true };
                command.GetTemporaryRT(newId, desc);
                command.SetComputeTextureParam(compute, 0, "_TempResultWrite", newId);
            }

            if (isFinalPass && !isFirstPass)
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
            using var noHeightsKeywordScope = command.KeywordScope("NO_HEIGHTS");

            var threadCount = 1 << (i * 6 + passCount - 1);
            command.DispatchNormalized(compute, 0, threadCount, threadCount, 1);

            // Assign so it can be used next pass
            if (!isFirstPass)
                command.ReleaseTemporaryRT(previousId);

            previousId = newId;
        }

        if (dispatchCount > 1)
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

    public void Render(CommandBuffer command, string passName, Vector3 viewPosition)
    {
        var pass = material.FindPass(passName);
        if (pass == -1)
            return;

        var propertyBlock = GenericPool<MaterialPropertyBlock>.Get();
        propertyBlock.Clear();
        propertyBlock.SetBuffer("_PatchData", patchDataBuffer);
        propertyBlock.SetInt("_VerticesPerEdge", VerticesPerTileEdge);
        propertyBlock.SetInt("_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
        propertyBlock.SetFloat("_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));

        // Snap to quad-sized increments on largest cell
        var texelSize = size / (float)patchVertices;
        var positionX = Mathf.Floor((viewPosition.x - size * 0.5f) / texelSize) * texelSize - viewPosition.x;
        var positionZ = Mathf.Floor((viewPosition.z - size * 0.5f) / texelSize) * texelSize - viewPosition.z;
        propertyBlock.SetVector("_PatchScaleOffset", new Vector4(size / (float)cellCount, size / (float)cellCount, positionX, positionZ));

        command.DrawProceduralIndirect(indexBuffer, Matrix4x4.identity, material, pass, MeshTopology.Quads, indirectArgsBuffer, 0, propertyBlock);

        GenericPool<MaterialPropertyBlock>.Release(propertyBlock);
    }
}
