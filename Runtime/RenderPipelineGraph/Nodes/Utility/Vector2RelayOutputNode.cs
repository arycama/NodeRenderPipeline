using UnityEngine;

namespace NodeGraph
{
    [NodeMenuItem("Relay/Vector2 Output")]
    public partial class Vector2RelayOutputNode : RelayOutputNode<Vector2>
    {
        [Output] private Vector2 input;

        public override void OnUpdateValues()
        {
            base.OnUpdateValues();
            input = Value;
        }
    }
}