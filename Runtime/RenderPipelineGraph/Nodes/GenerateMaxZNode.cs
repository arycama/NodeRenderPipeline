using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Utility/Generate MaxZ")]
public partial class GenerateMaxZNode : RenderPipelineNode
{
    [SerializeField] private HiZMode mode = HiZMode.Max;

    [Input] private RenderTargetIdentifier depthBuffer;
    [Input] private RenderTargetIdentifier maxZBuffer;
    [Input, Output] private NodeConnection connection;

    private ComputeShader computeShader;
    private IndexedShaderPropertyId resultIds = new("_Result");

    public override void Initialize()
    {
        computeShader = Resources.Load<ComputeShader>("Utility/MaxZ");
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Generate MaxZ", true);

        var visiblityDesc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.RFloat, 0)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            useMipMap = true,
        };

        var kernel = (int)mode * 2;
        scope.Command.SetComputeTextureParam(computeShader, kernel, "_Input", depthBuffer);

        var mipCount = Texture2DExtensions.MipCount(camera.pixelWidth, camera.pixelHeight);

        var maxMipsPerPass = 6;
        var hasSecondPass = mipCount > maxMipsPerPass;

        // First pass
        {
            scope.Command.SetComputeIntParam(computeShader, "_Width", camera.pixelWidth);
            scope.Command.SetComputeIntParam(computeShader, "_Height", camera.pixelHeight);
            scope.Command.SetComputeIntParam(computeShader, "_MaxMip", hasSecondPass ? maxMipsPerPass : mipCount);

            for (var i = 0; i < maxMipsPerPass; i++)
            {
                var texture = i < mipCount ? maxZBuffer : CoreUtils.emptyUAV;
                var mip = i < mipCount ? i : 0;
                scope.Command.SetComputeTextureParam(computeShader, kernel, resultIds.GetProperty(i), texture, mip);
            }

            scope.Command.DispatchNormalized(computeShader, kernel, camera.pixelWidth, camera.pixelHeight, 1);
        }

        // Second pass if needed
        if (hasSecondPass)
        {
            scope.Command.SetComputeIntParam(computeShader, "_Width", camera.pixelWidth >> (maxMipsPerPass - 1));
            scope.Command.SetComputeIntParam(computeShader, "_Height", camera.pixelHeight >> (maxMipsPerPass - 1));
            scope.Command.SetComputeIntParam(computeShader, "_MaxMip", mipCount - maxMipsPerPass);

            for (var i = 0; i < maxMipsPerPass; i++)
            {
                var level = i + maxMipsPerPass - 1;
                var texture = level < mipCount ? maxZBuffer : CoreUtils.emptyUAV;
                var mip = level < mipCount ? level : 0;

                // Start from maxMips - 1, as we bind the last mip from the last pass as the first input for this pass
                scope.Command.SetComputeTextureParam(computeShader, kernel + 1, resultIds.GetProperty(i), texture, mip);
            }

            scope.Command.DispatchNormalized(computeShader, kernel + 1, camera.pixelWidth >> (maxMipsPerPass - 1), camera.pixelHeight >> (maxMipsPerPass - 1), 1);
        }
    }

    private enum HiZMode
    {
        Min,
        Max,
        CheckerMinMax
    }
}
