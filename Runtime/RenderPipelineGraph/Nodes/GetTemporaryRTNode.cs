using NodeGraph;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

[NodeMenuItem("Utility/Get Temporary RT")]
public partial class GetTemporaryRTNode : RenderPipelineNode
{
    [SerializeField] private GraphicsFormat colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
    [SerializeField] private GraphicsFormat depthFormat = GraphicsFormat.None;
    [SerializeField] private bool enableRandomWrite = false;
    [SerializeField] private bool useMipMap = false;

    [Output] private RenderTargetIdentifier result;

    private int propertyId;

    public override void Initialize()
    {
        propertyId = GetShaderPropertyId();
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        var descriptor = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, colorFormat, depthFormat)
        {
            enableRandomWrite = enableRandomWrite,
            useMipMap = useMipMap,
        };

        scope.Command.GetTemporaryRT(propertyId, descriptor);
        result = propertyId;
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(propertyId);
    }
}