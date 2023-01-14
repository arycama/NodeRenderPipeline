using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/GPU Instanced Culling")]
public partial class GpuInstancedCullingNode : RenderPipelineNode
{
    private static readonly uint[] emptyCounter = new uint[1];

    [SerializeField] private bool isShadow;

    [Input] private RenderTargetIdentifier hiZTexture;
    [Input] private Vector4Array cullingPlanes;
    [Input] private int cullingPlanesCount;
    [Input] private GpuInstanceBuffers gpuInstanceBuffers;

    [Input, Output] private NodeConnection connection;

    private ComputeShader clearShader, cullingShader, scanShader, compactShader;
    private ComputeBuffer memoryCounterBuffer;

    public override void Initialize()
    {
        clearShader = Resources.Load<ComputeShader>("GPU Driven Rendering/InstanceClear");
        cullingShader = Resources.Load<ComputeShader>("GPU Driven Rendering/InstanceRendererCull");
        scanShader = Resources.Load<ComputeShader>("GPU Driven Rendering/InstanceScan");
        compactShader = Resources.Load<ComputeShader>("GPU Driven Rendering/InstanceCompaction");

        memoryCounterBuffer = new ComputeBuffer(1, sizeof(uint));
    }

    public override void Cleanup()
    {
        GraphicsUtilities.SafeDestroy(ref memoryCounterBuffer);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        if ((camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game) || gpuInstanceBuffers.rendererInstanceIDsBuffer == null)
        {
            return;
        }

        using var scope = context.ScopedCommandBuffer("Indirect Rendering", true);

        var viewMatrix = camera.worldToCameraMatrix;
        viewMatrix.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));

        var screenMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * viewMatrix;

        using (var profilerScope = scope.Command.ProfilerScope("Clear"))
        {
            scope.Command.SetComputeBufferParam(clearShader, 0, "_Result", gpuInstanceBuffers.rendererInstanceIDsBuffer);
            scope.Command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.rendererInstanceIDsBuffer.count, 1, 1);
            scope.Command.SetComputeBufferParam(clearShader, 0, "_Result", gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer);
            scope.Command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer.count, 1, 1);
            scope.Command.SetComputeBufferParam(clearShader, 0, "_Result", gpuInstanceBuffers.rendererCountsBuffer);
            scope.Command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.rendererCountsBuffer.count, 1, 1);
            scope.Command.SetComputeBufferParam(clearShader, 0, "_Result", gpuInstanceBuffers.finalRendererCountsBuffer);
            scope.Command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.finalRendererCountsBuffer.count, 1, 1);
            scope.Command.SetComputeBufferParam(clearShader, 0, "_Result", gpuInstanceBuffers.visibleRendererInstanceIndicesBuffer);
            scope.Command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.visibleRendererInstanceIndicesBuffer.count, 1, 1);
        }

        // Culling shader
        using (var profilerScope = scope.Command.ProfilerScope("Instance Cull"))
        {
            scope.Command.SetBufferData(memoryCounterBuffer, emptyCounter);

            scope.Command.SetComputeBufferParam(cullingShader, 0, "_Positions", gpuInstanceBuffers.positionsBuffer);
            scope.Command.SetComputeBufferParam(cullingShader, 0, "_InstanceTypes", gpuInstanceBuffers.instanceTypeIdsBuffer);
            scope.Command.SetComputeBufferParam(cullingShader, 0, "_LodFades", gpuInstanceBuffers.lodFadesBuffer);
            scope.Command.SetComputeBufferParam(cullingShader, 0, "_RendererBounds", gpuInstanceBuffers.rendererBoundsBuffer);
            scope.Command.SetComputeBufferParam(cullingShader, 0, "_RendererInstanceIDs", gpuInstanceBuffers.rendererInstanceIDsBuffer);
            scope.Command.SetComputeBufferParam(cullingShader, 0, "_RendererCounts", gpuInstanceBuffers.rendererCountsBuffer);
            scope.Command.SetComputeBufferParam(cullingShader, 0, "_LodSizes", gpuInstanceBuffers.lodSizesBuffer);
            scope.Command.SetComputeBufferParam(cullingShader, 0, "_InstanceTypeData", gpuInstanceBuffers.instanceTypeDataBuffer);
            scope.Command.SetComputeBufferParam(cullingShader, 0, "_InstanceTypeLodData", gpuInstanceBuffers.instanceTypeLodDataBuffer);

            scope.Command.SetComputeTextureParam(cullingShader, 0, "_CameraMaxZTexture", hiZTexture);

            scope.Command.SetComputeMatrixParam(cullingShader, "_ScreenMatrix", screenMatrix);
            scope.Command.SetComputeVectorArrayParam(cullingShader, "_CullingPlanes", cullingPlanes.value);
            scope.Command.SetComputeVectorParam(cullingShader, "_Resolution", new Vector4(1f / camera.Resolution().x, 1f / camera.Resolution().y, camera.Resolution().x, camera.Resolution().y));
            scope.Command.SetComputeIntParam(cullingShader, "_MaxHiZMip", Texture2DExtensions.MipCount(camera.Resolution().x, camera.Resolution().y) - 1);
            scope.Command.SetComputeIntParam(cullingShader, "_CullingPlanesCount", cullingPlanesCount);
            scope.Command.SetComputeIntParam(cullingShader, "_InstanceCount", gpuInstanceBuffers.positionsBuffer.count);

            using var hiZScope = scope.Command.KeywordScope("HIZ_ON", !isShadow);
            scope.Command.DispatchNormalized(cullingShader, 0, gpuInstanceBuffers.positionsBuffer.count, 1, 1);
        }

        using (var profilerScope = scope.Command.ProfilerScope("Instance Scan"))
        {
            scope.Command.SetComputeIntParam(scanShader, "_Count", gpuInstanceBuffers.rendererCountsBuffer.count);
            scope.Command.SetComputeBufferParam(scanShader, 0, "_MemoryCounter", memoryCounterBuffer);
            scope.Command.SetComputeBufferParam(scanShader, 0, "_RendererCounts", gpuInstanceBuffers.rendererCountsBuffer);
            scope.Command.SetComputeBufferParam(scanShader, 0, "_SubmeshOffsetLengths", gpuInstanceBuffers.submeshOffsetLengthsBuffer);
            scope.Command.SetComputeBufferParam(scanShader, 0, "_RendererInstanceIndexOffsets", gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer);
            scope.Command.SetComputeBufferParam(scanShader, 0, "_DrawCallArgs", gpuInstanceBuffers.drawCallArgsBuffer);
            scope.Command.DispatchNormalized(scanShader, 0, gpuInstanceBuffers.rendererCountsBuffer.count, 1, 1);
        }

        using (var profilerScope = scope.Command.ProfilerScope("Instance Compact"))
        {
            scope.Command.SetComputeIntParam(compactShader, "_RendererInstanceIDsCount", gpuInstanceBuffers.positionsBuffer.count);
            scope.Command.SetComputeBufferParam(compactShader, 0, "_RendererInstanceIDs", gpuInstanceBuffers.rendererInstanceIDsBuffer);
            scope.Command.SetComputeBufferParam(compactShader, 0, "_RendererInstanceIndexOffsets", gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer);
            scope.Command.SetComputeBufferParam(compactShader, 0, "_FinalRendererCounts", gpuInstanceBuffers.finalRendererCountsBuffer);
            scope.Command.SetComputeBufferParam(compactShader, 0, "_VisibleRendererInstanceIndices", gpuInstanceBuffers.visibleRendererInstanceIndicesBuffer);
            scope.Command.SetComputeBufferParam(compactShader, 0, "_InstanceTypeIds", gpuInstanceBuffers.instanceTypeIdsBuffer);
            scope.Command.SetComputeBufferParam(compactShader, 0, "_InstanceTypeData", gpuInstanceBuffers.instanceTypeDataBuffer);
            scope.Command.SetComputeBufferParam(compactShader, 0, "_InstanceTypeLodData", gpuInstanceBuffers.instanceTypeLodDataBuffer);
            scope.Command.DispatchNormalized(compactShader, 0, gpuInstanceBuffers.positionsBuffer.count, 1, 1);
        }
    }
}
