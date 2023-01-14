using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Sky/Celestial Body Render")]
public partial class CelestialBodyNode : RenderPipelineNode
{
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Celestial Body", true);
        foreach (var celestialBody in CelestialBody.CelestialBodies)
            celestialBody.Render(scope.Command, camera);
    }
}
