using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Texture/Exposure")]
public partial class ExposureTextureNode : RenderPipelineNode
{
    [Output] private RenderTargetIdentifier currentFrame;
    [Output] private RenderTargetIdentifier previousFrame;
    [Input, Output] private NodeConnection connection;

    private CameraTextureCache textureCache;

    public override void Initialize()
    {
        textureCache = new("Camera Exposure");
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var descriptor = new RenderTextureDescriptor(1, 1, RenderTextureFormat.RFloat, 0)
        {
            enableRandomWrite = true,
        };

        var wasCreated = textureCache.GetTexture(camera, descriptor, out var texture0, out var texture1, FrameCount);

        // If this is first frame, or a preview camera, fill exposrue textures with white
        if (wasCreated)
        {
            using var scope = context.ScopedCommandBuffer();
            scope.Command.SetRenderTarget(new RenderTargetIdentifier[] { texture0, texture1 }, texture0);
            scope.Command.ClearRenderTarget(false, true, new Color(Mathf.PI * 4, Mathf.PI * 4, Mathf.PI * 4, 1f));
        }

        currentFrame = texture0;
        previousFrame = texture1;
    }

    public override void Cleanup()
    {
        textureCache.Dispose();
    }
}