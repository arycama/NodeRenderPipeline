using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Utility/Copy Texture Node")]
public partial class CopyTextureNode : RenderPipelineNode
{
    [Input] private RenderTargetIdentifier input;

    [Input, Output] private RenderTargetIdentifier result;
    [Input, Output] private NodeConnection connection;
    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.CopyTexture(input, result);
    }
}