using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Utility/Copy Buffer")]
public partial class CopyBufferNode : RenderPipelineNode
{
    [Input] private GraphicsBuffer source;
    [Input, Output] private GraphicsBuffer destination;

    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        if (source == null)
            return;

        using var scope = context.ScopedCommandBuffer();
        scope.Command.CopyBuffer(source, destination);
    }
}
