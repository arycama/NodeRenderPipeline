using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Utility/Draw Gizmos")]
public partial class DrawGizmosNode : RenderPipelineNode
{
    [SerializeField] private GizmoSubset subset = GizmoSubset.PreImageEffects;

    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using (var scope = context.ScopedCommandBuffer())
        {
            GraphicsUtilities.SetupCameraProperties(scope.Command, FrameCount, camera, context, camera.Resolution());
        }

#if UNITY_EDITOR
        if (UnityEditor.Handles.ShouldRenderGizmos())
        {
            context.DrawGizmos(camera, subset);
        }
#endif
    }
}