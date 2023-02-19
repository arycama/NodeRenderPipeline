using NodeGraph;

[NodeMenuItem("Relay/Output/Matrix3x4Buffer Output")]
public partial class Matrix3x4BufferOutputNode : RelayOutputNode<SmartComputeBuffer<Matrix3x4>>
{
    [Output] private SmartComputeBuffer<Matrix3x4> output;

    public override void OnUpdateValues()
    {
        base.OnUpdateValues();
        output = Value;
    }
}
