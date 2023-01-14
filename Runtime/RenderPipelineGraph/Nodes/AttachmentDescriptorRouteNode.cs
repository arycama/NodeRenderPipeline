using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Route/AttachmentDescriptor Route")]
public partial class AttachmentDescriptorRouteNode : RenderPipelineNode
{
    [Input, Output] AttachmentDescriptor connection;
}
