using System;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Windows;

[NodeMenuItem("Lighting/AutoExposure")]
public partial class AutoExposureNode : RenderPipelineNode
{
    private static readonly IndexedShaderPropertyId bloomIds = new("Bloom"), flareIds = new("Flare"), tempResultIds = new("_TempResult");

    [SerializeField] private bool debugExposure;

    [Header("Bloom")]
    [SerializeField, Range(1, 12)] private int numBloomMips = 5;
    [SerializeField, Range(0f, 1f)] private float bloomStrength = 0.04f;
    [SerializeField, Range(0f, 1f)] private float dirtStrength = 1f;

    [Header("Eye Adaptation")]
    [SerializeField] private AnimationCurve exposureCurve = AnimationCurve.Linear(-10f, -10f, 20f, 20f);
    [SerializeField, Pow2(128)] private int exposureResolution = 32;
    [SerializeField, Tooltip("Sets the minimum value that the Scene exposure can be set to.")] private float limitMin = -1f;
    [SerializeField, Tooltip("Sets the maximum value that the Scene exposure can be set to.")] private float limitMax = 14f;
    [SerializeField, Tooltip("Sets the range of values (in terms of percentages) of the histogram that are accepted while finding a stable average exposure. Anything outside the value is discarded.")] private Vector2 histogramPercentages = new(40f, 90f);
    [SerializeField, Min(0)] private float adaptationSpeed = 1f;

    [Header("Exposure Fusion")]
    [SerializeField] private bool enableLtm = true;
    [SerializeField] private bool boostLocalContrast = false;
    [SerializeField] private float exposure = 1.0f;
    [SerializeField] private float shadows = 1.5f;
    [SerializeField] private float highlights = 2.0f;
    [SerializeField, Range(0, 12)] private int mip = 4;
    [SerializeField, Range(0, 6)] private int displayMip = 2;
    [SerializeField, Range(0.0f, 20.0f)] private float exposurePreferenceSigma = 5.0f;

    [Header("Lens Flare")]
    [SerializeField, Range(0, 10)] private int flareMip = 2;

    [SerializeField, Range(1, 3)] private int distortionQuality = 2;
    [SerializeField, Range(0f, 0.1f)] private float distortion = 0.02f;

    [SerializeField, Range(0f, 32f)] private float ghostStrength = 1f;
    [SerializeField, Range(0, 8)] private int ghostCount = 7;
    [SerializeField, Range(0f, 1f)] private float ghostSpacing = 1.19f;

    [SerializeField, Range(0f, 32f)] private float haloStrength = 1f;
    [SerializeField, Range(0f, 1f)] private float haloRadius = 0.692f;
    [SerializeField, Range(0f, 1f)] private float haloWidth = 0.692f;

    [SerializeField, Range(0f, 1f)] private float streakStrength = 1f;
    [SerializeField] private Texture2D starburstTexture;
    [SerializeField] private Texture2D lensDirt;

    [Input] private RenderTargetIdentifier currentExposure;
    [Input] private RenderTargetIdentifier previousExposure;
    [Input, Output] private RenderTargetIdentifier result;
    [Input, Output] private NodeConnection connection;

    private ComputeBuffer histogramBuffer, debugExposureBuffer;
    private Texture2D exposureTexture;
    private float[] exposurePixels;

    public override void Initialize()
    {
        histogramBuffer = new ComputeBuffer(256, sizeof(uint));
        debugExposureBuffer = new ComputeBuffer(1, sizeof(float));

        // exposureCurve = new AnimationCurve();
        // for(var i = limitMin; i < limitMax; i++)
        // {
        //     var luminance = Mathf.Pow(2f, i - 3f);
        //     var compensation = 1.03f - 2f / (Mathf.Log10(luminance + 1f) + 2f);
        //     var evCompensation = Mathf.Log(100 * compensation / 12.5f, 2f);
        //     exposureCurve.AddKey(i, evCompensation);
        // }

        exposurePixels = new float[exposureResolution];
        for (var i = 0; i < exposureResolution; i++)
        {
            var uv = i / (exposureResolution - 1f);
            var t = Mathf.Lerp(limitMin, limitMax, uv);
            var exposure = exposureCurve.Evaluate(t);
            exposurePixels[i] = exposure;
        }

        exposureTexture = new Texture2D(exposureResolution, 1, TextureFormat.RFloat, false) { hideFlags = HideFlags.HideAndDontSave };
        exposureTexture.SetPixelData(exposurePixels, 0);
        exposureTexture.Apply(false, false);
    }

    public override void NodeChanged()
    {
        // Probably don't need to do this every frame
        for (var i = 0; i < exposureResolution; i++)
        {
            var uv = i / (exposureResolution - 1f);
            var t = Mathf.Lerp(limitMin, limitMax, uv);
            var exposurePixel = exposureCurve.Evaluate(t);
            exposurePixels[i] = exposurePixel;
        }

        exposureTexture.SetPixelData(exposurePixels, 0);
        exposureTexture.Apply(false, false);
    }

    public override void Cleanup()
    {
        histogramBuffer.Release();
        debugExposureBuffer.Release();
        DestroyImmediate(exposureTexture);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Post Processing", true);

        EyeAdaptation(scope, camera);
        Bloom(scope, camera);
        ExposureFusion(scope, camera);
    }

    private void Bloom(ScopedCommandBuffer scope, Camera camera)
    {
        var computeShader = Resources.Load<ComputeShader>("Post Processing/Bloom");
        var mipCount = Mathf.Min(numBloomMips, camera.MipCount() - 1);

        scope.Command.SetComputeFloatParam(computeShader, "_Strength", bloomStrength);
        scope.Command.SetComputeFloatParam(computeShader, "_DirtStrength", dirtStrength);

        // Lens flare 
        scope.Command.SetComputeFloatParam(computeShader, "_DistortionQuality", distortionQuality);
        scope.Command.SetComputeFloatParam(computeShader, "_Distortion", distortion);
        scope.Command.SetComputeFloatParam(computeShader, "_GhostStrength", ghostStrength);
        scope.Command.SetComputeFloatParam(computeShader, "_GhostCount", ghostCount);
        scope.Command.SetComputeFloatParam(computeShader, "_GhostSpacing", ghostSpacing);
        scope.Command.SetComputeFloatParam(computeShader, "_HaloStrength", haloStrength);
        scope.Command.SetComputeFloatParam(computeShader, "_HaloWidth", haloWidth);
        scope.Command.SetComputeFloatParam(computeShader, "_HaloRadius", haloRadius);
        scope.Command.SetComputeFloatParam(computeShader, "_StreakStrength", streakStrength);


        // Downsample
        for (var i = 0; i < mipCount; i++)
        {
            var kernel = i == 0 ? 0 : 1;

            if (i == 0)
                scope.Command.SetComputeTextureParam(computeShader, kernel, "_Input", result);
            else
            {
                var inputId = bloomIds.GetProperty(i - 1);
                scope.Command.SetComputeTextureParam(computeShader, kernel, "_Input", inputId);
            }

            var width = Mathf.Max(1, camera.pixelWidth >> (i + 1));
            var height = Mathf.Max(1, camera.pixelHeight >> (i + 1));
            var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.RGB111110Float) { enableRandomWrite = true };

            var resultId = bloomIds.GetProperty(i);
            scope.Command.GetTemporaryRT(resultId, desc);

            scope.Command.SetComputeTextureParam(computeShader, kernel, "_Result", resultId);
            scope.Command.SetComputeVectorParam(computeShader, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset(width, height));

            var useLensFlare = i == Mathf.Min(mipCount - 1, flareMip);
            if (useLensFlare)
            {
                scope.Command.SetComputeTextureParam(computeShader, kernel, "_Starburst", starburstTexture == null ? Texture2D.whiteTexture : starburstTexture);

                var flareTempId = flareIds.GetProperty(i);
                scope.Command.GetTemporaryRT(flareTempId, desc);
                scope.Command.SetComputeTextureParam(computeShader, kernel, "_FlareResult", flareTempId);
            }

            using var keywordScope = scope.Command.KeywordScope("LENS_FLARE", useLensFlare);

            using (var profilerScope = scope.Command.ProfilerScope("Bloom Downsample"))
                scope.Command.DispatchNormalized(computeShader, kernel, width, height, 1);
        }

        // Upsample
        for (var i = mipCount - 1; i >= 0; i--)
        {
            var kernel = i == 0 ? 3 : 2;

            var inputId = bloomIds.GetProperty(i);
            scope.Command.SetComputeTextureParam(computeShader, kernel, "_Input", inputId);

            if (i > 0)
            {
                var resultId = bloomIds.GetProperty(i - 1);
                scope.Command.SetComputeTextureParam(computeShader, kernel, "_Result", resultId);
            }
            else
            {
                scope.Command.SetComputeTextureParam(computeShader, kernel, "_Result", result);
            }

            var width = Mathf.Max(1, camera.pixelWidth >> i);
            var height = Mathf.Max(1, camera.pixelHeight >> i);
            scope.Command.SetComputeVectorParam(computeShader, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset(width, height));
            scope.Command.SetComputeTextureParam(computeShader, kernel, "_LensDirt", lensDirt == null ? Texture2D.whiteTexture : lensDirt);

            var prevFlareTempId = flareIds.GetProperty(i + 1);
            var flareTempId = flareIds.GetProperty(i);

            var useLensFlare = i < Mathf.Min(mipCount - 1, flareMip);
            if (useLensFlare)
            {
                var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.RGB111110Float) { enableRandomWrite = true };

                if (i + 1 != Mathf.Min(mipCount - 1, flareMip))
                    scope.Command.GetTemporaryRT(prevFlareTempId, desc);

                scope.Command.SetComputeTextureParam(computeShader, kernel, "_FlareInput", prevFlareTempId);

                if (i > 0)
                {
                    if (i != Mathf.Min(mipCount - 1, flareMip))
                        scope.Command.GetTemporaryRT(flareTempId, desc);

                    scope.Command.SetComputeTextureParam(computeShader, kernel, "_FlareResult", flareTempId);
                }
            }

            using var keywordScope = scope.Command.KeywordScope("LENS_FLARE", useLensFlare);

            using (var profilerScope = scope.Command.ProfilerScope("Bloom Upsample"))
                scope.Command.DispatchNormalized(computeShader, kernel, width, height, 1);

            if (i > 0)
                scope.Command.ReleaseTemporaryRT(inputId);

            if (useLensFlare)
                scope.Command.ReleaseTemporaryRT(prevFlareTempId);
        }
    }

    private void EyeAdaptation(ScopedCommandBuffer scope, Camera camera)
    {
        Vector4 exposureParams = new Vector4(0f, limitMin, limitMax, 0f);
        Vector2 histogramFraction = histogramPercentages / 100.0f;
        float evRange = limitMax - limitMin;
        float histScale = 1.0f / Mathf.Max(1e-5f, evRange);
        float histBias = -limitMin * histScale;
        Vector4 histogramParams = new Vector4(histScale, histBias, histogramFraction.x, histogramFraction.y);

        var computeShader = Resources.Load<ComputeShader>("Post Processing/Exposure");
        scope.Command.SetComputeVectorParam(computeShader, "_ExposureCompensationRemap", GraphicsUtilities.HalfTexelRemap(exposureResolution, 1));

        // Build luminance histogram
        var histogramKernel = computeShader.FindKernel("LuminanceHistogram");
        scope.Command.SetComputeTextureParam(computeShader, histogramKernel, "_ExposureCompensation", exposureTexture);
        scope.Command.SetComputeTextureParam(computeShader, histogramKernel, "_Input", result);
        scope.Command.SetComputeTextureParam(computeShader, histogramKernel, "_Exposure", currentExposure);
        scope.Command.SetComputeBufferParam(computeShader, histogramKernel, "_Histogram", histogramBuffer);
        scope.Command.SetComputeVectorParam(computeShader, "_HistogramExposureParams", histogramParams);
        scope.Command.SetComputeVectorParam(computeShader, "_ExposureParams", exposureParams);

        using (var profilerScope = scope.Command.ProfilerScope("Exposure Histogram"))
            scope.Command.DispatchNormalized(computeShader, histogramKernel, camera.pixelWidth, camera.pixelHeight, 1);

        // Calculate average luminance
        var averageKernel = computeShader.FindKernel("AverageLuminance");
        scope.Command.SetComputeFloatParam(computeShader, "_AdaptationSpeed", adaptationSpeed);
        scope.Command.SetComputeBufferParam(computeShader, averageKernel, "_Histogram", histogramBuffer);
        scope.Command.SetComputeBufferParam(computeShader, averageKernel, "_DebugExposure", debugExposureBuffer);
        scope.Command.SetComputeTextureParam(computeShader, averageKernel, "_ExposureCompensation", exposureTexture);
        scope.Command.SetComputeTextureParam(computeShader, averageKernel, "_Exposure", currentExposure);
        scope.Command.SetComputeTextureParam(computeShader, averageKernel, "_Result", previousExposure);
        computeShader.ToggleKeyword("FIRST", FrameCount == 0);
        scope.Command.DispatchNormalized(computeShader, averageKernel, 256, 1, 1);

        if (debugExposure)
            scope.Command.RequestAsyncReadback(debugExposureBuffer, OnDebugReadback);
    }

    private void OnDebugReadback(AsyncGPUReadbackRequest obj)
    {
        if (obj.hasError)
            throw new InvalidOperationException("Async Readback Error");

        // Readback exposure each frame and use it to update the saved value
        var data = obj.GetData<float>();
        var exposure = data[0];
        Debug.Log($"Exposure: {exposure}");
    }

    private void ExposureFusion(ScopedCommandBuffer scope, Camera camera)
    {
        var computeShader = Resources.Load<ComputeShader>("Post Processing/ExposureFusion");

        var exposures = Shader.PropertyToID("_Mips");
        var weights = Shader.PropertyToID("_MipsWeights");
        var desc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.RGB111110Float)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            useMipMap = true,
        };

        scope.Command.GetTemporaryRT(exposures, desc);
        scope.Command.GetTemporaryRT(weights, desc);

        // Compute the luminances of synthetic exposures.
        // Compute the local weights of synthetic exposures.
        scope.Command.SetComputeFloatParam(computeShader, "_Compensation", enableLtm ? exposure : 1);
        scope.Command.SetComputeFloatParam(computeShader, "_Shadows", enableLtm ? Mathf.Pow(2, shadows) : 1.0f);
        scope.Command.SetComputeFloatParam(computeShader, "_Highlights", enableLtm ? Mathf.Pow(2, -highlights) : 1.0f);
        scope.Command.SetComputeFloatParam(computeShader, "_SigmaSq", exposurePreferenceSigma * exposurePreferenceSigma);

        scope.Command.SetComputeTextureParam(computeShader, 0, "_Original", result);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_LuminanceResult", exposures);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_WeightsResult", weights);

        using (var profilerScope = scope.Command.ProfilerScope("Weights"))
            scope.Command.DispatchNormalized(computeShader, 0, camera.pixelWidth, camera.pixelHeight, 1);

        var gaussianDownsample = Resources.Load<ComputeShader>("Shaders/GaussianDownsample");

        var mipCount = Texture2DExtensions.MipCount(camera.pixelWidth, camera.pixelHeight);
        for (var i = 1; i < mipCount; i++)
        {
            var mipSize = new Vector2Int(camera.pixelWidth >> i, camera.pixelHeight >> i);
            scope.Command.SetComputeVectorParam(gaussianDownsample, "_Size", new Vector4(mipSize.x * 2, mipSize.y * 2, 0, 0));

            // Exposure
            scope.Command.SetComputeTextureParam(gaussianDownsample, 0, "_Source", exposures, i - 1);
            scope.Command.SetComputeTextureParam(gaussianDownsample, 0, "_Destination", exposures, i);

            using (var profilerScope = scope.Command.ProfilerScope("GaussianDownsample"))
                scope.Command.DispatchNormalized(gaussianDownsample, 0, mipSize.x, mipSize.y, 1);

            // Weights
            scope.Command.SetComputeTextureParam(gaussianDownsample, 0, "_Source", weights, i - 1);
            scope.Command.SetComputeTextureParam(gaussianDownsample, 0, "_Destination", weights, i);

            using (var profilerScope = scope.Command.ProfilerScope("GaussianDownsample"))
                scope.Command.DispatchNormalized(gaussianDownsample, 0, mipSize.x, mipSize.y, 1);
        }

        // Blend the coarsest level - Gaussian.
        var resultId = tempResultIds.GetProperty(mip);
        scope.Command.GetTemporaryRT(resultId, camera.pixelWidth >> mip, camera.pixelHeight >> mip, 0, FilterMode.Bilinear, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear, 1, true);

        scope.Command.SetComputeTextureParam(computeShader, 1, "_Exposures", exposures);
        scope.Command.SetComputeTextureParam(computeShader, 1, "_Weights", weights);
        scope.Command.SetComputeTextureParam(computeShader, 1, "_WeightResult", resultId);
        scope.Command.SetComputeFloatParam(computeShader, "_Mip", mip);

        using (var profilerScope = scope.Command.ProfilerScope("Blend"))
            scope.Command.DispatchNormalized(computeShader, 1, camera.pixelWidth >> mip, camera.pixelHeight >> mip, 1);

        for (var i = mip; i > this.displayMip; i--)
        {
            var newId = tempResultIds.GetProperty(i - 1);
            scope.Command.GetTemporaryRT(newId, camera.pixelWidth >> (i - 1), camera.pixelHeight >> (i - 1), 0, FilterMode.Bilinear, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear, 1, true);

            // Blend the finer levels - Laplacians.
            scope.Command.SetComputeTextureParam(computeShader, 2, "_Exposures", exposures);
            scope.Command.SetComputeFloatParam(computeShader, "_BoostLocalContrast", boostLocalContrast ? 1.0f : 0.0f);
            scope.Command.SetComputeTextureParam(computeShader, 2, "_Weights", weights);
            scope.Command.SetComputeTextureParam(computeShader, 2, "_AccumSoFar", resultId);
            scope.Command.SetComputeTextureParam(computeShader, 2, "_WeightResult", newId);
            scope.Command.SetComputeFloatParam(computeShader, "_Mip", i);
            scope.Command.SetComputeVectorParam(computeShader, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset(camera.pixelWidth >> (i - 1), camera.pixelHeight >> (i - 1)));
            scope.Command.SetComputeIntParam(computeShader, "_MaxWidth", (camera.pixelWidth >> i) - 1);
            scope.Command.SetComputeIntParam(computeShader, "_MaxHeight", (camera.pixelHeight >> i) - 1);

            using (var profilerScope = scope.Command.ProfilerScope("Laplacian"))
                scope.Command.DispatchNormalized(computeShader, 2, camera.pixelWidth >> (i - 1), camera.pixelHeight >> (i - 1), 1);

            scope.Command.ReleaseTemporaryRT(resultId);
            resultId = newId;
        }

        scope.Command.ReleaseTemporaryRT(weights);

        // Perform guided upsampling and output the final RGB image.
        var displayMip = Mathf.Min(this.displayMip, mip);

        var width = camera.pixelWidth >> displayMip;
        var height = camera.pixelHeight >> displayMip;
        var mipPixelSize = new Vector4(width, height, 1f / width, 1f / height);

        scope.Command.SetComputeTextureParam(computeShader, 3, "_Diffuse", resultId);
        scope.Command.SetComputeTextureParam(computeShader, 3, "_OriginalMip", exposures);
        scope.Command.SetComputeTextureParam(computeShader, 3, "_Result", result);
        scope.Command.SetComputeFloatParam(computeShader, "_Mip", displayMip);
        scope.Command.SetComputeVectorParam(computeShader, "_MipPixelSize", mipPixelSize);
        scope.Command.SetComputeVectorParam(computeShader, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset(camera.pixelWidth, camera.pixelHeight));

        using (var profilerScope = scope.Command.ProfilerScope("Combine"))
            scope.Command.DispatchNormalized(computeShader, 3, camera.pixelWidth, camera.pixelHeight, 1);

        scope.Command.ReleaseTemporaryRT(exposures);
        scope.Command.ReleaseTemporaryRT(resultId);
    }
}