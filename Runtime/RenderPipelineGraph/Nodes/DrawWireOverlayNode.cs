using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Utility/Draw Wire Overlay")]
public partial class DrawWireOverlayNode : RenderPipelineNode
{
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        context.DrawWireOverlay(camera);
    }
}