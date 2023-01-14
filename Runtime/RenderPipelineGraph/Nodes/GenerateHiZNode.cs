using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Utility/Generate HiZ")]
public partial class GenerateHiZNode : RenderPipelineNode
{
    [Input] private RenderTargetIdentifier depthBuffer;

    [Output] private RenderTargetIdentifier minZBuffer;
    [Output] private RenderTargetIdentifier maxZBuffer;
    [Output] private RenderTargetIdentifier checkerboardMinMaxZBuffer;
    [Input, Output] private NodeConnection connection;

    private int minZTextureId, maxZTextureId, checkerZTextureId, tempDepth;
    private ComputeShader checkerCS;

    public override void Initialize()
    {
        checkerCS = Resources.Load<ComputeShader>("Utility/CheckerboardMinMaxZ");

        var instanceId = GetInstanceID();
        minZTextureId = GetShaderPropertyId("MinZ");
        maxZTextureId = GetShaderPropertyId("MaxZ");
        checkerZTextureId = GetShaderPropertyId("CheckerZ");
        tempDepth = GetShaderPropertyId("TempDepth");

        minZBuffer = minZTextureId;
        maxZBuffer = maxZTextureId;
        checkerboardMinMaxZBuffer = checkerZTextureId;
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        //return;
        using var scope = context.ScopedCommandBuffer("Generate HiZ");

        var visiblityDesc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.RFloat, 0)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            useMipMap = true
        };

        scope.Command.GetTemporaryRT(tempDepth, camera.pixelWidth, camera.pixelHeight, 32, FilterMode.Point, RenderTextureFormat.Depth);
        scope.Command.CopyTexture(depthBuffer, tempDepth);

        scope.Command.GetTemporaryRT(minZTextureId, visiblityDesc);
        scope.Command.GetTemporaryRT(maxZTextureId, visiblityDesc);

        using (var minMaxProfileScope = scope.Command.ProfilerScope("Min Max Z"))
            GraphicsUtilities.GenerateMinMaxHiZ(scope.Command, camera.pixelWidth, camera.pixelHeight, tempDepth, minZTextureId, maxZTextureId, false);

        scope.Command.GetTemporaryRT(checkerZTextureId, visiblityDesc);
        var mipCount = Texture2DExtensions.MipCount(camera.pixelWidth, camera.pixelHeight);
        for (var i = 0; i < mipCount; i++)
        {
            scope.Command.SetComputeTextureParam(checkerCS, 0, "_MinZ", minZTextureId, i);
            scope.Command.SetComputeTextureParam(checkerCS, 0, "_MaxZ", maxZTextureId, i);
            scope.Command.SetComputeTextureParam(checkerCS, 0, "_Result", checkerZTextureId, i);

            using (var checkerboardZProfileScope = scope.Command.ProfilerScope("Checkerboard Z"))
                scope.Command.DispatchNormalized(checkerCS, 0, camera.pixelWidth >> i, camera.pixelHeight >> i, 1);
        }

        scope.Command.ReleaseTemporaryRT(tempDepth);
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(checkerZTextureId);
        scope.Command.ReleaseTemporaryRT(minZTextureId);
        scope.Command.ReleaseTemporaryRT(maxZTextureId);
    }
}
