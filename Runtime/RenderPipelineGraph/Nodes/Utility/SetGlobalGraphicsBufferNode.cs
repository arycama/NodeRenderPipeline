using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Utility/Set Global Graphics Buffer")]
public partial class SetGlobalGraphicsBufferNode : RenderPipelineNode
{
    [SerializeField] private string propertyName;
    [Input] private GraphicsBuffer buffer;

    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.SetGlobalBuffer(propertyName, buffer);
    }
}
