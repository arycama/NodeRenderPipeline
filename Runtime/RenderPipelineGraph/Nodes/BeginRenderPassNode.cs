using NodeGraph;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Setup/Begin Render Pass")]
public partial class BeginRenderPassNode : RenderPipelineNode
{
    [Input, SerializeField] private int samples = 1;
    [Input, SerializeField] private int depthAttachmentIndex = -1;

    [Input] private int width;
    [Input] private int height;

    [InputArray] private AttachmentDescriptor[] attachmentDescriptors;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var width = NodeIsConnected("width") ? this.width : camera.pixelWidth;
        var height = NodeIsConnected("height") ? this.height : camera.pixelHeight;

        using var attachmentDescriptors = new NativeArray<AttachmentDescriptor>(this.attachmentDescriptors, Allocator.Temp);
        context.BeginRenderPass(width, height, samples, attachmentDescriptors, depthAttachmentIndex);
    }
}
