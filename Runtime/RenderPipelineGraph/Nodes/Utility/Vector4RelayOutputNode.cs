using UnityEngine;

namespace NodeGraph
{
    [NodeMenuItem("Relay/Vector4 Output")]
    public partial class Vector4RelayOutputNode : RelayOutputNode<Vector4>
    {
        [Output] private Vector4 input;

        public override void OnUpdateValues()
        {
            base.OnUpdateValues();
            input = Value;
        }
    }
}