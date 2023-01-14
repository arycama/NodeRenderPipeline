using NodeGraph;

[NodeMenuItem("Relay/Output/LightDataBuffer Output")]
public partial class LightDataBufferOutputNode : RelayOutputNode<SmartComputeBuffer<LightData>>
{
    [Output] private SmartComputeBuffer<LightData> output;

    public override void OnUpdateValues()
    {
        base.OnUpdateValues();
        output = Value;
    }
}
