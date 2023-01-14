using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Setup/Render State Block")]
public partial class RenderStateBlockNode : RenderPipelineNode
{
    [SerializeField] private RenderStateMask renderStateMask = RenderStateMask.Nothing;
    [SerializeField] private int stencilReference = 0;

    [Input] private StencilState stencilState;
    [Output] private RenderStateBlock renderStateBlock;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        renderStateBlock = new RenderStateBlock(renderStateMask)
        {
            stencilReference = stencilReference,
            stencilState = stencilState
        };
    }
}
