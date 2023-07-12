using System;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Atmosphere")]
public partial class AtmosphereNode : RenderPipelineNode
{
    private RenderTexture transmittanceTexture, multiScatterTexture, ambientTexture;

    [SerializeField, Range(1, 512)] private int transmittanceSamples = 64;
    [SerializeField] private Vector2Int transmittanceResolution = new(256, 64);
    [SerializeField] private RenderTextureFormat transmittanceFormat = RenderTextureFormat.RGB111110Float;

    [SerializeField, Range(1, 128)] private int multiScatterSamples = 64;
    [SerializeField] private Vector2Int multiScatterResolution = new(32, 32);
    [SerializeField] private RenderTextureFormat multiScatterFormat = RenderTextureFormat.RGB111110Float;

    [SerializeField, Input, Range(1, 256)] private int ambientWidth = 64;
    [SerializeField, Input, Range(1, 256)] private int ambientHeight = 64;

    [SerializeField] private AtmosphereProfile atmosphereProfile;

    [Output] private RenderTargetIdentifier transmittance;
    [Output] private RenderTargetIdentifier multiScatter;
    [Output] private RenderTargetIdentifier ambient;
    [Input, Output] private NodeConnection connection;

    private int version;

    public override void Initialize()
    {
        base.Initialize();

        transmittanceTexture = new RenderTexture(transmittanceResolution.x, transmittanceResolution.y, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true, hideFlags = HideFlags.HideAndDontSave }.Created();
        multiScatterTexture = new RenderTexture(multiScatterResolution.x, multiScatterResolution.y, 0, multiScatterFormat) { enableRandomWrite = true, hideFlags = HideFlags.HideAndDontSave }.Created();
        ambientTexture = new RenderTexture(ambientWidth, ambientHeight, 0, RenderTextureFormat.RGB111110Float) { enableRandomWrite = true, hideFlags = HideFlags.HideAndDontSave }.Created();
        ambient = ambientTexture;

        transmittance = transmittanceTexture;
        multiScatter = multiScatterTexture;

        version = -1;
    }

    public override void Cleanup()
    {
        base.Cleanup();

        DestroyImmediate(transmittanceTexture);
        DestroyImmediate(multiScatterTexture);
        DestroyImmediate(ambientTexture);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        transmittanceTexture.Resize(transmittanceResolution.x, transmittanceResolution.y);
        multiScatterTexture.Resize(multiScatterResolution.x, multiScatterResolution.y);
        ambientTexture.Resize(ambientWidth, ambientHeight);

        using var scope = context.ScopedCommandBuffer("Atmosphere", true);

        // Required for now, but place somewhere else maybe
        var planetCenterRws = new Vector3(0f, (float)(double)atmosphereProfile.PlanetRadius + camera.transform.position.y, 0f);
        scope.Command.SetGlobalVector("_PlanetOffset", planetCenterRws);

        var transmittanceRemap = GraphicsUtilities.HalfTexelRemap(transmittanceResolution.x, transmittanceResolution.y);
        scope.Command.SetGlobalVector("_AtmosphereTransmittanceRemap", transmittanceRemap);

        var multiScatterRemap = GraphicsUtilities.HalfTexelRemap(multiScatterResolution.x, multiScatterResolution.y);
        scope.Command.SetGlobalVector("_AtmosphereMultiScatterRemap", multiScatterRemap);

        var ambientRemap = GraphicsUtilities.HalfTexelRemap(ambientWidth, ambientHeight);
        scope.Command.SetGlobalVector("_AtmosphereAmbientRemap", ambientRemap);

        if (atmosphereProfile == null || atmosphereProfile.Version == version)
            return;

        version = atmosphereProfile.Version;

        // Planet
        scope.Command.SetGlobalFloat("_PlanetRadius", atmosphereProfile.PlanetRadius);
        scope.Command.SetGlobalFloat("_AtmosphereHeight", atmosphereProfile.AtmosphereHeight);

        // Mie scatter
        scope.Command.SetGlobalFloat("_MiePhase", atmosphereProfile.AerosolAnisotropy);

        // Ozone
        scope.Command.SetGlobalVector("_OzoneAbsorption", atmosphereProfile.AirAbsorption);
        scope.Command.SetGlobalFloat("_OzoneWidth", atmosphereProfile.OzoneWidth);
        scope.Command.SetGlobalFloat("_OzoneHeight", atmosphereProfile.OzoneHeight);

        scope.Command.SetGlobalFloat("_RayleighHeight", atmosphereProfile.AirAverageHeight);
        scope.Command.SetGlobalFloat("_MieHeight", atmosphereProfile.AerosolAverageHeight);

        // For some testing
        scope.Command.SetGlobalVector("_RayleighScatter", atmosphereProfile.AirScatter);
        scope.Command.SetGlobalFloat("_MieScatter", atmosphereProfile.AerosolScatter);
        scope.Command.SetGlobalFloat("_MieAbsorption", atmosphereProfile.AerosolAbsorption);

        var computeShader = Resources.Load<ComputeShader>("Lighting/PrecomputeAtmosphere");

        scope.Command.SetGlobalFloat("_TopRadius", (float)((double)atmosphereProfile.PlanetRadius + (double)atmosphereProfile.AtmosphereHeight));
        scope.Command.SetComputeVectorParam(computeShader, "_PlanetOffset", Vector3.zero);

        // Calculate transmittance
        using (scope.Command.ProfilerScope("Transmittance"))
        {
            var transmittanceKernel = computeShader.FindKernel("Transmittance");
            scope.Command.SetComputeTextureParam(computeShader, transmittanceKernel, "Result", transmittanceTexture);
            scope.Command.SetComputeVectorParam(computeShader, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(transmittanceResolution.x, transmittanceResolution.y));
            scope.Command.SetComputeIntParam(computeShader, "_SampleCount", transmittanceSamples);
            scope.Command.DispatchNormalized(computeShader, transmittanceKernel, transmittanceResolution.x, transmittanceResolution.y, 1);
        }

        // Calculate multiple scatter
        using (scope.Command.ProfilerScope("Multi Scatter"))
        {
            var multiScatterKernel = computeShader.FindKernel("MultiScatter");
            scope.Command.SetComputeTextureParam(computeShader, multiScatterKernel, "_AtmosphereTransmittance", transmittanceTexture);
            scope.Command.SetComputeTextureParam(computeShader, multiScatterKernel, "Result", multiScatterTexture);
            scope.Command.SetComputeVectorParam(computeShader, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(multiScatterResolution.x, multiScatterResolution.y));
            scope.Command.DispatchCompute(computeShader, multiScatterKernel, multiScatterResolution.x, multiScatterResolution.y, 1);
        }

        // Calculate ambient
        using (scope.Command.ProfilerScope("Ambient"))
        {
            var ambientKernel = computeShader.FindKernel("Ambient");
            scope.Command.SetComputeTextureParam(computeShader, ambientKernel, "_AtmosphereTransmittance", transmittanceTexture);
            scope.Command.SetComputeTextureParam(computeShader, ambientKernel, "_MultipleScatter", multiScatterTexture);
            scope.Command.SetComputeTextureParam(computeShader, ambientKernel, "Result", ambient);
            scope.Command.SetComputeVectorParam(computeShader, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(ambientWidth, ambientHeight));
            scope.Command.SetComputeVectorParam(computeShader, "_GroundColor", atmosphereProfile.GroundColor.linear);
            scope.Command.DispatchCompute(computeShader, ambientKernel, ambientWidth, ambientHeight, 1);
        }
    }
}