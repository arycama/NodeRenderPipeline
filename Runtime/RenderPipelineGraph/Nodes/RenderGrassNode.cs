using NodeGraph;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Grass")]
public partial class RenderGrassNode : RenderPipelineNode
{
    [SerializeField] private bool enabled = true;
    [SerializeField] private bool update = true;
    [SerializeField] private bool castShadow = false;
    [SerializeField, Pow2(128)] private int patchSize = 32;
    [SerializeField] private Material material = null;

    private ComputeBuffer finalPatches;
    private ComputeBuffer subdividePatchesA, subdividePatchesB;
    private ComputeBuffer cullingPlaneBuffer;
    private ComputeBuffer indirectArgsBuffer;
    private ComputeBuffer indirectDispatchBuffer;
    private ComputeBuffer elementCountBuffer;

    public override void Initialize()
    {
        //DirectionalShadowRenderer.DrawShadows += (context, camera, cullingPlanes) => Draw(context, camera, true, cullingPlanes);
        cullingPlaneBuffer = new ComputeBuffer(9, UnsafeUtility.SizeOf<Plane>()) { name = "Culling Planes" };
        indirectArgsBuffer = new ComputeBuffer(5, sizeof(int), ComputeBufferType.IndirectArguments) { name = "Indirect Args" };
        indirectDispatchBuffer = new ComputeBuffer(3, sizeof(int), ComputeBufferType.IndirectArguments) { name = "Indirect Dispatch" };
        elementCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw) { name = "Element Count" };
        indirectDispatchBuffer.SetData(new int[] { 1, 1, 1 });
    }

    public override void Cleanup()
    {
        //DirectionalShadowRenderer.DrawShadows -= (context, camera, cullingPlanes) => Draw(context, camera, true, cullingPlanes, );

        GraphicsUtilities.SafeDestroy(ref finalPatches);
        GraphicsUtilities.SafeDestroy(ref subdividePatchesA);
        GraphicsUtilities.SafeDestroy(ref subdividePatchesB);
        GraphicsUtilities.SafeDestroy(ref cullingPlaneBuffer);
        GraphicsUtilities.SafeDestroy(ref indirectArgsBuffer);
        GraphicsUtilities.SafeDestroy(ref indirectDispatchBuffer);
        GraphicsUtilities.SafeDestroy(ref elementCountBuffer);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        //if (material == null || !enabled)
        //    return;

        //var terrain = Terrain.activeTerrain;
        //if (terrain == null)
        //    return;

        //// Need to resize buffer for visible indices
        //var size = new Vector2(terrain.terrainData.size.x, terrain.terrainData.size.z);
        //var patchCounts = Vector2Int.FloorToInt(size / patchSize);
        //var patchCount = patchCounts.x * patchCounts.y;
        //GraphicsUtilities.SafeExpand(ref finalPatches, patchCount, sizeof(float) * 8, ComputeBufferType.Append); // 2x float4
        //GraphicsUtilities.SafeExpand(ref subdividePatchesA, patchCount, sizeof(float) * 8, ComputeBufferType.Append); // 2x float4
        //GraphicsUtilities.SafeExpand(ref subdividePatchesB, patchCount, sizeof(float) * 8, ComputeBufferType.Append); // 2x float4

        //finalPatches.name = "Final Patches";
        //subdividePatchesA.name = "Subdivide Patches A";
        //subdividePatchesB.name = "Subdivide Patches B";

        //using var scope = context.ScopedCommandBuffer("Render Grass", true);

        //// Generate min/max for terrain.. really don't need to do this every frame
        //var terrainResolution = terrain.terrainData.heightmapResolution;
        //var terrainDescriptor = new RenderTextureDescriptor(terrainResolution, terrainResolution, RenderTextureFormat.RGFloat)
        //{
        //    autoGenerateMips = false,
        //    enableRandomWrite = true,
        //    useMipMap = true
        //};

        //var tempHeightId = Shader.PropertyToID("_TerrainHeightMinMax");
        //scope.Command.GetTemporaryRT(tempHeightId, terrainDescriptor);

        //var heightmap = terrain.GetComponent<TerrainRenderer>().Heightmap;

        //GraphicsUtilities.GenerateMinMaxHiZ(scope.Command, terrainResolution, terrainResolution, heightmap, tempHeightId, tempHeightId, true,  terrain.terrainData.size.y, terrain.GetPosition().y);

        //// Culling planes
        //var cullingPlanes = ListPool<Plane>.Get();
        //cullingPlanes.Clear();
        //cullingPlanes.AddRange(GeometryUtility.CalculateFrustumPlanes(camera));
        //if (cullingPlaneBuffer.count < cullingPlanes.Count)
        //{
        //    cullingPlaneBuffer.Release();
        //    cullingPlaneBuffer = new ComputeBuffer(cullingPlanes.Count, UnsafeUtility.SizeOf<Plane>());
        //}

        //var height = material.GetFloat("_Height");
        //var bladeDensity = (int)material.GetFloat("_BladeDensity");
        //var bladeCount = patchSize * bladeDensity;

        //// Cull patches
        //if (update)
        //{
        //    var compute = Resources.Load<ComputeShader>("GrassCull");

        //    scope.Command.SetBufferCounterValue(subdividePatchesA, 0);
        //    scope.Command.SetBufferCounterValue(subdividePatchesB, 0);
        //    scope.Command.SetBufferCounterValue(finalPatches, 0);

        //    var screenMatrix = (camera.projectionMatrix * camera.worldToCameraMatrix).ConvertToAtlasMatrix();
        //    scope.Command.SetComputeMatrixParam(compute, "_WorldToScreen", screenMatrix);

        //    var extents = terrain.terrainData.size * 0.5f;
        //    var center = terrain.GetPosition() + extents;

        //    scope.Command.SetComputeVectorParam(compute, "_TerrainSize", terrain.terrainData.size);

        //    scope.Command.SetComputeVectorParam(compute, "_BoundsCenter", center);
        //    scope.Command.SetComputeVectorParam(compute, "_BoundsExtents", extents);

        //    scope.Command.SetComputeFloatParam(compute, "_EdgeLength", material.GetFloat("_EdgeLength"));
        //    scope.Command.SetComputeIntParam(compute, "_CullingPlanesCount", cullingPlanes.Count);
        //    scope.Command.SetBufferData(cullingPlaneBuffer, cullingPlanes);

        //    scope.Command.EnableShaderKeyword("HI_Z_CULL");

        //    var mipCount = Texture2DExtensions.MipCount(patchCounts.x, patchCounts.y) - 1;
        //    for (var i = 0; i <= mipCount; i++)
        //    {
        //        var kernelIndex = i == 0 ? 0 : (i == mipCount ? 2 : 1);

        //        // Need to ping pong between two buffers so we're not reading/writing to the same one
        //        var srcBuffer = i % 2 == 0 ? subdividePatchesB : subdividePatchesA;
        //        var dstBuffer = i % 2 == 0 ? subdividePatchesA : subdividePatchesB;

        //        var mip = mipCount - i;
        //        var patchExtents = this.patchSize * Mathf.Pow(2, mip) * 0.5f;
        //        scope.Command.SetComputeVectorParam(compute, "_PatchExtents", new Vector3(patchExtents, height * 0.5f, patchExtents));
        //        scope.Command.SetComputeBufferParam(compute, kernelIndex, "_InputPatches", srcBuffer);
        //        scope.Command.SetComputeBufferParam(compute, kernelIndex, "_SubdividePatches", dstBuffer);
        //        scope.Command.SetComputeBufferParam(compute, kernelIndex, "_FinalPatchesWrite", finalPatches);
        //        scope.Command.SetComputeBufferParam(compute, kernelIndex, "_ElementCount", elementCountBuffer);
        //        scope.Command.SetComputeBufferParam(compute, kernelIndex, "_CullingPlanes", cullingPlaneBuffer);
        //        scope.Command.SetComputeTextureParam(compute, kernelIndex, "_TerrainHeights", tempHeightId);
        //        scope.Command.SetComputeIntParam(compute, "_MaxHiZMip", mipCount);

        //        // First dispatch only needs 1 element, other dispatches are indirect
        //        if (i == 0)
        //            scope.Command.DispatchCompute(compute, kernelIndex, 1, 1, 1);
        //        else
        //            scope.Command.DispatchCompute(compute, kernelIndex, indirectDispatchBuffer, 0);

        //        // Copy to indirect args buffer, and another buffer with 1 element for reading
        //        // (As we're not sure if reading from the buffer while rendering will work
        //        scope.Command.CopyCounterValue(dstBuffer, elementCountBuffer, 0);
        //        scope.Command.CopyCounterValue(dstBuffer, indirectDispatchBuffer, 0);

        //        // Need to process indirect dispatch buffer to contain ceil(count / 64) elements
        //        scope.Command.SetComputeBufferParam(compute, 3, "_IndirectArgs", indirectDispatchBuffer);
        //        scope.Command.DispatchCompute(compute, 3, 1, 1, 1);
        //    }

        //    ListPool<Plane>.Release(cullingPlanes);

        //    var indirectArgs = ListPool<int>.Get();
        //    indirectArgs.Add(bladeCount * bladeCount); // vertex count per instance
        //    indirectArgs.Add(0); // instance count (filled in later)
        //    indirectArgs.Add(0); // start vertex location
        //    indirectArgs.Add(0); // start instance location
        //    scope.Command.SetBufferData(indirectArgsBuffer, indirectArgs);
        //    ListPool<int>.Release(indirectArgs);

        //    // Copy counter value to indirect args buffer
        //    scope.Command.CopyCounterValue(finalPatches, indirectArgsBuffer, sizeof(int));
        //}

        //var propertyBlock = GenericPool<MaterialPropertyBlock>.Get();
        //propertyBlock.Clear();

        ////scope.Command.SetGlobalTexture("_TerrainHeightmapTexture", terrain.GetComponent<ITerrainRenderer>().Heightmap);
        //propertyBlock.SetFloat("BladeCount", bladeCount);
        //propertyBlock.SetBuffer("_FinalPatches", finalPatches);

        ////scope.Command.SetRenderTarget(binding);
        //scope.Command.DrawProceduralIndirect(Matrix4x4.identity, material, 0, MeshTopology.Points, indirectArgsBuffer, 0, propertyBlock);

        //scope.Command.ReleaseTemporaryRT(tempHeightId);
        //scope.Command.DisableShaderKeyword("HI_Z_CULL");

        //GenericPool<MaterialPropertyBlock>.Release(propertyBlock);
    }
}