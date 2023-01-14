using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/UI Blur")]
public partial class UIBlurNode : RenderPipelineNode
{
    [SerializeField]
    private RenderTextureFormat format = RenderTextureFormat.RGB111110Float;

    [SerializeField] private bool sRGB;

    [SerializeField, Range(0, 64)]
    private int blurRadius = 8;

    [SerializeField, Min(0f)]
    private float strength = 3;

    [SerializeField, Range(0, 4)]
    private int blurDownsample = 1;

    [Input] private RenderTargetIdentifier input;
    [Output] private RenderTargetIdentifier result;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();

        var uiBlurId = Shader.PropertyToID("_GrabBlurTexture");
        scope.Command.GetTemporaryRT(uiBlurId, camera.pixelWidth >> blurDownsample, camera.pixelHeight >> blurDownsample, 0, FilterMode.Bilinear, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Default, 1, true);

        if (blurRadius < 1)
        {
            scope.Command.Blit(input, uiBlurId);
            return;
        }


        var width = camera.pixelWidth >> blurDownsample;
        var height = camera.pixelHeight >> blurDownsample;

        var gaussianBlurTempId = Shader.PropertyToID("_GaussianBlurTemp");
        scope.Command.GetTemporaryRT(gaussianBlurTempId, width, height, 0, FilterMode.Bilinear, format, sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear, 1, true);

        var computeShader = Resources.Load<ComputeShader>("GaussianBlur");
        scope.Command.SetComputeFloatParam(computeShader, "Radius", blurRadius);
        scope.Command.SetComputeFloatParam(computeShader, "Sigma", 1f / (2.0f * strength * strength));
        scope.Command.SetComputeTextureParam(computeShader, 0, "Input", input);
        scope.Command.SetComputeTextureParam(computeShader, 0, "Result", gaussianBlurTempId);
        scope.Command.SetComputeVectorParam(computeShader, "Direction", new Vector2(1f, 0f));
        scope.Command.SetComputeVectorParam(computeShader, "ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset(width, height));
        scope.Command.SetComputeVectorParam(computeShader, "TexelSize", new Vector4(1f / width, 1f / height, width, height));
        scope.Command.DispatchNormalized(computeShader, 0, width, height, 1);
        scope.Command.SetComputeTextureParam(computeShader, 0, "Input", gaussianBlurTempId);
        scope.Command.SetComputeTextureParam(computeShader, 0, "Result", uiBlurId);
        scope.Command.SetComputeVectorParam(computeShader, "Direction", new Vector2(0f, 1f));

        using var keywordScope = scope.Command.KeywordScope("SRGB");
        scope.Command.DispatchNormalized(computeShader, 0, width, height, 1);
        scope.Command.ReleaseTemporaryRT(gaussianBlurTempId);

        result = uiBlurId;
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(Shader.PropertyToID("_GrabBlurTexture"));
    }
}