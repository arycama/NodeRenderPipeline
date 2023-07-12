using NodeGraph;
using UnityEngine;

[NodeMenuItem("Relay/Graphics Buffer Output")]
public partial class GraphicsBufferRelayOutputNode : RelayOutputNode<GraphicsBuffer>
{
    [Output] private GraphicsBuffer input;

    public override void OnUpdateValues()
    {
        base.OnUpdateValues();
        input = Value;
    }
}