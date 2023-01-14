using System;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Atmosphere")]
public partial class AtmosphereNode : RenderPipelineNode
{
    private RenderTexture transmittanceTexture, multiScatterTexture;

    [SerializeField, Range(1, 128)] private int transmittanceSamples = 64;
    [SerializeField] private Vector2Int transmittanceResolution = new(256, 64);
    [SerializeField] private RenderTextureFormat transmittanceFormat = RenderTextureFormat.RGB111110Float;

    [SerializeField, Range(1, 128)] private int multiScatterSamples = 64;
    [SerializeField] private Vector2Int multiScatterResolution = new(32, 32);
    [SerializeField] private RenderTextureFormat multiScatterFormat = RenderTextureFormat.RGB111110Float;

    [SerializeField] private AtmosphereProfile atmosphereProfile;

    [Output] private RenderTargetIdentifier transmittance;
    [Output] private RenderTargetIdentifier multiScatter;
    [Input, Output] private NodeConnection connection;

    private int version;

    public override void Initialize()
    {
        base.Initialize();

        transmittanceTexture = new RenderTexture(transmittanceResolution.x, transmittanceResolution.y, 0, transmittanceFormat) { enableRandomWrite = true, hideFlags = HideFlags.HideAndDontSave }.Created();
        multiScatterTexture = new RenderTexture(multiScatterResolution.x, multiScatterResolution.y, 0, multiScatterFormat) { enableRandomWrite = true, hideFlags = HideFlags.HideAndDontSave }.Created();

        transmittance = transmittanceTexture;
        multiScatter = multiScatterTexture;

        version = -1;
    }

    public override void Cleanup()
    {
        base.Cleanup();

        DestroyImmediate(transmittanceTexture);
        DestroyImmediate(multiScatterTexture);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        transmittanceTexture.Resize(transmittanceResolution.x, transmittanceResolution.y);
        multiScatterTexture.Resize(multiScatterResolution.x, multiScatterResolution.y);

        using var scope = context.ScopedCommandBuffer("Atmosphere", true);

        // Required for now, but place somewhere else maybe
        var planetCenterRws = new Vector3(0f, (float)(double)atmosphereProfile.PlanetRadius + camera.transform.position.y, 0f);
        scope.Command.SetGlobalVector("_PlanetOffset", planetCenterRws);

        if (atmosphereProfile == null || atmosphereProfile.Version == version)
            return;

        version = atmosphereProfile.Version;

        // Planet
        scope.Command.SetGlobalFloat("_PlanetRadius", atmosphereProfile.PlanetRadius);
        scope.Command.SetGlobalFloat("_AtmosphereHeight", atmosphereProfile.AtmosphereHeight);

        // Mie scatter
        var g = atmosphereProfile.AerosolAnisotropy;
        var csPhasePartConstant = (3 / (8 * Mathf.PI)) * (1 - g * g) / (2 + g * g);
        scope.Command.SetGlobalFloat("_MiePhase", g);
        scope.Command.SetGlobalFloat("_MiePhaseConstant", csPhasePartConstant);

        // Ozone
        scope.Command.SetGlobalVector("_OzoneAbsorption", atmosphereProfile.AirAbsorption);
        scope.Command.SetGlobalFloat("_OzoneWidth", atmosphereProfile.OzoneWidth);
        scope.Command.SetGlobalFloat("_OzoneHeight", atmosphereProfile.OzoneHeight);

        // Other
        scope.Command.SetGlobalColor("_GroundColor", atmosphereProfile.GroundColor.linear);

        // Some optimized variables for precision
        var log2e = Math.Log(Math.E, 2.0);
        var rayleighHeightScale = -log2e / (double)atmosphereProfile.AirAverageHeight;
        var mieHeightScale = -log2e / (double)atmosphereProfile.AerosolAverageHeight;

        var rayleighHeightOffsetX = (double)atmosphereProfile.PlanetRadius * log2e / (double)atmosphereProfile.AirAverageHeight + Math.Log(atmosphereProfile.AirScatter.x, 2.0);
        var rayleighHeightOffsetY = (double)atmosphereProfile.PlanetRadius * log2e / (double)atmosphereProfile.AirAverageHeight + Math.Log(atmosphereProfile.AirScatter.y, 2.0);
        var rayleighHeightOffsetZ = (double)atmosphereProfile.PlanetRadius * log2e / (double)atmosphereProfile.AirAverageHeight + Math.Log(atmosphereProfile.AirScatter.z, 2.0);
        var mieHeightOffset = (double)atmosphereProfile.PlanetRadius * log2e / (double)atmosphereProfile.AerosolAverageHeight + Math.Log((double)atmosphereProfile.AerosolScatter + (double)atmosphereProfile.AerosolAbsorption, 2.0);

        var heightScale = new Vector4((float)rayleighHeightScale, (float)rayleighHeightScale, (float)rayleighHeightScale, (float)mieHeightScale);
        var heightOffset = new Vector4((float)rayleighHeightOffsetX, (float)rayleighHeightOffsetY, (float)rayleighHeightOffsetZ, (float)mieHeightOffset);

        Vector3 ozoneScale, ozoneOffset;
        ozoneScale.x = (float)((double)atmosphereProfile.AirAbsorption.x / (double)atmosphereProfile.OzoneWidth);
        ozoneScale.y = (float)((double)atmosphereProfile.AirAbsorption.y / (double)atmosphereProfile.OzoneWidth);
        ozoneScale.z = (float)((double)atmosphereProfile.AirAbsorption.z / (double)atmosphereProfile.OzoneWidth);

        ozoneOffset.x = (float)(-((double)atmosphereProfile.PlanetRadius + (double)atmosphereProfile.OzoneHeight) * (double)atmosphereProfile.AirAbsorption.x / (double)atmosphereProfile.OzoneWidth);
        ozoneOffset.y = (float)(-((double)atmosphereProfile.PlanetRadius + (double)atmosphereProfile.OzoneHeight) * (double)atmosphereProfile.AirAbsorption.y / (double)atmosphereProfile.OzoneWidth);
        ozoneOffset.z = (float)(-((double)atmosphereProfile.PlanetRadius + (double)atmosphereProfile.OzoneHeight) * (double)atmosphereProfile.AirAbsorption.z / (double)atmosphereProfile.OzoneWidth);

        scope.Command.SetGlobalVector("_AtmosphereExtinctionScale", heightScale);
        scope.Command.SetGlobalVector("_AtmosphereExtinctionOffset", heightOffset);
        scope.Command.SetGlobalVector("_AtmosphereOzoneScale", ozoneScale);
        scope.Command.SetGlobalVector("_AtmosphereOzoneOffset", ozoneOffset);

        // Basically the same, except for mie there is no absorption
        var rayleighScatterOffsetX = (double)atmosphereProfile.PlanetRadius * log2e / (double)atmosphereProfile.AirAverageHeight + Math.Log(atmosphereProfile.AirScatter.x, 2.0);
        var rayleighScatterOffsetY = (double)atmosphereProfile.PlanetRadius * log2e / (double)atmosphereProfile.AirAverageHeight + Math.Log(atmosphereProfile.AirScatter.y, 2.0);
        var rayleighScatterOffsetZ = (double)atmosphereProfile.PlanetRadius * log2e / (double)atmosphereProfile.AirAverageHeight + Math.Log(atmosphereProfile.AirScatter.z, 2.0);
        var mieScatterOffset = (double)atmosphereProfile.PlanetRadius * log2e / (double)atmosphereProfile.AerosolAverageHeight + Math.Log((double)atmosphereProfile.AerosolScatter, 2.0);
        var scatterOffset = new Vector4((float)rayleighScatterOffsetX, (float)rayleighScatterOffsetY, (float)rayleighScatterOffsetZ, (float)mieScatterOffset);
        scope.Command.SetGlobalVector("_AtmosphereScatterOffset", scatterOffset);

        var computeShader = Resources.Load<ComputeShader>("Lighting/PrecomputeAtmosphere");

        scope.Command.SetGlobalFloat("_TopRadius", (float)((double)atmosphereProfile.PlanetRadius + (double)atmosphereProfile.AtmosphereHeight));
        scope.Command.SetComputeVectorParam(computeShader, "_PlanetOffset", Vector3.zero);

        // Calculate transmittance
        using (scope.Command.ProfilerScope("Transmittance"))
        {
            var transmittanceKernel = computeShader.FindKernel("Transmittance");
            scope.Command.SetComputeTextureParam(computeShader, transmittanceKernel, "Result", transmittanceTexture);
            scope.Command.SetComputeVectorParam(computeShader, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset(transmittanceResolution.x, transmittanceResolution.y));
            scope.Command.SetComputeIntParam(computeShader, "_SampleCount", transmittanceSamples);
            scope.Command.DispatchNormalized(computeShader, transmittanceKernel, transmittanceResolution.x, transmittanceResolution.y, 1);
        }

        // Calculate multiple scatter
        using (scope.Command.ProfilerScope("Multi Scatter"))
        {
            var multiScatterKernel = computeShader.FindKernel("MultiScatter");
            scope.Command.SetComputeTextureParam(computeShader, multiScatterKernel, "_AtmosphereTransmittance", transmittanceTexture);
            scope.Command.SetComputeTextureParam(computeShader, multiScatterKernel, "Result", multiScatterTexture);
            scope.Command.SetComputeVectorParam(computeShader, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset(multiScatterResolution.x, multiScatterResolution.y));
            scope.Command.SetComputeIntParam(computeShader, "_SampleCount", multiScatterSamples);
            scope.Command.DispatchCompute(computeShader, multiScatterKernel, multiScatterResolution.x, multiScatterResolution.y, 1);
        }
    }
}