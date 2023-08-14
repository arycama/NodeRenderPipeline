using System;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Terrain/Cull Terrain")]
public partial class CullTerrainNode : RenderPipelineNode
{
    [Input] private CullingPlanes cullingPlanes;
    [Input, Output] private NodeConnection connection;

    public static event Action<CommandBuffer, Vector3, CullingPlanes> Render;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Terrain Cull", true);
        Render?.Invoke(scope.Command, camera.transform.position, cullingPlanes);
    }
}
