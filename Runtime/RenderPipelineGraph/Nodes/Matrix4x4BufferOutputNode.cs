using NodeGraph;
using UnityEngine;

[NodeMenuItem("Relay/Output/Matrix4x4Buffer Output")]
public partial class Matrix4x4BufferOutputNode : RelayOutputNode<SmartComputeBuffer<Matrix4x4>>
{
    [Output] private SmartComputeBuffer<Matrix4x4> output;

    public override void OnUpdateValues()
    {
        base.OnUpdateValues();
        output = Value;
    }
}
