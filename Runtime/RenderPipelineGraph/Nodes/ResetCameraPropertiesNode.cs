using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Reset camera Properties")]
public partial class ResetCameraPropertiesNode : RenderPipelineNode
{
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();

        // After txaa, remove the camera jitter
        var projection = camera.projectionMatrix;
        projection[0, 2] = 0f;
        projection[1, 2] = 0f;
        camera.projectionMatrix = projection;

        GraphicsUtilities.SetupCameraProperties(scope.Command, FrameCount, camera, context, camera.Resolution());
    }
}
