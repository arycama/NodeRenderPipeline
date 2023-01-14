using NodeGraph;
using UnityEngine;

[NodeMenuItem("Route/ComputeBuffer Route")]
public partial class ComputeBufferRouteNode : RenderPipelineNode
{
    [Input, Output] ComputeBuffer connection;
}
