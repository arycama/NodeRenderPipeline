using System;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Terrain/Draw Terrain")]
public partial class DrawTerrainNode : RenderPipelineNode
{
    [SerializeField] private bool isShadow = false;

    [Input, Output] private NodeConnection connection;

    public static event Action<CommandBuffer, string, Vector3> Render;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Terrain Render", true);
        var passName = isShadow ? "ShadowCaster" : "Terrain";
        Render?.Invoke(scope.Command, passName, camera.transform.position);
    }
}
