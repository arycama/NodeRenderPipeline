using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Route/RenderTargetIdentifier Route")]
public partial class RenderTargetIdentifierRouteNode : RenderPipelineNode
{
    [Input, Output] RenderTargetIdentifier connection;
}
