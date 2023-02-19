using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Setup/Set Render Target")]
public partial class SetRenderTargetNode : RenderPipelineNode
{
    [Input] private RenderTargetIdentifier color;
    [SerializeField] private RenderBufferLoadAction colorLoadAction;
    [SerializeField] private RenderBufferStoreAction colorStoreAction;
    [Input] private RenderTargetIdentifier depth;
    [SerializeField] private RenderBufferLoadAction depthLoadAction;
    [SerializeField] private RenderBufferStoreAction depthStoreAction;

    [Input, Output] private NodeConnection conection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.SetRenderTarget(color, colorLoadAction, colorStoreAction, depth, depthLoadAction, depthStoreAction);
    }
}