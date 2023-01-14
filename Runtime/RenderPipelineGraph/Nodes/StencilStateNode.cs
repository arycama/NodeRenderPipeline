using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Setup/Stencil State")]
public partial class StencilStateNode : RenderPipelineNode
{
    [SerializeField] private bool enabled = false;
    [SerializeField] private byte readMask = 255;
    [SerializeField] private byte writeMask = 255;
    [SerializeField] private CompareFunction compareFunctionFront = CompareFunction.Always;
    [SerializeField] private StencilOp passOperationFront = StencilOp.Keep;
    [SerializeField] private StencilOp failOperationFront = StencilOp.Keep;
    [SerializeField] private StencilOp zFailOperationFront = StencilOp.Keep;
    [SerializeField] private CompareFunction compareFunctionBack = CompareFunction.Always;
    [SerializeField] private StencilOp passOperationBack = StencilOp.Keep;
    [SerializeField] private StencilOp failOperationBack = StencilOp.Keep;
    [SerializeField] private StencilOp zFailOperationBack = StencilOp.Keep;

    [Output] private StencilState stencilState;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        stencilState = new StencilState(enabled, readMask, writeMask, compareFunctionFront, passOperationFront, failOperationFront, zFailOperationFront, compareFunctionBack, passOperationBack, failOperationBack, zFailOperationBack);
    }
}