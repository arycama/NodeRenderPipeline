using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Set Global Buffer and Int")]
public partial class SetGlobalBufferAndIntNode : RenderPipelineNode
{
    [SerializeField] private string bufferPropertyName;
    [SerializeField] private string countPropertyName;

    [Input] private SmartComputeBuffer input;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.SetGlobalBuffer(bufferPropertyName, input);
        scope.Command.SetGlobalInt(countPropertyName, input.Count);
    }
}