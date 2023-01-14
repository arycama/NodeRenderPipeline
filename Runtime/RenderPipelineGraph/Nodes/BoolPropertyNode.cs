using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Property/Bool")]
public partial class BoolPropertyNode : RenderPipelineNode
{
    [SerializeField] private string category;
    [SerializeField] private string propertyName;
    [Output] private bool value;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var property = Graph.GetPipelineProperty(category, propertyName);
        value = property == null ? false : property.BoolValue;
    }
}
