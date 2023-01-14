using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Set Global Float")]
public partial class SetGlobalFloatNode : RenderPipelineNode
{
    [SerializeField] private string propertyName;
    [Input] private float value;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.SetGlobalFloat(propertyName, value);
    }
}