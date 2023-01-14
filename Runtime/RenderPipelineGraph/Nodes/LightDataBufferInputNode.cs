using NodeGraph;
using UnityEngine;

[NodeMenuItem("Relay/Input/LightDataBuffer Input")]
public partial class LightDataBufferInputNode : RelayInputNode<SmartComputeBuffer<LightData>>
{
    [Input] private SmartComputeBuffer<LightData> input;

    public override SmartComputeBuffer<LightData> GetValue() => input;
}
