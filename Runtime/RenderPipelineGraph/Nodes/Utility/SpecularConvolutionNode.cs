using System;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Utility/Specular Convolution")]
public partial class SpecularConvolutionNode : RenderPipelineNode
{
    private static readonly Matrix4x4[] matrices = new Matrix4x4[6];

    [SerializeField, Input, Pow2(512)] private int resolution = 128;

    [Input, Output] private RenderTargetIdentifier input;
    [Output] private RenderTargetIdentifier result;
    [Input, Output] private NodeConnection connection;

    private int propertyId;

    private ComputeShader computeShader;

    public override void Initialize()
    {
        computeShader = Resources.Load<ComputeShader>("Utility/GGXConvolve");
        propertyId = GetShaderPropertyId("Sky Reflection");
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        // Solid angle associated with a texel of the cubemap.
        var invOmegaP = 6.0f * resolution * resolution / (4.0f * Mathf.PI);

        using var scope = context.ScopedCommandBuffer("Cubemap Specular Convolution", true);
        scope.Command.SetComputeFloatParam(computeShader, "InvOmegaP", invOmegaP);
        scope.Command.SetComputeTextureParam(computeShader, 0, "Input", input);

        var desc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RGB111110Float)
        {
            autoGenerateMips = false,
            dimension = TextureDimension.Tex2DArray,
            volumeDepth = 6,
            enableRandomWrite = true,
            useMipMap = true
        };

        var tempId = Shader.PropertyToID("_SpecConvTemp");
        scope.Command.GetTemporaryRT(tempId, desc);

        const int mipLevels = 6;

        for (var i = 1; i < 7; i++)
        {
            scope.Command.SetComputeTextureParam(computeShader, 0, "Result", tempId, i);
            scope.Command.SetComputeFloatParam(computeShader, "Level", i);

            // Different sample counts depending on mip level
            var sampleCount = i switch
            {
                1 => 21,
                2 => 34,
                3 => 55,
                4 => 89,
                5 => 89,
                6 => 89,
                _ => throw new InvalidOperationException(),
            };

            scope.Command.SetComputeIntParam(computeShader, "SampleCount", sampleCount);
            scope.Command.SetComputeFloatParam(computeShader, "RcpSampleCount", 1.0f / sampleCount);

            var perceptualRoughness = Mathf.Clamp01(i / (float)mipLevels);
            var mipPerceptualRoughness = Mathf.Clamp01(1.7f / 1.4f - Mathf.Sqrt(2.89f / 1.96f - (2.8f / 1.96f) * perceptualRoughness));
            var mipRoughness = mipPerceptualRoughness * mipPerceptualRoughness;
            scope.Command.SetComputeFloatParam(computeShader, "Roughness", mipRoughness);

            for (var j = 0; j < 6; j++)
            {
                var res = new Vector2Int(resolution >> i, resolution >> i);
                var viewToWorld = Matrix4x4.LookAt(Vector3.zero, CoreUtils.lookAtList[j], CoreUtils.upVectorList[j]);
                matrices[j] = Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(res, Vector2.zero, 90f, 1f, viewToWorld, true);
            }

            scope.Command.SetComputeMatrixArrayParam(computeShader, "_PixelCoordToViewDirWS", matrices);
            scope.Command.DispatchNormalized(computeShader, 0, resolution >> i, resolution >> i, 6);
        }

        var resultDesc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RGB111110Float)
        {
            autoGenerateMips = false,
            dimension = TextureDimension.Cube,
            useMipMap = true
        };

        scope.Command.GetTemporaryRT(propertyId, resultDesc);

        for (var i = 0; i < 6; i++)
        {
            scope.Command.CopyTexture(input, i, 0, tempId, i, 0);
            scope.Command.CopyTexture(tempId, i, propertyId, i);
        }

        result = propertyId;
        scope.Command.ReleaseTemporaryRT(tempId);
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(propertyId);
    }
}
