using System.Collections.Generic;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Sky Reflection")]
public partial class SkyReflectionNode : RenderPipelineNode
{
    [SerializeField, Pow2(512)] private int resolution = 128;
    [SerializeField] private AtmosphereProfile atmosphereProfile;
    [SerializeField] private CloudProfile cloudProfile;

    [Header("Sky")]
    [Input] private RenderTargetIdentifier exposure;
    [Input] private RenderTargetIdentifier cloudCoverage;
    [Input] private RenderTargetIdentifier atmosphereTransmittance;
    [Input] private RenderTargetIdentifier multiScatter;
    [Input] private RenderTargetIdentifier cloudAmbient;
    [Input] private CullingResults cullingResults;
    [Output] private RenderTargetIdentifier reflection;

    [Header("Clouds")]
    [Input] private RenderTargetIdentifier noise;
    [Input] private RenderTargetIdentifier detailNoise;
    [Input] private RenderTargetIdentifier weather;

    [Input, Output] private NodeConnection connection;

    private int propertyId;

    public override void Initialize()
    {
        propertyId = GetShaderPropertyId("Sky Reflection");
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Sky Reflection", true);
        GraphicsUtilities.SetupCameraProperties(scope.Command, FrameCount, camera, context, camera.Resolution(), out var viewProjectionMatrix);

        var tempSkyId = Shader.PropertyToID("_TempSky");
        scope.Command.GetTemporaryRT(tempSkyId, resolution, resolution, 0, FilterMode.Bilinear, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear, 1, true);

        var skyDescriptor = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RGB111110Float)
        {
            autoGenerateMips = false,
            dimension = TextureDimension.Cube,
            enableRandomWrite = true,
            useMipMap = true
        };

        scope.Command.GetTemporaryRT(propertyId, skyDescriptor);

        var skyComputeShader = Resources.Load<ComputeShader>("PhysicalSky");

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
                scope.Command.SetComputeVectorParam(skyComputeShader, "_LightDirection0", -light.localToWorldMatrix.Forward());
                scope.Command.SetComputeVectorParam(skyComputeShader, "_LightColor0", light.finalColor);
            }
            else if (dirLightCount == 2)
            {
                scope.Command.SetComputeVectorParam(skyComputeShader, "_LightDirection1", -light.localToWorldMatrix.Forward());
                scope.Command.SetComputeVectorParam(skyComputeShader, "_LightColor1", light.finalColor);
            }
            else
            {
                // Only 2 lights supported
                break;
            }
        }

        var kernel = skyComputeShader.FindKernel("SkyReflection");
        var planetCenterRws = new Vector3(0f, (float)((double)atmosphereProfile.PlanetRadius + camera.transform.position.y), 0f);
        scope.Command.SetComputeVectorParam(skyComputeShader, "_PlanetOffset", planetCenterRws);

        scope.Command.SetComputeTextureParam(skyComputeShader, kernel, "_Result", tempSkyId);
        scope.Command.SetComputeTextureParam(skyComputeShader, kernel, "_StarMap", atmosphereProfile.StarTexture);
        scope.Command.SetComputeTextureParam(skyComputeShader, kernel, "_AtmosphereTransmittance", atmosphereTransmittance);
        scope.Command.SetComputeTextureParam(skyComputeShader, kernel, "_MultipleScatter", multiScatter);
        scope.Command.SetComputeTextureParam(skyComputeShader, kernel, "_Exposure", exposure);
        scope.Command.SetComputeTextureParam(skyComputeShader, kernel, "_CloudCoverage", cloudCoverage);
        scope.Command.SetComputeTextureParam(skyComputeShader, kernel, "_CloudAmbient", cloudAmbient);

        scope.Command.SetComputeVectorParam(skyComputeShader, "_StarColor", atmosphereProfile.StarColor.linear);
        scope.Command.SetComputeVectorParam(skyComputeShader, "_GroundColor", atmosphereProfile.GroundColor.linear);

        // Clouds
        scope.Command.SetComputeTextureParam(skyComputeShader, kernel, "_CloudNoise", noise);
        scope.Command.SetComputeTextureParam(skyComputeShader, kernel, "_CloudDetail", detailNoise);
        scope.Command.SetComputeTextureParam(skyComputeShader, kernel, "_WeatherTexture", weather);
        cloudProfile.SetMaterialProperties(skyComputeShader, kernel, scope.Command, atmosphereProfile.PlanetRadius);

        for (var i = 0; i < 6; i++)
        {
            var up = CoreUtils.upVectorList[i];
            var fwd = CoreUtils.lookAtList[i];

            var viewToWorld = Matrix4x4.LookAt(Vector3.zero, fwd, up);
            var res = new Vector2Int(resolution, resolution);
            var mat = Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(res, Vector2.zero, 90f, 1f, viewToWorld, true);

            scope.Command.SetComputeMatrixParam(skyComputeShader, "_PixelCoordToViewDirWS", mat);

            var keyword = dirLightCount == 2 ? "LIGHT_COUNT_TWO" : (dirLightCount == 1 ? "LIGHT_COUNT_ONE" : string.Empty);
            using (var keywordScope = scope.Command.KeywordScope(keyword))
                scope.Command.DispatchNormalized(skyComputeShader, kernel, resolution, resolution, 1);

            scope.Command.CopyTexture(tempSkyId, 0, 0, propertyId, i, 0);
        }

        scope.Command.ReleaseTemporaryRT(tempSkyId);

        scope.Command.GenerateMips(propertyId);
        reflection = propertyId;
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(propertyId);
    }
}