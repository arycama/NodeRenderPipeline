using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Temporal Anti Aliasing")]
public partial class TemporalAntiAliasingNode : RenderPipelineNode
{
    [SerializeField, Min(0f)] private float sharpening = 1f;
    [SerializeField, Range(0f, 1f)] private float historySharpening = 0.25f;

    [Input] private RenderTargetIdentifier depth;
    [Input] private RenderTargetIdentifier motionVectors;

    [Input, Output] private RenderTargetIdentifier result;
    [Input, Output] private NodeConnection connection;

    private CameraTextureCache textureCache, frameCountCache;

    public override void Initialize()
    {
        textureCache = new("Temporal Anti Aliasing");
        frameCountCache = new("TXAA Frame Count");
    }

    public override void Cleanup()
    {
        textureCache.Dispose();
        frameCountCache.Dispose();
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Temporal Anti Aliasing", true);

        var descriptor = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.RGB111110Float) { enableRandomWrite = true };
        if (textureCache.GetTexture(camera, descriptor, out var texture0, out var texture1, FrameCount))
        {
            // If no history texture exists, copy the current camera's color into it so it's available for next frame, and skip remaining txaa
            // Actually we still probably want to do the resolve pass to remove jitter..
            scope.Command.CopyTexture(result, 0, 0, texture0, 0, 0);
        }
        else
        {
            var frameCountDesc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.R8) { enableRandomWrite = true };
            frameCountCache.GetTexture(camera, frameCountDesc, out var newFrameCount, out var prevFrameCount, FrameCount);

            var computeShader = Resources.Load<ComputeShader>("Utility/TemporalAntiAliasing");
            scope.Command.SetComputeFloatParam(computeShader, "_HistorySharpening", historySharpening);
            scope.Command.SetComputeFloatParam(computeShader, "_Sharpening", sharpening);
            scope.Command.SetComputeTextureParam(computeShader, 0, "_Depth", depth);
            scope.Command.SetComputeTextureParam(computeShader, 0, "_Result", texture0);
            scope.Command.SetComputeTextureParam(computeShader, 0, "_Input", result);
            scope.Command.SetComputeTextureParam(computeShader, 0, "_History", texture1);
            scope.Command.SetComputeTextureParam(computeShader, 0, "_MotionVectors", motionVectors);
            scope.Command.SetComputeTextureParam(computeShader, 0, "_PrevFrameCount", prevFrameCount);
            scope.Command.SetComputeTextureParam(computeShader, 0, "_NewFrameCount", newFrameCount);

            computeShader.GetKernelThreadGroupSizes(0, out var xThreads, out var yThreads, out var zThreads);

            // Each thread group is 32x32, but the first/last row are only used to fetch additional data
            var threadGroupsX = (camera.pixelWidth - 1) / ((int)xThreads - 2) + 1;
            var threadGroupsY = (camera.pixelHeight - 1) / ((int)yThreads - 2) + 1;

            scope.Command.DispatchCompute(computeShader, 0, threadGroupsX, threadGroupsY, 1);

            // Copy texture to output
            scope.Command.CopyTexture(texture0, 0, 0, result, 0, 0);
        }
    }
}
