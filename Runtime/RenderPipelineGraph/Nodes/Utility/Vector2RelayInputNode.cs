using UnityEngine;

namespace NodeGraph
{
    [NodeMenuItem("Relay/Vector2 Input")]
    public partial class Vector2RelayInputNode : RelayInputNode<Vector2>
    {
        [Input] private Vector2 input;

        public override Vector2 GetValue()
        {
            return input;
        }
    }
}