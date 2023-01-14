using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Set Global Int")]
public partial class SetGlobalIntNode : RenderPipelineNode
{
    [SerializeField] private string propertyName;
    [Input] private int value;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.SetGlobalInt(propertyName, value);
    }
}
