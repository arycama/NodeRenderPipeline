using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Property/Float Property")]
public partial class FloatPropertyNode : RenderPipelineNode
{
    [SerializeField] private string category;
    [SerializeField] private string propertyName;
    [Output] private float value;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var property = Graph.GetPipelineProperty(category, propertyName);
        value = property == null ? default : property.FloatValue;
    }
}
