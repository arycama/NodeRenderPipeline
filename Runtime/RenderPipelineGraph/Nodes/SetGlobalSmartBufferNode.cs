using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Set Global SmartBuffer")]
public partial class SetGlobalSmartBufferNode : RenderPipelineNode
{
    [SerializeField] private string propertyName;
    [Input] private SmartComputeBuffer buffer;

    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.SetGlobalBuffer(propertyName, buffer);
    }
}