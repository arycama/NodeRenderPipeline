using System;
using System.Collections.Generic;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/GPU Instance Manager")]
public partial class GpuInstanceManagerNode : RenderPipelineNode
{
    private static readonly Dictionary<LODGroup, LOD[]> lodCache = new();
    private static readonly Dictionary<GameObject, int> instanceTypeIdCache = new();

    [Output] private GpuInstanceBuffers gpuInstanceBuffers;

    [Input, Output] private NodeConnection connection;

    private static List<InstanceRendererData> pendingInstanceData = new();

    private ComputeShader fillInstanceTypeIdShader;
    private ComputeBuffer rendererBoundsBuffer, rendererCountsBuffer, finalRendererCountsBuffer, submeshOffsetLengthsBuffer, lodSizesBuffer, rendererInstanceIDsBuffer, instanceTypeIdsBuffer, instanceTypeDataBuffer, instanceTypeLodDataBuffer, rendererInstanceIndexOffsetsBuffer, visibleRendererInstanceIndicesBuffer, positionsBuffer, lodFadesBuffer, drawCallArgsBuffer;

    private List<InstanceRendererData> dataToDelete = new();
    private List<InstanceRendererData> readyInstanceData = new();
    private Dictionary<string, List<RendererDrawCallData>> passDrawList = new();

    public static event Action<CommandBuffer> OnWillRender;
    private static bool needsRebuild;

    /// <summary>
    /// Gets typeId for a prefab. Adds it to the cache if needed
    /// </summary>
    public static int AddInstanceType(GameObject gameObject)
    {
        if (!instanceTypeIdCache.TryGetValue(gameObject, out var typeId))
        {
            typeId = instanceTypeIdCache.Count;
            instanceTypeIdCache.Add(gameObject, typeId);
        }

        return typeId;
    }

    public static void AddInstanceData(InstanceRendererData instanceRendererData)
    {
        pendingInstanceData.Add(instanceRendererData);
    }

    public static void BeginFillBuffers()
    {
        needsRebuild = true;
    }

    public override void Initialize()
    {
        fillInstanceTypeIdShader = Resources.Load<ComputeShader>("GPU Driven Rendering/FillInstanceTypeId");
    }

    public override void Cleanup()
    {
        // Delete any pending data
        foreach (var data in pendingInstanceData)
        {
            data.Clear();
        }

        // Delete all ready data
        foreach (var data in readyInstanceData)
        {
            data.Clear();
        }

        instanceTypeIdCache.Clear();
        needsRebuild = false;

        GraphicsUtilities.SafeDestroy(ref drawCallArgsBuffer);
        GraphicsUtilities.SafeDestroy(ref rendererInstanceIndexOffsetsBuffer);
        GraphicsUtilities.SafeDestroy(ref rendererBoundsBuffer);
        GraphicsUtilities.SafeDestroy(ref rendererCountsBuffer);
        GraphicsUtilities.SafeDestroy(ref finalRendererCountsBuffer);
        GraphicsUtilities.SafeDestroy(ref submeshOffsetLengthsBuffer);
        GraphicsUtilities.SafeDestroy(ref lodSizesBuffer);
        GraphicsUtilities.SafeDestroy(ref rendererInstanceIDsBuffer);
        GraphicsUtilities.SafeDestroy(ref visibleRendererInstanceIndicesBuffer);
        GraphicsUtilities.SafeDestroy(ref instanceTypeIdsBuffer);
        GraphicsUtilities.SafeDestroy(ref lodFadesBuffer);
        GraphicsUtilities.SafeDestroy(ref positionsBuffer);
        GraphicsUtilities.SafeDestroy(ref instanceTypeDataBuffer);
        GraphicsUtilities.SafeDestroy(ref instanceTypeLodDataBuffer);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("GPU Instanced Rendering", true);

        // Update any data sources
        OnWillRender?.Invoke(scope.Command);

        // Clear some buffers. We should be able to mostly avoid this by resetting values in the compute shaders.
        // Only if changed?
        if (needsRebuild)
        {
            FillBuffers(scope.Command);
            instanceTypeIdCache.Clear();
            needsRebuild = false;
        }

        if (readyInstanceData.Count == 0)
        {
            gpuInstanceBuffers = default;
            return;
        }

        // Build mesh rendering data
        using var sharedMaterials = ScopedPooledList<Material>.Get();
        using var submeshOffsetLengths = ScopedPooledList<Vector2Int>.Get();
        using var lodSizes = ScopedPooledList<float>.Get();
        using var rendererBounds = ScopedPooledList<RendererBounds>.Get();
        using var drawCallArgs = ScopedPooledList<DrawIndexedInstancedIndirectArgs>.Get();
        using var instanceTypeDatas = ScopedPooledList<InstanceTypeData>.Get();
        using var instanceTypeLodDatas = ScopedPooledList<InstanceTypeLodData>.Get();
        using var renderers = ScopedPooledList<Renderer>.Get();

        var submeshOffset = 0;
        var lodOffset = 0;
        var instanceTimesRendererCount = 0;
        var totalRendererSum = 0;

        // Stores the starting thread for each instance position
        var totalInstanceCount = 0;
        var indirectArgsOffset = 0;

        passDrawList.Clear();

        foreach (var data in readyInstanceData)
        {
            foreach (var prefab in data.GameObjects)
            {
                InstanceTypeData typeData;
                typeData.instanceCount = data.Count;
                typeData.lodRendererOffset = lodOffset;

                var rendererCount = 0;

                if (prefab.TryGetComponent<LODGroup>(out var lodGroup))
                {
                    typeData.localReferencePoint = lodGroup.localReferencePoint;
                    typeData.radius = lodGroup.size * 0.5f;
                    typeData.lodCount = lodGroup.lodCount;
                    typeData.lodSizeBufferPosition = lodOffset;

                    // Unity does not have a non-allocating version of GetLODs, so we store the lods in a dictionary.
                    // This means adding/removing a lod will not update until domain reload however
                    if (!lodCache.TryGetValue(lodGroup, out var lods))
                    {
                        lods = lodGroup.GetLODs();
                        lodCache.Add(lodGroup, lods);
                    }

                    foreach (var lod in lods)
                    {
                        ProcessLodLevel(lod.renderers, lod.screenRelativeTransitionHeight / QualitySettings.lodBias);
                    }
                }
                else
                {
                    prefab.GetComponentsInChildren<Renderer>(renderers);
                    var bounds = renderers.Value[0].bounds;
                    for (var i = 1; i < renderers.Value.Count; i++)
                    {
                        bounds.Encapsulate(renderers.Value[i].bounds);
                    }

                    typeData.localReferencePoint = bounds.center;
                    typeData.radius = Vector3.Magnitude(bounds.extents);
                    typeData.lodCount = 1;
                    typeData.lodSizeBufferPosition = lodOffset;

                    ProcessLodLevel(prefab.GetComponentsInChildren<Renderer>(), 0f);
                }

                void ProcessLodLevel(Renderer[] renderers, float lodSize)
                {
                    foreach (var renderer in renderers)
                    {
                        if (renderer == null)
                            continue;

                        var meshFilter = renderer.GetComponent<MeshFilter>();
                        if (meshFilter == null)
                            continue;

                        var mesh = meshFilter.sharedMesh;
                        if (mesh == null)
                            continue;

                        var rendererHasMotionVectors = renderer.motionVectorGenerationMode == MotionVectorGenerationMode.Object;
                        var rendererIsShadowCaster = renderer.shadowCastingMode != ShadowCastingMode.Off;

                        // TODO: Once we have combined mesh support, we should just transform the vertices by the matrix to avoid extra shader work
                        var localToWorld = Matrix4x4.TRS(renderer.transform.localPosition, renderer.transform.localRotation, renderer.transform.localScale);

                        renderer.GetSharedMaterials(sharedMaterials);

                        submeshOffsetLengths.Value.Add(new Vector2Int(submeshOffset, sharedMaterials.Value.Count));
                        submeshOffset += sharedMaterials.Value.Count;

                        // Get the mesh bounds, and transform by the renderer's matrix if it is not identity
                        var bounds = mesh.bounds;
                        if (localToWorld != Matrix4x4.identity)
                            bounds = bounds.Transform(localToWorld);

                        rendererBounds.Value.Add(new RendererBounds(bounds));

                        for (var i = 0; i < sharedMaterials.Value.Count; i++)
                        {
                            var material = sharedMaterials.Value[i];
                            if (material == null)
                                continue;

                            // First, find if the material has a motion vectors pass
                            var materialHasMotionVectors = false;
                            if (rendererHasMotionVectors)
                            {
                                for (var j = 0; j < material.passCount; j++)
                                {
                                    if (material.GetPassName(j) != "MotionVectors")
                                        continue;

                                    materialHasMotionVectors = true;
                                    break;
                                }
                            }

                            // Now add any valid passes. If material has motion vectors enabled, no other passes will be added except shadows.
                            for (var j = 0; j < material.passCount; j++)
                            {
                                var passName = material.GetPassName(j);

                                // Skip MotionVectors passes if not enabled
                                if (!materialHasMotionVectors && passName == "MotionVectors")
                                    continue;

                                // Skip ShadowCaster passes if shadows not enabled
                                if (!rendererIsShadowCaster && passName == "ShadowCaster")
                                    continue;

                                // Skip non-motion vector passes if motion vectors enabled (Except shadow caster passes)
                                if (materialHasMotionVectors && passName != "MotionVectors" && passName != "ShadowCaster")
                                    continue;

                                // Get the draw list for the current pass, or create if it doesn't yet exist
                                if (!passDrawList.TryGetValue(passName, out var drawList))
                                {
                                    drawList = new List<RendererDrawCallData>();
                                    passDrawList.Add(passName, drawList);
                                }

                                var drawData = new RendererDrawCallData(material.renderQueue, mesh, i, material, j, indirectArgsOffset * sizeof(uint), totalRendererSum, localToWorld);
                                drawList.Add(drawData);
                            }

                            var indexCount = meshFilter.sharedMesh.GetIndexCount(i);
                            var indexStart = meshFilter.sharedMesh.GetIndexStart(i);
                            drawCallArgs.Value.Add(new DrawIndexedInstancedIndirectArgs(indexCount, 0, indexStart, 0, 0));
                            indirectArgsOffset += 5;
                        }
                    }

                    instanceTypeLodDatas.Value.Add(new InstanceTypeLodData(totalRendererSum, renderers.Length, instanceTimesRendererCount - totalInstanceCount));

                    lodOffset++;
                    rendererCount += renderers.Length;
                    totalRendererSum += renderers.Length;

                    instanceTimesRendererCount += renderers.Length * data.Count;
                    lodSizes.Value.Add(lodSize);
                }

                totalInstanceCount += data.Count;
                instanceTypeDatas.Value.Add(typeData);
            }
        }

        // Now that all the renderers are grouped, sort them by queue
        foreach (var item in passDrawList.Values)
        {
            item.Sort((draw0, draw1) => draw0.renderQueue.CompareTo(draw1.renderQueue));
        }

        scope.Command.ExpandAndSetComputeBufferData(ref submeshOffsetLengthsBuffer, submeshOffsetLengths.Value);
        scope.Command.ExpandAndSetComputeBufferData(ref lodSizesBuffer, lodSizes.Value);
        scope.Command.ExpandAndSetComputeBufferData(ref rendererBoundsBuffer, rendererBounds.Value);
        scope.Command.ExpandAndSetComputeBufferData(ref instanceTypeDataBuffer, instanceTypeDatas.Value);
        scope.Command.ExpandAndSetComputeBufferData(ref drawCallArgsBuffer, drawCallArgs.Value, ComputeBufferType.IndirectArguments);
        scope.Command.ExpandAndSetComputeBufferData(ref instanceTypeLodDataBuffer, instanceTypeLodDatas.Value);

        GraphicsUtilities.SafeResize(ref rendererInstanceIDsBuffer, instanceTimesRendererCount);
        GraphicsUtilities.SafeResize(ref visibleRendererInstanceIndicesBuffer, instanceTimesRendererCount);
        GraphicsUtilities.SafeResize(ref rendererCountsBuffer, totalRendererSum);
        GraphicsUtilities.SafeResize(ref rendererInstanceIndexOffsetsBuffer, totalRendererSum);
        GraphicsUtilities.SafeResize(ref finalRendererCountsBuffer, totalRendererSum);

        gpuInstanceBuffers = new GpuInstanceBuffers(rendererInstanceIDsBuffer, rendererInstanceIndexOffsetsBuffer, rendererCountsBuffer, finalRendererCountsBuffer, visibleRendererInstanceIndicesBuffer, positionsBuffer, instanceTypeIdsBuffer, lodFadesBuffer, rendererBoundsBuffer, lodSizesBuffer, instanceTypeDataBuffer, instanceTypeLodDataBuffer, submeshOffsetLengthsBuffer, drawCallArgsBuffer, readyInstanceData, passDrawList);
    }

    public override void FrameRenderComplete()
    {
        foreach (var data in dataToDelete)
        {
            data.Clear();
        }

        dataToDelete.Clear();
    }

    private void FillBuffers(CommandBuffer command)
    {
        // Fill instanceId buffer. (Should be done when the object is assigned)
        // This buffer contains the type at each index. (Eg 0, 1, 2)
        var positionCountSum = 0;
        foreach (var data in pendingInstanceData)
            positionCountSum += data.Count;

        GraphicsUtilities.SafeResize(ref instanceTypeIdsBuffer, positionCountSum);
        GraphicsUtilities.SafeResize(ref positionsBuffer, positionCountSum, sizeof(float) * 12);
        GraphicsUtilities.SafeResize(ref lodFadesBuffer, positionCountSum);

        readyInstanceData.Clear();
        int positionOffset = 0;
        foreach (var data in pendingInstanceData)
        {
            command.SetComputeIntParam(fillInstanceTypeIdShader, "_Offset", positionOffset);
            command.SetComputeIntParam(fillInstanceTypeIdShader, "_Count", data.Count);

            command.SetComputeBufferParam(fillInstanceTypeIdShader, 0, "_InstanceTypeIds", instanceTypeIdsBuffer);
            command.SetComputeBufferParam(fillInstanceTypeIdShader, 0, "_PositionsResult", positionsBuffer);
            command.SetComputeBufferParam(fillInstanceTypeIdShader, 0, "_LodFadesResult", lodFadesBuffer);
            command.SetComputeBufferParam(fillInstanceTypeIdShader, 0, "_PositionsInput", data.PositionBuffer);
            command.SetComputeBufferParam(fillInstanceTypeIdShader, 0, "_InstanceTypeIdsInput", data.InstanceTypeIdBuffer);
            command.DispatchNormalized(fillInstanceTypeIdShader, 0, data.Count, 1, 1);
            positionOffset += data.Count;

            // Schedule the original buffer for deletion (Immediately releasing can cause errors, eg we might release to pool, then it might get unpooled and used elsewhere before this command executes)
            dataToDelete.Add(data);

            readyInstanceData.Add(data);
        }

        pendingInstanceData.Clear();
    }
}
