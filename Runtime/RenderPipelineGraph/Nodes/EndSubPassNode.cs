using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Setup/End Sub Pass")]
public partial class EndSubPassNode : RenderPipelineNode
{
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        context.EndSubPass();
    }
}