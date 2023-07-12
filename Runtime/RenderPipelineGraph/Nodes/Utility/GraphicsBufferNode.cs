using NodeGraph;
using UnityEngine;

[NodeMenuItem("Utility/Graphics Buffer")]
public partial class GraphicsBufferNode : RenderPipelineNode
{
    [SerializeField] private GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured;
    [SerializeField] private int count = 1;
    [SerializeField] private int stride = 4;

    [Output] private GraphicsBuffer result;

    public override void Initialize()
    {
        result = new GraphicsBuffer(target, count, stride);
    }

    public override void NodeChanged()
    {
        result?.Release();
        result = new GraphicsBuffer(target, count, stride);
    }

    public override void Cleanup()
    {
        result.Release();
        result = null;
    }
}
