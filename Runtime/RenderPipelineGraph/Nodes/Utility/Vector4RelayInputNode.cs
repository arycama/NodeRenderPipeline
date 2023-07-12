using UnityEngine;

namespace NodeGraph
{
    [NodeMenuItem("Relay/Vector4 Input")]
    public partial class Vector4RelayInputNode : RelayInputNode<Vector4>
    {
        [Input] private Vector4 input;

        public override Vector4 GetValue()
        {
            return input;
        }
    }
}