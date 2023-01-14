using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Set Global Vector")]
public partial class SetGlobalVectorNode : RenderPipelineNode
{
    [SerializeField] private string propertyName;
    [Input] private Vector4 value;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.SetGlobalVector(propertyName, value);
    }
}