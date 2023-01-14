using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Property/String")]
public partial class StringPropertyNode : RenderPipelineNode
{
    [SerializeField] private string category;
    [SerializeField] private string propertyName;
    [Output] private string value;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var property = Graph.GetPipelineProperty(category, propertyName);
        value = property == null ? default : property.StringValue;
    }
}
