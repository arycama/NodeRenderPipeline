using NodeGraph;
using UnityEngine;

[NodeMenuItem("Relay/Input/Matrix4x4Buffer Input")]
public partial class Matrix4x4BufferInputNode : RelayInputNode<SmartComputeBuffer<Matrix4x4>>
{
    [Input] private SmartComputeBuffer<Matrix4x4> input;

    public override SmartComputeBuffer<Matrix4x4> GetValue() => input;
}
