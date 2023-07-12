using NodeGraph;
using UnityEngine;

[NodeMenuItem("Relay/Graphics Buffer Input")]
public partial class GraphicsBufferRelayInputNode : RelayInputNode<GraphicsBuffer>
{
    [Input] private GraphicsBuffer input;

    public override GraphicsBuffer GetValue()
    {
        return input;
    }
}
