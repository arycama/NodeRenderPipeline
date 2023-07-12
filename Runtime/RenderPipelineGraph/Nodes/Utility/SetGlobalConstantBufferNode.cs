using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Utility/Set Global Constant Buffer")]
public partial class SetGlobalConstantBufferNode : RenderPipelineNode
{
    [Input] private GraphicsBuffer buffer;
    [SerializeField] private string id;

    [Input, Output] private NodeConnection connection;

    private int nameId;

    public override void Initialize()
    {
        nameId = Shader.PropertyToID(id);
    }

    public override void NodeChanged()
    {
        nameId = Shader.PropertyToID(id);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        var size = buffer == null ? 0 : buffer.count * buffer.stride;
        scope.Command.SetGlobalConstantBuffer(buffer, nameId, 0, size);
    }
}
