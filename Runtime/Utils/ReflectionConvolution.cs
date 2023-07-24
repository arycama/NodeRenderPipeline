using System;
using UnityEngine;
using UnityEngine.Rendering;

public static class ReflectionConvolution
{
    private static readonly Matrix4x4[] matrices = new Matrix4x4[6];

    public static void Convolve(CommandBuffer command, RenderTargetIdentifier input, RenderTargetIdentifier destination, int resolution, int dstOffset = 0)
    {
        var computeShader = Resources.Load<ComputeShader>("Utility/GGXConvolve");

        // Solid angle associated with a texel of the cubemap.
        var invOmegaP = 6.0f * resolution * resolution / (4.0f * Mathf.PI);

        command.SetComputeFloatParam(computeShader, "InvOmegaP", invOmegaP);
        command.SetComputeTextureParam(computeShader, 0, "Input", input);

        var desc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RGB111110Float)
        {
            autoGenerateMips = false,
            dimension = TextureDimension.Tex2DArray,
            volumeDepth = 6,
            enableRandomWrite = true,
            useMipMap = true
        };

        var tempId = Shader.PropertyToID("_SpecConvTemp");
        command.GetTemporaryRT(tempId, desc);

        const int mipLevels = 6;

        for (var i = 1; i < 7; i++)
        {
            command.SetComputeTextureParam(computeShader, 0, "Result", tempId, i);
            command.SetComputeFloatParam(computeShader, "Level", i);

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

            command.SetComputeIntParam(computeShader, "SampleCount", sampleCount);
            command.SetComputeFloatParam(computeShader, "RcpSampleCount", 1.0f / sampleCount);

            var perceptualRoughness = Mathf.Clamp01(i / (float)mipLevels);
            var mipPerceptualRoughness = Mathf.Clamp01(1.7f / 1.4f - Mathf.Sqrt(2.89f / 1.96f - (2.8f / 1.96f) * perceptualRoughness));
            var mipRoughness = mipPerceptualRoughness * mipPerceptualRoughness;
            command.SetComputeFloatParam(computeShader, "Roughness", mipRoughness);

            for (var j = 0; j < 6; j++)
            {
                var res = new Vector2Int(resolution >> i, resolution >> i);
                var viewToWorld = Matrix4x4.LookAt(Vector3.zero, CoreUtils.lookAtList[j], CoreUtils.upVectorList[j]);
                matrices[j] = Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(res, Vector2.zero, 90f, 1f, viewToWorld, true);
            }

            command.SetComputeMatrixArrayParam(computeShader, "_PixelCoordToViewDirWS", matrices);
            command.DispatchNormalized(computeShader, 0, resolution >> i, resolution >> i, 6);
        }

        var resultDesc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RGB111110Float)
        {
            autoGenerateMips = false,
            dimension = TextureDimension.Cube,
            useMipMap = true
        };

        for (var i = 0; i < 6; i++)
        {
            command.CopyTexture(input, i, 0, tempId, i, 0);
            command.CopyTexture(tempId, i, destination, i + dstOffset);
        }

        command.ReleaseTemporaryRT(tempId);
    }
}