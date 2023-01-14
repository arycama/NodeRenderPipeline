using NodeGraph;
using UnityEngine;

[NodeMenuItem("Relay/Input/DirectionaLightDataBuffer Input")]
public partial class DirectionalLightDataBufferInputNode : RelayInputNode<SmartComputeBuffer<DirectionalLightData>>
{
    [Input] private SmartComputeBuffer<DirectionalLightData> input;

    public override SmartComputeBuffer<DirectionalLightData> GetValue() => input;
}
