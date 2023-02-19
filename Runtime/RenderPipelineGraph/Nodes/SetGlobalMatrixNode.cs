using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Set Global Matrix")]
public partial class SetGlobalMatrixNode : RenderPipelineNode
{
    [SerializeField] private string propertyName;
    [Input] private Matrix4x4 value;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.SetGlobalMatrix(propertyName, value);
    }
}
