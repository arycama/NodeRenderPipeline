using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Setup/Clear Render Target")]
public partial class ClearRenderTargetNode : RenderPipelineNode
{
    [SerializeField] private RTClearFlags rtClearFlags;
    [SerializeField] private Color backgroundColor;
    [SerializeField] private float depth;
    [SerializeField] private uint stencil;

    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ClearRenderTarget(rtClearFlags, backgroundColor, depth, stencil);
    }
}
