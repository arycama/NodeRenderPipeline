using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Water/Water Cull")]
public partial class WaterCullNode : RenderPipelineNode
{
    [Input] private CullingPlanes cullingPlanes;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var command = context.ScopedCommandBuffer("Water Cull", true);
        foreach (var waterRenderer in WaterRenderer.WaterRenderers)
            waterRenderer.Cull(command.Command, camera.transform.position, cullingPlanes);
    }
}
