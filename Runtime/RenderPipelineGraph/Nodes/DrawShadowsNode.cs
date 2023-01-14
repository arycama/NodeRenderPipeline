using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Drawing/Draw Shadows")]
public partial class DrawShadowsNode : RenderPipelineNode
{
    [Input] private bool renderShadowCasters;
    [Input] private ShadowDrawingSettings shadowDrawingSettings;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        if (renderShadowCasters)
            context.DrawShadows(ref shadowDrawingSettings);
    }
}
