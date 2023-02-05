using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

public partial class TextureGraphNode : RenderPipelineNode
{
    [SerializeField] private TextureGraph textureGraph;

    [Output] private RenderTargetIdentifier result;

    private bool isInitialized, isDirty;

    public override void Initialize()
    {
        textureGraph.Initialize();
        isInitialized = false;
        textureGraph.AddListener(OnGraphModified, 0);
    }

    private void OnGraphModified()
    {
        isDirty = true;
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();

        if (!isInitialized || isDirty)
        {
            textureGraph.Run(scope.Command);
            isDirty = false;
            isInitialized = true;
        }

        result = textureGraph.Result;
    }

    public override void Cleanup()
    {
        textureGraph.Cleanup();
        textureGraph.RemoveListener(OnGraphModified);
    }
}