using UnityEngine;

namespace NodeGraph
{
    [NodeMenuItem("Relay/Vector3 Input")]
    public partial class Vector3RelayInputNode : RelayInputNode<Vector3>
    {
        [Input] private Vector3 input;

        public override Vector3 GetValue()
        {
            return input;
        }
    }
}