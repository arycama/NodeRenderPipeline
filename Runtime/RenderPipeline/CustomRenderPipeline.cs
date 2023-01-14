using UnityEngine;
using UnityEngine.Rendering;

public class CustomRenderPipeline : RenderPipeline
{
    private readonly CustomRenderPipelineAsset renderPipelineAsset;

    public CustomRenderPipeline(CustomRenderPipelineAsset renderPipelineAsset)
    {
        this.renderPipelineAsset = renderPipelineAsset;

        GraphicsSettings.realtimeDirectRectangularAreaLights = true;
        GraphicsSettings.lightsUseColorTemperature = true;
        GraphicsSettings.lightsUseLinearIntensity = true;
        GraphicsSettings.disableBuiltinCustomRenderTextureUpdate = true;

        SupportedRenderingFeatures.active = new SupportedRenderingFeatures()
        {
            defaultMixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.None,
            editableMaterialRenderQueue = false,
            enlighten = false,
            lightmapBakeTypes = LightmapBakeType.Realtime,
            lightmapsModes = LightmapsMode.NonDirectional,
            lightProbeProxyVolumes = false,
            mixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.None,
            motionVectors = true,
            overridesEnvironmentLighting = true,
            overridesFog = true,
            overrideShadowmaskMessage = null,
            overridesLODBias = false,
            overridesMaximumLODLevel = false,
            overridesOtherLightingSettings = true,
            overridesRealtimeReflectionProbes = true,
            overridesShadowmask = true,
            particleSystemInstancing = true,
            receiveShadows = false,
            reflectionProbeModes = SupportedRenderingFeatures.ReflectionProbeModes.None,
            reflectionProbes = false,
            rendererPriority = false,
            rendererProbes = false,
            rendersUIOverlay = true,
            autoAmbientProbeBaking = false,
            autoDefaultReflectionProbeBaking = false,
            enlightenLightmapper = false,
            reflectionProbesBlendDistance = false,
        };

        renderPipelineAsset.Graph.Initialize();
    }

    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        GraphicsSettings.useScriptableRenderPipelineBatching = renderPipelineAsset.UseSRPBatcher;

        renderPipelineAsset.Graph.Render(context, cameras);
    }

    protected override void Dispose(bool disposing)
    {
        renderPipelineAsset.Graph.Cleanup();
    }
}