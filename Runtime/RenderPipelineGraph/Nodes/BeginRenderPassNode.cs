using NodeGraph;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Setup/Begin Render Pass")]
public partial class BeginRenderPassNode : RenderPipelineNode
{
    [Input, SerializeField] private int samples = 1;
    [Input, SerializeField] private int depthAttachmentIndex = -1;

    [InputArray] private AttachmentDescriptor[] attachmentDescriptors;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var attachmentDescriptors = new NativeArray<AttachmentDescriptor>(this.attachmentDescriptors, Allocator.Temp);

        context.BeginRenderPass(camera.pixelWidth, camera.pixelHeight, samples, attachmentDescriptors, depthAttachmentIndex);
        attachmentDescriptors.Dispose();
    }
}
