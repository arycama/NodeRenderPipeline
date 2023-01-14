using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Set Global Texture")]
public partial class SetGlobalTextureNode : RenderPipelineNode
{
    [SerializeField] private string propertyName;
    [Input] private RenderTargetIdentifier texture;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.SetGlobalTexture(propertyName, texture);
    }
}