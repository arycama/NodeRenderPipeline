using NodeGraph;
using UnityEngine;

[NodeMenuItem("Utility/Compute Buffer")]
public partial class ComputeBufferNode : RenderPipelineNode
{
    [SerializeField] private int count;
    [SerializeField] private int stride;
    [SerializeField] private ComputeBufferType type = ComputeBufferType.Structured;
    [SerializeField] private ComputeBufferMode mode = ComputeBufferMode.Immutable;

    [Output] private ComputeBuffer result;

    public override void Initialize()
    {
        result = new ComputeBuffer(count, stride, type, mode);
    }

    public override void NodeChanged()
    {
        result?.Release();
        result = new ComputeBuffer(count, stride, type, mode);
    }

    public override void Cleanup()
    {
        result.Release();
        result = null;
    }
}
