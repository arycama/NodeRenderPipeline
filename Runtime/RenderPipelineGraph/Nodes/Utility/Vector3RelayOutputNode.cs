using UnityEngine;

namespace NodeGraph
{
    [NodeMenuItem("Relay/Vector3 Output")]
    public partial class Vector3RelayOutputNode : RelayOutputNode<Vector3>
    {
        [Output] private Vector3 input;

        public override void OnUpdateValues()
        {
            base.OnUpdateValues();
            input = Value;
        }
    }
}