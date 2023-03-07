using NodeGraph;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

[NodeMenuItem("Utility/Previous Frame Texture")]
public partial class PreviousFrameTextureNode : RenderPipelineNode
{
    [SerializeField] private GraphicsFormat format = GraphicsFormat.R8G8B8A8_UNorm;
    [SerializeField] private int depth = 0;
    [SerializeField] private bool useMipMap = false;

    [Input] private RenderTargetIdentifier result;
    [Output] private RenderTargetIdentifier previousFrame;

    [Input, Output] private NodeConnection connection;

    private CameraTextureCache cache;

    public override void Initialize()
    {
        cache = new("Previous Frame Texture");
    }

    public override void Cleanup()
    {
        cache.Dispose();
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();

        var descriptor = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, format, depth)
        {
            enableRandomWrite = false,
            useMipMap = useMipMap,
        };

        if(cache.GetTexture(camera, descriptor, out var current, out var previous, FrameCount))
        {
            // If textures did not exist, copy into previous as well as current
            scope.Command.CopyTexture(result, previous);
        }

        scope.Command.CopyTexture(result, current);

        previousFrame = previous;
    }
}
