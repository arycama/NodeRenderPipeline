using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Physical Sky")]
public partial class PhysicalSkyNode : RenderPipelineNode
{
    private static readonly IndexedString noiseIds = new("STBN/Scalar/stbn_scalar_2Dx1Dx1D_128x128x64x1_");

    [SerializeField, Input] private CloudProfile cloudProfile;
    [SerializeField] private bool noiseDebug;
    [SerializeField] private AtmosphereProfile atmosphereProfile;

    [Header("Sky")]
    [Input] private RenderTargetIdentifier exposure;
    [Input] private RenderTargetIdentifier transmittance;
    [Input] private RenderTargetIdentifier multiScatter;
    [Input] private RenderTargetIdentifier depth;
    [Input] private RenderTargetIdentifier motionVectors;
    [Input] private ComputeBuffer ambient;
    [Input] private RenderTargetIdentifier volumetricClouds;
    [Input] private RenderTargetIdentifier cloudDepth;
    [Input] private RenderTargetIdentifier volumetricLighting;
    [Input] private CullingResults cullingResults;
    [Input, Output] private RenderTargetIdentifier result;
    [Input, Output] private NodeConnection connection;

    private CameraTextureCache scatterCache, transmittanceCache, frameCountCache;

    private ComputeShader computeShader;
    private int tempScatter, tempTransmittance, spatialDenoiseKernel, temporalKernel, upsampleKernel;

    public override void Initialize()
    {
        tempScatter = Shader.PropertyToID("_TempScatter");
        tempTransmittance = Shader.PropertyToID("_TempTransmittance");

        computeShader = Resources.Load<ComputeShader>("PhysicalSky");
        spatialDenoiseKernel = computeShader.FindKernel("SpatialDenoise");
        temporalKernel = computeShader.FindKernel("Temporal");
        upsampleKernel = computeShader.FindKernel("Upsample");

        scatterCache = new("Physical Sky Scatter");
        transmittanceCache = new("Physical Sky Transmittance");
        frameCountCache = new("Sky Frame Count");
    }

    public override void Cleanup()
    {
        scatterCache.Dispose();
        transmittanceCache.Dispose();
        frameCountCache.Dispose();
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Physical Sky");

        var projMatrix = camera.projectionMatrix;
        var jitterX = projMatrix[0, 2];
        var jitterY = projMatrix[1, 2];
        var viewToWorld = Matrix4x4.Rotate(camera.transform.rotation);
        var mat = Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera.Resolution(), new Vector2(jitterX, jitterY), camera.fieldOfView, camera.aspect, viewToWorld, false);

        scope.Command.SetComputeMatrixParam(computeShader, "_PixelCoordToViewDirWS", mat);

        var desc = new RenderTextureDescriptor(camera.pixelWidth >> 1, camera.pixelHeight >> 1, RenderTextureFormat.RGB111110Float) { enableRandomWrite = true };
        scatterCache.GetTexture(camera, desc, out var scatterCurrent, out var scatterHistory, FrameCount);
        transmittanceCache.GetTexture(camera, desc, out var transmittanceCurrent, out var transmittanceHistory, FrameCount);

        using (var renderProfileScope = scope.Command.ProfilerScope("Render"))
            RenderSky(scope.Command, transmittanceCurrent, scatterCurrent, camera, camera.pixelWidth >> 1, camera.pixelHeight >> 1);

        scope.Command.GetTemporaryRT(tempScatter, desc);
        scope.Command.GetTemporaryRT(tempTransmittance, desc);
        scope.Command.SetComputeFloatParam(computeShader, "_MaxDistance", cloudProfile.GetMaxDistance(atmosphereProfile.PlanetRadius));

        using (var spatialDenoiseProfileScope = scope.Command.ProfilerScope("Spatial Denoise"))
            SpatialDenoise(scope.Command, scatterCurrent, transmittanceCurrent, tempScatter, tempTransmittance, camera.pixelWidth >> 1, camera.pixelHeight >> 1);

        using (var temporalProfileScope = scope.Command.ProfilerScope("Temporal"))
            TemporalDenoise(scope.Command, scatterCurrent, scatterHistory, transmittanceCurrent, transmittanceHistory, camera.pixelWidth >> 1, camera.pixelHeight >> 1, camera);

        using (var upsampleProfileScope = scope.Command.ProfilerScope("Upsample"))
            Upsample(scope.Command, scatterCurrent, transmittanceCurrent, camera.pixelWidth, camera.pixelHeight);
    }

    private void RenderSky(CommandBuffer command, RenderTargetIdentifier transmittance, RenderTargetIdentifier scatter, Camera camera, int width, int height)
    {
        // Find first 2 directional lights
        var dirLightCount = 0;
        for (var i = 0; i < cullingResults.visibleLights.Length; i++)
        {
            var light = cullingResults.visibleLights[i];
            if (light.lightType != LightType.Directional)
                continue;

            dirLightCount++;

            if (dirLightCount == 1)
            {
                command.SetComputeVectorParam(computeShader, "_LightDirection0", -light.localToWorldMatrix.Forward());
                command.SetComputeVectorParam(computeShader, "_LightColor0", light.finalColor);
            }
            else if (dirLightCount == 2)
            {
                command.SetComputeVectorParam(computeShader, "_LightDirection1", -light.localToWorldMatrix.Forward());
                command.SetComputeVectorParam(computeShader, "_LightColor1", light.finalColor);
            }
            else
            {
                // Only 2 lights supported
                break;
            }
        }

        var blueNoise1D = Resources.Load<Texture2D>(noiseIds.GetString(noiseDebug ? 0 : FrameCount % 16));

        var planetCenterRws = new Vector3(0f, (float)((double)atmosphereProfile.PlanetRadius + camera.transform.position.y), 0f);
        command.SetComputeVectorParam(computeShader, "_PlanetOffset", planetCenterRws);

        var kernel = computeShader.FindKernel("SkyMain");
        command.SetComputeTextureParam(computeShader, kernel, "_Exposure", exposure);
        command.SetComputeTextureParam(computeShader, kernel, "_AtmosphereTransmittance", this.transmittance);
        command.SetComputeTextureParam(computeShader, kernel, "_MultipleScatter", multiScatter);
        command.SetComputeTextureParam(computeShader, kernel, "_StarMap", atmosphereProfile.StarTexture);
        command.SetComputeBufferParam(computeShader, kernel, "_AmbientSh", ambient);
        command.SetComputeTextureParam(computeShader, kernel, "_BlueNoise1D", blueNoise1D);
        command.SetComputeTextureParam(computeShader, kernel, "_Depth", depth);

        command.SetComputeVectorParam(computeShader, "_StarColor", atmosphereProfile.StarColor.linear);
        command.SetComputeVectorParam(computeShader, "_GroundColor", atmosphereProfile.GroundColor.linear);

        command.SetComputeTextureParam(computeShader, kernel, "_TransmittanceResult", transmittance);
        command.SetComputeTextureParam(computeShader, kernel, "_ScatterResult", scatter);
        command.SetComputeTextureParam(computeShader, kernel, "_VolumetricClouds", volumetricClouds);
        command.SetComputeTextureParam(computeShader, kernel, "_CloudDepth", cloudDepth);

        var keyword = dirLightCount == 2 ? "LIGHT_COUNT_TWO" : (dirLightCount == 1 ? "LIGHT_COUNT_ONE" : string.Empty);
        using var keywordScope = command.KeywordScope(keyword);
        command.DispatchNormalized(computeShader, kernel, width, height, 1);
    }

    private void SpatialDenoise(CommandBuffer command, RenderTargetIdentifier scatterInput, RenderTargetIdentifier transmittanceInput, RenderTargetIdentifier scatterResult, RenderTargetIdentifier transmittanceResult, int width, int height)
    {
        command.SetComputeTextureParam(computeShader, spatialDenoiseKernel, "_Scatter", scatterInput);
        command.SetComputeTextureParam(computeShader, spatialDenoiseKernel, "_Transmittance", transmittanceInput);
        command.SetComputeTextureParam(computeShader, spatialDenoiseKernel, "_ScatterResult", scatterResult);
        command.SetComputeTextureParam(computeShader, spatialDenoiseKernel, "_TransmittanceResult", transmittanceResult);
        command.SetComputeTextureParam(computeShader, spatialDenoiseKernel, "_Depth", depth);
        command.SetComputeTextureParam(computeShader, spatialDenoiseKernel, "_CloudDepth", cloudDepth);
        command.SetComputeTextureParam(computeShader, spatialDenoiseKernel, "_VolumetricClouds", volumetricClouds);
        command.DispatchNormalized(computeShader, spatialDenoiseKernel, width, height, 1);
    }

    private void TemporalDenoise(CommandBuffer command, RenderTargetIdentifier scatterCurrent, RenderTargetIdentifier scatterHistory, RenderTargetIdentifier transmittanceCurrent, RenderTargetIdentifier transmittanceHistory, int width, int height, Camera camera)
    {
        command.SetComputeTextureParam(computeShader, temporalKernel, "_Depth", depth);
        command.SetComputeTextureParam(computeShader, temporalKernel, "_CloudDepth", cloudDepth);
        command.SetComputeTextureParam(computeShader, temporalKernel, "_MotionVectors", motionVectors);

        command.SetComputeTextureParam(computeShader, temporalKernel, "_ScatterResult", scatterCurrent);
        command.SetComputeTextureParam(computeShader, temporalKernel, "_Scatter", tempScatter);
        command.SetComputeTextureParam(computeShader, temporalKernel, "_ScatterHistory", scatterHistory);

        command.SetComputeTextureParam(computeShader, temporalKernel, "_TransmittanceResult", transmittanceCurrent);
        command.SetComputeTextureParam(computeShader, temporalKernel, "_Transmittance", tempTransmittance);
        command.SetComputeTextureParam(computeShader, temporalKernel, "_TransmittanceHistory", transmittanceHistory);

        var frameCountDesc = new RenderTextureDescriptor(width, height, RenderTextureFormat.R8) { enableRandomWrite = true };
        frameCountCache.GetTexture(camera, frameCountDesc, out var newFrameCount, out var prevFrameCount, FrameCount);

        command.SetComputeTextureParam(computeShader, temporalKernel, "_PrevFrameCount", prevFrameCount);
        command.SetComputeTextureParam(computeShader, temporalKernel, "_NewFrameCount", newFrameCount);

        command.SetComputeVectorParam(computeShader, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset(width, height));

        computeShader.GetKernelThreadGroupSizes(temporalKernel, out var xThreads, out var yThreads, out var zThreads);

        // Each thread group is 32x32, but the first/last row are only used to fetch additional data
        var threadGroupsX = (width - 1) / ((int)xThreads - 0) + 1;
        var threadGroupsY = (height - 1) / ((int)yThreads - 0) + 1;

        command.DispatchCompute(computeShader, temporalKernel, threadGroupsX, threadGroupsY, 1);
        command.ReleaseTemporaryRT(tempScatter);
        command.ReleaseTemporaryRT(tempTransmittance);
    }

    private void Upsample(CommandBuffer command, RenderTargetIdentifier currentScatter, RenderTargetIdentifier currentTransmittance, int width, int height)
    {
        command.SetComputeTextureParam(computeShader, upsampleKernel, "_Depth", depth);
        command.SetComputeTextureParam(computeShader, upsampleKernel, "_Scatter", currentScatter);
        command.SetComputeTextureParam(computeShader, upsampleKernel, "_Transmittance", currentTransmittance);
        command.SetComputeTextureParam(computeShader, upsampleKernel, "_Result", result);

        // Also render star cubemap in final pass, so it is full res
        command.SetComputeTextureParam(computeShader, upsampleKernel, "_Exposure", exposure);
        command.SetComputeTextureParam(computeShader, upsampleKernel, "_StarMap", atmosphereProfile.StarTexture);
        command.SetComputeTextureParam(computeShader, upsampleKernel, "_VolumetricLighting", volumetricLighting);

        command.DispatchNormalized(computeShader, upsampleKernel, width, height, 1);
        command.ReleaseTemporaryRT(tempTransmittance);
    }
}
