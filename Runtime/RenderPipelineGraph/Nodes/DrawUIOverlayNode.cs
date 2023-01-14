using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Utility/Draw UI Overlay")]
public partial class DrawUIOverlayNode : RenderPipelineNode
{
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType == CameraType.Game)
            context.DrawUIOverlay(camera);
    }
}