using UnityEngine;
using UnityEngine.Rendering;

public static class ReflectionConvolution
{
    private static readonly Matrix4x4[] matrices = new Matrix4x4[6];

    public static void Convolve(CommandBuffer command, RenderTargetIdentifier source, RenderTargetIdentifier destination, int resolution, int dstOffset = 0)
    {
        // Solid angle associated with a texel of the cubemap.
        var invOmegaP = 6.0f * resolution * resolution / (4.0f * Mathf.PI);

        var ggxConvolve = Resources.Load<ComputeShader>("Utility/GGXConvolve");
        command.SetComputeFloatParam(ggxConvolve, "InvOmegaP", invOmegaP);
        command.SetComputeTextureParam(ggxConvolve, 0, "Input", source);

        var tempId = Shader.PropertyToID("ConvolveTemp");
        var desc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RGB111110Float)
        {
            autoGenerateMips = false,
            dimension = TextureDimension.Tex2DArray,
            volumeDepth = 6,
            enableRandomWrite = true,
            useMipMap = true
        };
        command.GetTemporaryRT(tempId, desc);

        for (var i = 1; i < 7; i++)
        {
            command.SetComputeTextureParam(ggxConvolve, 0, "Result", tempId, i);
            command.SetComputeFloatParam(ggxConvolve, "Level", i);

            for (var j = 0; j < 6; j++)
            {
                var res = new Vector2Int(resolution >> i, resolution >> i);
                var viewToWorld = Matrix4x4.LookAt(Vector3.zero, CoreUtils.lookAtList[j], CoreUtils.upVectorList[j]);
                matrices[j] = Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(res, Vector2.zero, 90f, 1f, viewToWorld, true);
            }

            command.SetComputeMatrixArrayParam(ggxConvolve, "_PixelCoordToViewDirWS", matrices);
            command.DispatchNormalized(ggxConvolve, 0, resolution >> i, resolution >> i, 6);
        }

        for (var i = 0; i < 6; i++)
        {
            command.CopyTexture(source, i, 0, tempId, i, 0);
            command.CopyTexture(tempId, i, destination, i + dstOffset);
        }

        command.ReleaseTemporaryRT(tempId);
    }
}