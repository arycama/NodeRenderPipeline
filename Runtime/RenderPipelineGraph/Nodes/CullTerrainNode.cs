using System;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Terrain/Cull Terrain")]
public partial class CullTerrainNode : RenderPipelineNode
{
    [Input] private Vector4Array cullingPlanes;
    [Input] private int cullingPlanesCount;

    [Input, Output] private NodeConnection connection;

    public static event Action<CommandBuffer, Vector3, Vector4[], int> Render;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Terrain Cull", true);
        Render?.Invoke(scope.Command, camera.transform.position, cullingPlanes.value, cullingPlanesCount);
    }
}
