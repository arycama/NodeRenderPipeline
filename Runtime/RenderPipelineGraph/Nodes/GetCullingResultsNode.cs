using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Core/Get Culling Results")]
public partial class GetCullingResultsNode : RenderPipelineNode
{

    [SerializeField, Input, Output] private float shadowDistance;
    [SerializeField] private CullingOptions cullingOptions = CullingOptions.None;

    [Output] private bool isValid;
    [Output] private CullingResults cullingResults;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        isValid = camera.TryGetCullingParameters(out var cullingParameters);

        if (!isValid)
        {
            Debug.LogError("Culling Results invalid");
            return;
        }

        cullingParameters.shadowDistance = shadowDistance;
        cullingParameters.cullingOptions = cullingOptions;

        cullingResults = context.Cull(ref cullingParameters);
    }
}
