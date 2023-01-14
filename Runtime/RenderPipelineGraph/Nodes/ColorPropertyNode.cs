using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Property/Color")]
public partial class ColorPropertyNode : RenderPipelineNode
{
    [SerializeField] private string category;
    [SerializeField] private string propertyName;
    [Output] private Color value;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var property = Graph.GetPipelineProperty(category, propertyName);
        value = property == null ? default : property.ColorValue;
    }
}
