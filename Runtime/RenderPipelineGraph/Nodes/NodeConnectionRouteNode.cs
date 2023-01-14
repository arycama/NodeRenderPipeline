using NodeGraph;
using UnityEngine;

[NodeMenuItem("Route/NodeConnection Route")]
public partial class NodeConnectionRouteNode : RenderPipelineNode
{
    [Input, Output] NodeConnection connection;
}
