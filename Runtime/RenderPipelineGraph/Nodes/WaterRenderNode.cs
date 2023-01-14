using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Water/Water Render")]
public partial class WaterRenderNode : RenderPipelineNode
{
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var command = context.ScopedCommandBuffer("Water Render", true);
        foreach (var waterRenderer in WaterRenderer.WaterRenderers)
            waterRenderer.Render(command.Command, "Water", camera.transform.position);
    }
}
