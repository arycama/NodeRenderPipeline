using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Route/CullingResults Route")]
public partial class CullingResultsRouteNode : RenderPipelineNode
{
    [Input, Output] CullingResults connection;
}
