using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Screen Space Reflections")]
public partial class ScreenSpaceReflectionsNode : RenderPipelineNode
{
    private static readonly IndexedString noiseIds = new("STBN/Vec2/stbn_vec2_2Dx1D_128x128x64_");

    [SerializeField, Input] private bool isEnabled = true;

    [Header("Tracing")]
    [Input, SerializeField, Range(0f, 1f)] private float brdfBias = 0.3f;
    [Input, SerializeField, Range(0, 1f), Tooltip("Thickness of a pixel")] private float thickness = 0.05f;
    [Input, SerializeField, Range(0, 128)] private int maxSamples = 64;
    [Input, SerializeField, Range(1, 16), Tooltip("Increase for better quality, but slower performance")] private int resolveSamples = 4;
    [Input, SerializeField, Range(0f, 1f)] private float screenFadeDistance = 0.1f;

    [Header("Reprojection")]
    [Input, SerializeField, Min(0), Tooltip("High values reduce flicker but increase ghosting")] private float standardDeviationFactor = 2f;
    [Input, SerializeField, Range(0, 1)] private float temporalBlendMin = 0.85f;
    [Input, SerializeField, Range(0, 1)] private float temporalBlendMax = 0.95f;
    [Input, SerializeField] private float motionScale = 50f;
    [Input, SerializeField, Min(1e-6f)] private float blurSharpness = 0.1f;
    [Input, SerializeField, Range(0f, 32f)] private float blurRadius = 1f;

    // Kind of weird, but should work for now
    [InputNoUpdate] private RenderTargetIdentifier previousFrame;
    [InputNoUpdate] private RenderTargetIdentifier previousDepth;

    [Input] private RenderTargetIdentifier cameraMinZTexture;
    [Input] private RenderTargetIdentifier gBuffer1;
    [Input] private RenderTargetIdentifier gBuffer2;
    [Input] private RenderTargetIdentifier motionVectors;

    [Output] private readonly RenderTargetIdentifier result = resultId;
    [Input, Output] private NodeConnection connection;

    private static readonly int
        intersectId = Shader.PropertyToID("_SsrHitTemp"),
        tempResultId = Shader.PropertyToID("_SsrResultTemp"),
        resultId = Shader.PropertyToID("_SsrResult");

    private CameraTextureCache textureCache, frameCountCache;

    private ComputeShader ssrComputeShader;
    private int intersectKernel, temporalKernel;

    public override void Initialize()
    {
        Shader.EnableKeyword("SCREENSPACE_REFLECTIONS_ON");

        ssrComputeShader = Resources.Load<ComputeShader>("Shaders/ScreenSpaceReflections");

        intersectKernel = ssrComputeShader.FindKernel("Intersect");
        temporalKernel = ssrComputeShader.FindKernel("Temporal");

        textureCache = new("Screen Space Reflections");
        frameCountCache = new("SSR Frame Count");
    }

    public override void Cleanup()
    {
        Shader.DisableKeyword("SCREENSPACE_REFLECTIONS_ON");
        textureCache.Dispose();
        frameCountCache.Dispose();
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Screen Space Reflections", true);
        scope.Command.ToggleKeyword("SCREENSPACE_REFLECTIONS_ON", isEnabled);
        if (!isEnabled)
            return;

        var blueNoise2D = Resources.Load<Texture2D>(noiseIds.GetString(FrameCount % 64));

        var projMatrix = camera.projectionMatrix;
        var jitterX = projMatrix[0, 2];
        var jitterY = projMatrix[1, 2];
        var lensShift = new Vector2(jitterX, jitterY);
        var resolution = new Vector4(camera.pixelWidth, camera.pixelHeight, 1f / camera.pixelWidth, 1f / camera.pixelHeight);

        var viewToWorld = Matrix4x4.Rotate(camera.transform.rotation);
        var mat = Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera.Resolution(), lensShift, camera.fieldOfView, camera.aspect, viewToWorld, false);

        scope.Command.SetComputeMatrixParam(ssrComputeShader, "_PixelCoordToViewDirWS", mat);
        scope.Command.SetComputeFloatParam(ssrComputeShader, "_Thickness", thickness);
        scope.Command.SetComputeFloatParam(ssrComputeShader, "_StdDevFactor", standardDeviationFactor);
        scope.Command.SetComputeFloatParam(ssrComputeShader, "_BrdfBias", brdfBias);
        scope.Command.SetComputeFloatParam(ssrComputeShader, "_Sharpness", blurSharpness);
        scope.Command.SetComputeFloatParam(ssrComputeShader, "_BlurRadius", blurRadius);
        scope.Command.SetComputeIntParam(ssrComputeShader, "_MaxSteps", maxSamples);
        scope.Command.SetComputeIntParam(ssrComputeShader, "_MaxMip", Texture2DExtensions.MipCount(camera.pixelWidth, camera.pixelHeight));

        // Trace into temporary texture, which we can then pass into a temporal pass
        var hitResultDesc = new RenderTextureDescriptor(camera.pixelWidth >> 1, camera.pixelHeight >> 1, RenderTextureFormat.RHalf) { enableRandomWrite = true };
        scope.Command.GetTemporaryRT(intersectId, hitResultDesc);

        var ssrDesc = new RenderTextureDescriptor(camera.pixelWidth >> 1, camera.pixelHeight >> 1, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
        textureCache.GetTexture(camera, ssrDesc, out var temporalResult, out var temporalHistory, FrameCount);

        scope.Command.GetTemporaryRT(tempResultId, ssrDesc);

        var width = camera.pixelWidth >> 1;
        var height = camera.pixelHeight >> 1;

        scope.Command.SetComputeVectorParam(ssrComputeShader, "_Resolution", new Vector2(width, height));
        scope.Command.SetComputeVectorParam(ssrComputeShader, "_ResolutionMinusOne", new Vector2(width - 1, height - 1));

        // Intersect pass
        {
            scope.Command.SetComputeTextureParam(ssrComputeShader, intersectKernel, "_Depth", cameraMinZTexture);
            scope.Command.SetComputeTextureParam(ssrComputeShader, intersectKernel, "_BlueNoise2D", blueNoise2D);
            scope.Command.SetComputeTextureParam(ssrComputeShader, intersectKernel, "_GBuffer1", gBuffer1);
            scope.Command.SetComputeTextureParam(ssrComputeShader, intersectKernel, "_GBuffer2", gBuffer2);
            scope.Command.SetComputeTextureParam(ssrComputeShader, intersectKernel, "_HitResult", intersectId);
            scope.Command.SetComputeTextureParam(ssrComputeShader, intersectKernel, "_Input", previousFrame);
            scope.Command.SetComputeTextureParam(ssrComputeShader, intersectKernel, "_Result", tempResultId);

            using (var profilerScope = scope.Command.ProfilerScope("Intersect"))
                scope.Command.DispatchNormalized(ssrComputeShader, intersectKernel, camera.pixelWidth >> 1, camera.pixelHeight >> 1, 1);
        }

        // Temporal pass
        {
            var frameDesc = new RenderTextureDescriptor(camera.pixelWidth >> 1, camera.pixelHeight >> 1, RenderTextureFormat.R8) { enableRandomWrite = true };
            frameCountCache.GetTexture(camera, frameDesc, out var currentCount, out var previousCount, FrameCount);

            scope.Command.SetComputeTextureParam(ssrComputeShader, temporalKernel, "_Depth", cameraMinZTexture);
            scope.Command.SetComputeTextureParam(ssrComputeShader, temporalKernel, "_PreviousDepth", previousDepth);
            scope.Command.SetComputeTextureParam(ssrComputeShader, temporalKernel, "_HitInput", intersectId);
            scope.Command.SetComputeTextureParam(ssrComputeShader, temporalKernel, "_Input", tempResultId);
            scope.Command.SetComputeTextureParam(ssrComputeShader, temporalKernel, "_History", temporalHistory);
            scope.Command.SetComputeTextureParam(ssrComputeShader, temporalKernel, "_Result", temporalResult);
            scope.Command.SetComputeTextureParam(ssrComputeShader, temporalKernel, "_FrameCountResult", currentCount);
            scope.Command.SetComputeTextureParam(ssrComputeShader, temporalKernel, "_FrameCountPrevious", previousCount);
            scope.Command.SetComputeTextureParam(ssrComputeShader, temporalKernel, "_MotionVectors", motionVectors);

            using (var profilerScope = scope.Command.ProfilerScope("Temporal"))
                scope.Command.DispatchNormalized(ssrComputeShader, temporalKernel, camera.pixelWidth >> 1, camera.pixelHeight >> 1, 1);
        }

        // Upsample
        {
            var upsampleDesc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
            scope.Command.GetTemporaryRT(resultId, upsampleDesc);

            scope.Command.SetComputeTextureParam(ssrComputeShader, 2, "_Input", temporalResult);
            scope.Command.SetComputeTextureParam(ssrComputeShader, 2, "_Result", resultId);
            scope.Command.SetComputeTextureParam(ssrComputeShader, 2, "_Depth", cameraMinZTexture);
            scope.Command.DispatchNormalized(ssrComputeShader, 2, camera.pixelWidth, camera.pixelHeight, 1);
        }

        scope.Command.ReleaseTemporaryRT(intersectId);
        scope.Command.ReleaseTemporaryRT(tempResultId);
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(resultId);
    }
}