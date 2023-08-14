using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Setup/Render State Block")]
public partial class RenderStateBlockNode : RenderPipelineNode
{
    [SerializeField] private RenderStateMask renderStateMask = RenderStateMask.Nothing;
    [SerializeField] private int stencilReference = 0;
    [SerializeField] private bool conservativeRasterisation = false;

    [Input] private StencilState stencilState;
    [Output] private RenderStateBlock renderStateBlock;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        renderStateBlock = new RenderStateBlock(renderStateMask)
        {
            stencilReference = stencilReference,
            stencilState = stencilState,
        };

        if(conservativeRasterisation)
        {
            var state = new RasterState
            {
                conservative = true,
                cullingMode = CullMode.Off,
                depthClip = true,
                offsetFactor = 0,
                offsetUnits = 0
            };

            renderStateBlock.rasterState = state;
        }
    }
}
