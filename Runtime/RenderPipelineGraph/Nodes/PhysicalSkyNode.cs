using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Physical Sky")]
public partial class PhysicalSkyNode : RenderPipelineNode
{
    private static readonly IndexedString noiseIds = new("STBN/Vec2/stbn_vec2_2Dx1D_128x128x64_");

    [SerializeField] private AtmosphereProfile atmosphereProfile;
    [Input, SerializeField, Range(1, 128)] private int sampleCount = 16;
    [Input, SerializeField] private bool debugNoise;
    [SerializeField] private CloudProfile cloudProfile;

    [Input, SerializeField, Pow2(256)] private int cdfWidth = 64;
    [Input, SerializeField, Pow2(256)] private int cdfHeight = 64;
    [Input, SerializeField, Pow2(256)] private int cdfDepth = 64;

    [Input] private RenderTargetIdentifier velocity;
    [Input] private RenderTargetIdentifier exposure;
    [Input] private RenderTargetIdentifier transmittance;
    [Input] private RenderTargetIdentifier multiScatter;
    [Input] private RenderTargetIdentifier depth;
    [Input] private RenderTargetIdentifier previousDepth;
    [Input] private RenderTargetIdentifier volumetricClouds;
    [Input] private RenderTargetIdentifier cloudDepth;
    [Input] private RenderTargetIdentifier cloudCoverage;

    [Input] private CullingResults cullingResults;
    [Input] private RenderTargetIdentifier directionalShadows;
    [Input, Output] private RenderTargetIdentifier result;
    [Input, Output] private NodeConnection connection;

    private RenderTexture invCdfTexture;
    private CameraTextureCache previousFrameCache;
    private CameraTextureCache frameCountCache;
    private int version = -1;

    public override void Initialize()
    {
        previousFrameCache = new("Physical Sky");
        frameCountCache = new("Ambient Occlusion FrameCount");
        version = -1;

        var cdfDesc = new RenderTextureDescriptor(cdfWidth * 3, cdfHeight, RenderTextureFormat.RFloat)
        {
            dimension = TextureDimension.Tex3D,
            enableRandomWrite = true,
            volumeDepth = cdfDepth,
        };

        invCdfTexture = new RenderTexture(cdfDesc) { hideFlags = HideFlags.HideAndDontSave }.Created();
    }

    public override void Cleanup()
    {
        previousFrameCache.Dispose();
        frameCountCache.Dispose();
        DestroyImmediate(invCdfTexture);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Physical Sky");

        var cdfDesc = new RenderTextureDescriptor(cdfWidth * 3, cdfHeight, RenderTextureFormat.RFloat)
        {
            dimension = TextureDimension.Tex3D,
            enableRandomWrite = true,
            volumeDepth = cdfDepth,
        };

        invCdfTexture.Resize(cdfWidth * 3, cdfHeight, cdfDepth, out var hasChanged);

        var cdfComputeShader = Resources.Load<ComputeShader>("ComputeSkyCDF");
        scope.Command.SetComputeTextureParam(cdfComputeShader, 0, "_AtmosphereTransmittance", transmittance);
        scope.Command.SetComputeTextureParam(cdfComputeShader, 0, "_Result", invCdfTexture);
        scope.Command.SetComputeIntParam(cdfComputeShader, "_Width", cdfWidth);
        scope.Command.SetComputeIntParam(cdfComputeShader, "_Height", cdfHeight);
        scope.Command.SetComputeIntParam(cdfComputeShader, "_Depth", cdfDepth);
        scope.Command.SetGlobalInt("_AtmosphereCdfWidth", cdfWidth);
        scope.Command.SetGlobalInt("_AtmosphereCdfHeight", cdfHeight);
        scope.Command.SetGlobalInt("_AtmosphereCdfDepth", cdfDepth);

        scope.Command.SetComputeVectorParam(cdfComputeShader, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(cdfWidth, cdfHeight, cdfDepth));

        if (atmosphereProfile.Version != version || hasChanged)
            scope.Command.DispatchNormalized(cdfComputeShader, 0, cdfWidth * 3, cdfHeight, cdfDepth);

        version = atmosphereProfile.Version;

        var computeShader = Resources.Load<ComputeShader>("PhysicalSkyNew");

        var projMatrix = camera.projectionMatrix;
        var jitterX = projMatrix[0, 2];
        var jitterY = projMatrix[1, 2];
        var viewToWorld = Matrix4x4.Rotate(camera.transform.rotation);
        var mat = Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera.Resolution(), new Vector2(jitterX, jitterY), camera.fieldOfView, camera.aspect, viewToWorld, false);

        var blueNoise2D = Resources.Load<Texture2D>(noiseIds.GetString(debugNoise ? 0 : FrameCount % 64));
        var planetCenterRws = new Vector3(0f, (float)((double)atmosphereProfile.PlanetRadius + camera.transform.position.y), 0f);

        var desc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.RInt) { enableRandomWrite = true };
        previousFrameCache.GetTexture(camera, desc, out var current, out var previous, FrameCount);


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
                scope.Command.SetComputeVectorParam(computeShader, "_LightDirection0", -light.localToWorldMatrix.Forward());
                scope.Command.SetComputeVectorParam(computeShader, "_LightColor0", light.finalColor);
            }
            else if (dirLightCount == 2)
            {
                // Only 2 lights supported
                scope.Command.SetComputeVectorParam(computeShader, "_LightDirection1", -light.localToWorldMatrix.Forward());
                scope.Command.SetComputeVectorParam(computeShader, "_LightColor1", light.finalColor);
                break;
            }
        }

        var kernelIndex = dirLightCount;

        cloudProfile.SetMaterialProperties(computeShader, kernelIndex, scope.Command, atmosphereProfile.PlanetRadius);
        cloudProfile.SetMaterialProperties(computeShader, 3, scope.Command, atmosphereProfile.PlanetRadius);

        var luminanceTemp = Shader.PropertyToID("_LuminanceTemp");
        var transmittanceTemp = Shader.PropertyToID("_TransmittanceTemp");
        var tempDesc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.RGB111110Float) { enableRandomWrite = true };
        scope.Command.GetTemporaryRT(luminanceTemp, tempDesc);
        scope.Command.GetTemporaryRT(transmittanceTemp, tempDesc);

        scope.Command.SetComputeTextureParam(computeShader, kernelIndex, "_Exposure", exposure);
        scope.Command.SetComputeTextureParam(computeShader, kernelIndex, "_AtmosphereTransmittance", transmittance);
        scope.Command.SetComputeTextureParam(computeShader, kernelIndex, "_MultipleScatter", multiScatter);
        scope.Command.SetComputeTextureParam(computeShader, kernelIndex, "_BlueNoise2D", blueNoise2D);
        scope.Command.SetComputeTextureParam(computeShader, kernelIndex, "_Depth", depth);
        scope.Command.SetComputeTextureParam(computeShader, kernelIndex, "_DirectionalShadows", directionalShadows);

        scope.Command.SetComputeTextureParam(computeShader, kernelIndex, "_VolumetricClouds", volumetricClouds);
        scope.Command.SetComputeTextureParam(computeShader, kernelIndex, "_CloudDepth", cloudDepth);
        scope.Command.SetComputeTextureParam(computeShader, kernelIndex, "_CloudCoverage", cloudCoverage);

        scope.Command.SetComputeTextureParam(computeShader, kernelIndex, "_AtmosphereCdf", invCdfTexture);

        scope.Command.SetComputeTextureParam(computeShader, kernelIndex, "_LuminanceResult", luminanceTemp);
        scope.Command.SetComputeTextureParam(computeShader, kernelIndex, "_TransmittanceResult", transmittanceTemp);
        
        scope.Command.SetComputeMatrixParam(computeShader, "_PixelCoordToViewDirWS", mat);

        scope.Command.SetComputeVectorParam(computeShader, "_PlanetOffset", planetCenterRws);
        scope.Command.SetComputeVectorParam(computeShader, "_GroundColor", atmosphereProfile.GroundColor.linear);

        scope.Command.SetComputeFloatParam(computeShader, "_SampleCount", sampleCount);
        scope.Command.SetComputeFloatParam(computeShader, "_ViewHeight", (float)((double)atmosphereProfile.PlanetRadius + camera.transform.position.y));

        scope.Command.DispatchNormalized(computeShader, kernelIndex, camera.pixelWidth, camera.pixelHeight, 1);

        var frameCountDesc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.R8, 0) { enableRandomWrite = true };
        frameCountCache.GetTexture(camera, frameCountDesc, out var currentFrameCount, out var previousFrameCount, FrameCount);

        scope.Command.SetComputeVectorParam(computeShader, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset(camera.pixelWidth, camera.pixelHeight));

        scope.Command.SetComputeTextureParam(computeShader, 3, "_PreviousDepth", previousDepth);
        scope.Command.SetComputeTextureParam(computeShader, 3, "_FrameCount", currentFrameCount);
        scope.Command.SetComputeTextureParam(computeShader, 3, "_FrameCountPrevious", previousFrameCount);
        scope.Command.SetComputeTextureParam(computeShader, 3, "_Luminance", luminanceTemp);
        scope.Command.SetComputeTextureParam(computeShader, 3, "_Transmittance", transmittanceTemp);
        scope.Command.SetComputeTextureParam(computeShader, 3, "_Current", current);
        scope.Command.SetComputeTextureParam(computeShader, 3, "_Previous", previous);
        scope.Command.SetComputeTextureParam(computeShader, 3, "_Velocity", velocity);
        scope.Command.SetComputeTextureParam(computeShader, 3, "_Result", result);

        scope.Command.SetComputeTextureParam(computeShader, 3, "_VolumetricClouds", volumetricClouds);
        scope.Command.SetComputeTextureParam(computeShader, 3, "_CloudDepth", cloudDepth);
        scope.Command.SetComputeTextureParam(computeShader, 3, "_CloudCoverage", cloudCoverage);

        scope.Command.DispatchNormalized(computeShader, 3, camera.pixelWidth, camera.pixelHeight, 1);

        scope.Command.ReleaseTemporaryRT(luminanceTemp);
        scope.Command.ReleaseTemporaryRT(transmittanceTemp);
    }
}
