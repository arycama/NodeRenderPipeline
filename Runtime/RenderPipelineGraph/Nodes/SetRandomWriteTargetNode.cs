using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Setup/Set Random Write Target")]
public partial class SetRandomWriteTargetNode : RenderPipelineNode
{
    [SerializeField] private int index;
    [SerializeField] private bool preserveCounterValue;

    [Input] private ComputeBuffer buffer;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.SetRandomWriteTarget(index, buffer, preserveCounterValue);
    }
}