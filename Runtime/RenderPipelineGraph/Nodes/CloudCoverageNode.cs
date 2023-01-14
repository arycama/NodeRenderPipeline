using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Cloud Coverage")]
public partial class CloudCoverageNode : RenderPipelineNode
{
    private static readonly IndexedString noiseIds = new("STBN/Scalar/stbn_scalar_2Dx1Dx1D_128x128x64x1_");
    private static readonly int cloudCoverageId = Shader.PropertyToID("_CloudCoverage");

    [SerializeField] private CloudProfile cloudProfile = null;
    [SerializeField] private AtmosphereProfile atmosphereProfile;

    [Input] private RenderTargetIdentifier cloudNoise;
    [Input] private RenderTargetIdentifier detailNoise;
    [Input] private RenderTargetIdentifier weather;
    [Input] private RenderTargetIdentifier exposure;
    [Input] private RenderTargetIdentifier atmosphereTransmittance;
    [Input] private RenderTargetIdentifier atmosphereMultiScatter;
    [Input] private SmartComputeBuffer<DirectionalLightData> directionalLightBuffer;

    [Output] private readonly RenderTargetIdentifier result = cloudCoverageId;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        if (cloudProfile == null)
            return;

        using var scope = context.ScopedCommandBuffer("Cloud Coverage", true);

        var descriptor = new RenderTextureDescriptor(1, 1, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
        scope.Command.GetTemporaryRT(cloudCoverageId, descriptor);

        var cs = Resources.Load<ComputeShader>("VolumetricClouds");
        var kernel = cs.FindKernel("CloudCoverage");

        var blueNoise1D = Resources.Load<Texture2D>(noiseIds.GetString(FrameCount % 64));

        scope.Command.SetComputeTextureParam(cs, kernel, "_BlueNoise1D", blueNoise1D);
        scope.Command.SetComputeTextureParam(cs, kernel, "_CloudNoise", cloudNoise);
        scope.Command.SetComputeTextureParam(cs, kernel, "_CloudDetail", detailNoise);
        scope.Command.SetComputeTextureParam(cs, kernel, "_WeatherTexture", weather);
        scope.Command.SetComputeTextureParam(cs, kernel, "_AtmosphereTransmittance", atmosphereTransmittance);
        scope.Command.SetComputeTextureParam(cs, kernel, "_MultipleScatter", atmosphereMultiScatter);
        scope.Command.SetComputeTextureParam(cs, kernel, "_Exposure", exposure);
        cloudProfile.SetMaterialProperties(cs, kernel, scope.Command, atmosphereProfile.PlanetRadius);

        scope.Command.SetComputeBufferParam(cs, kernel, "_DirectionalLightData", directionalLightBuffer);
        scope.Command.SetComputeIntParam(cs, "_DirectionalLightCount", directionalLightBuffer.Count);
        scope.Command.SetComputeTextureParam(cs, kernel, "_CloudCoverageResult", cloudCoverageId);
        scope.Command.DispatchCompute(cs, kernel, 1, 1, 1);

        scope.Command.SetGlobalTexture("_CloudCoverage", cloudCoverageId);
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(cloudCoverageId);
    }
}