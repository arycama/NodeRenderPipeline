using NodeGraph;
using UnityEngine;

[NodeMenuItem("Route/Int Route")]
public partial class IntRouteNode : RenderPipelineNode
{
    [Input, Output] int connection;
}
