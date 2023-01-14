using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Set Global Buffer")]
public partial class SetGlobalBufferNode : RenderPipelineNode
{
    [SerializeField] private string propertyName;
    [Input] private ComputeBuffer buffer;

    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.SetGlobalBuffer(propertyName, buffer);
    }
}
