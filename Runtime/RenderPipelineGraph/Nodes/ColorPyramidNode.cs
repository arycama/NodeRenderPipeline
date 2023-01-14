using NodeGraph;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Color Pyramid")]
public partial class ColorPyramidNode : RenderPipelineNode
{
    [Input] private RenderTargetIdentifier input;
    [Output] private RenderTargetIdentifier result;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Color Pyramid", true);

        var gaussianDownsample = Resources.Load<ComputeShader>("Shaders/GaussianDownsample");

        var id = Shader.PropertyToID("_CameraOpaqueTexture");
        scope.Command.GetTemporaryRT(id, new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.B10G11R11_UFloatPack32, 0) { enableRandomWrite = true, useMipMap = true, autoGenerateMips = false });
        scope.Command.CopyTexture(input, 0, 0, id, 0, 0);

        // Below seems to have issues with reflection probe cameras, just ignore for now... will break blurry refractions though
        // Color pyramid(This should be after deferred lighting)
        var mipCount = Texture2DExtensions.MipCount(camera.pixelWidth, camera.pixelHeight);
        scope.Command.BeginSample("ColorPyramid");
        for (var i = 1; i < mipCount; i++)
        {
            var mipSize = new Vector2Int(camera.pixelWidth >> i, camera.pixelHeight >> i);
            scope.Command.SetComputeVectorParam(gaussianDownsample, "_Size", new Vector4(mipSize.x * 2, mipSize.y * 2, 0, 0));
            scope.Command.SetComputeTextureParam(gaussianDownsample, 0, "_Source", id, i - 1);
            scope.Command.SetComputeTextureParam(gaussianDownsample, 0, "_Destination", id, i);
            scope.Command.DispatchNormalized(gaussianDownsample, 0, mipSize.x, mipSize.y, 1);
        }
        scope.Command.EndSample("ColorPyramid");
        result = id;
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        var id = Shader.PropertyToID("_CameraOpaqueTexture");
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(id);
    }
}