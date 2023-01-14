using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Route/Rtid Route")]
public partial class RtidRouteNode : RenderPipelineNode
{
    [Input, Output] RenderTargetIdentifier connection;
}
