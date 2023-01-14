using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Property/Int")]
public partial class IntPropertyNode : RenderPipelineNode
{
    [SerializeField] private string category;
    [SerializeField] private string propertyName;
    [Output] private int value;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var property = Graph.GetPipelineProperty(category, propertyName);
        value = property == null ? default : property.IntValue;
    }
}
