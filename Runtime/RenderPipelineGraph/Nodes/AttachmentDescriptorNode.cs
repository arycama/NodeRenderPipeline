using NodeGraph;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

[NodeMenuItem("Setup/Attachment Descriptor")]
public partial class AttachmentDescriptorNode : RenderPipelineNode
{
    [SerializeField] private RenderBufferLoadAction loadAction;
    [SerializeField] private RenderBufferStoreAction storeAction;
    [SerializeField] private GraphicsFormat format;
    [SerializeField] private Color clearColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
    [SerializeField] private float clearDepth = 1.0f;
    [SerializeField] private uint clearStencil = 0u;

    [Input] private RenderTargetIdentifier loadStoreTarget;
    [Input] private RenderTargetIdentifier resolveTarget;

    [Output] private AttachmentDescriptor result;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        result = new AttachmentDescriptor()
        {
            loadAction = loadAction,
            storeAction = storeAction,
            graphicsFormat = format,
            loadStoreTarget = loadStoreTarget,
            resolveTarget = resolveTarget,
            clearColor = clearColor,
            clearDepth = clearDepth,
            clearStencil = clearStencil
        };
    }
}
