using NodeGraph;
using UnityEngine;

[NodeMenuItem("Route/Float Route")]
public partial class FloatRouteNode : RenderPipelineNode
{
    [Input, Output] float connection;
}
