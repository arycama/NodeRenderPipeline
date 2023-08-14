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

    private MaxZProcessor processor;

    public override void Initialize()
    {
        processor = new();
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Generate MaxZ", true);
        processor.Execute(scope.Command, camera.pixelWidth, camera.pixelHeight, mode, depthBuffer, maxZBuffer);
    }
}

public enum HiZMode
{
    Min,
    Max,
    CheckerMinMax
}

public class MaxZProcessor
{
    private ComputeShader computeShader;
    private IndexedShaderPropertyId resultIds = new("_Result");

    public MaxZProcessor()
    {
        computeShader = Resources.Load<ComputeShader>("Utility/MaxZ");
    }

    public void Execute(CommandBuffer command, int width, int height, HiZMode mode, RenderTargetIdentifier input, RenderTargetIdentifier result)
    {
        var kernel = (int)mode * 2;
        command.SetComputeTextureParam(computeShader, kernel, "_Input", input);

        var mipCount = Texture2DExtensions.MipCount(width, height);

        var maxMipsPerPass = 6;
        var hasSecondPass = mipCount > maxMipsPerPass;

        // First pass
        {
            command.SetComputeIntParam(computeShader, "_Width", width);
            command.SetComputeIntParam(computeShader, "_Height", height);
            command.SetComputeIntParam(computeShader, "_MaxMip", hasSecondPass ? maxMipsPerPass : mipCount);

            for (var i = 0; i < maxMipsPerPass; i++)
            {
                var texture = i < mipCount ? result : CoreUtils.emptyUAV;
                var mip = i < mipCount ? i : 0;
                command.SetComputeTextureParam(computeShader, kernel, resultIds.GetProperty(i), texture, mip);
            }

            command.DispatchNormalized(computeShader, kernel, width, height, 1);
        }

        // Second pass if needed
        if (hasSecondPass)
        {
            command.SetComputeIntParam(computeShader, "_Width", width >> (maxMipsPerPass - 1));
            command.SetComputeIntParam(computeShader, "_Height", height >> (maxMipsPerPass - 1));
            command.SetComputeIntParam(computeShader, "_MaxMip", mipCount - maxMipsPerPass);

            for (var i = 0; i < maxMipsPerPass; i++)
            {
                var level = i + maxMipsPerPass - 1;
                var texture = level < mipCount ? result : CoreUtils.emptyUAV;
                var mip = level < mipCount ? level : 0;

                // Start from maxMips - 1, as we bind the last mip from the last pass as the first input for this pass
                command.SetComputeTextureParam(computeShader, kernel + 1, resultIds.GetProperty(i), texture, mip);
            }

            command.DispatchNormalized(computeShader, kernel + 1, width >> (maxMipsPerPass - 1), height >> (maxMipsPerPass - 1), 1);
        }
    }
}