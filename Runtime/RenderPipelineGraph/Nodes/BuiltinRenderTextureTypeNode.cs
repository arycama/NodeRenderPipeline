using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Utility/Builtin Render Target Type")]
public partial class BuiltinRenderTextureTypeNode : RenderPipelineNode
{
    [SerializeField] private BuiltinRenderTextureType type;
    [Output] private RenderTargetIdentifier result;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        result = type;
    }
}