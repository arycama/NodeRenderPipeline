using NodeGraph;
using UnityEngine;

[NodeMenuItem("Relay/Input/Matrix3x4Buffer Input")]
public partial class Matrix3x4BufferInputNode : RelayInputNode<SmartComputeBuffer<Matrix3x4>>
{
    [Input] private SmartComputeBuffer<Matrix3x4> input;

    public override SmartComputeBuffer<Matrix3x4> GetValue() => input;
}
