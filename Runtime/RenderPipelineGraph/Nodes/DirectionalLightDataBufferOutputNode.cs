using NodeGraph;

[NodeMenuItem("Relay/Output/DirectionaLightDataBuffer Output")]
public partial class DirectionalLightDataBufferOutputNode : RelayOutputNode<SmartComputeBuffer<DirectionalLightData>>
{
    [Output] private SmartComputeBuffer<DirectionalLightData> output;

    public override void OnUpdateValues()
    {
        base.OnUpdateValues();
        output = Value;
    }
}
