using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Cloud Shadows")]
public partial class CloudShadowsNode : RenderPipelineNode
{
    private static readonly int cloudShadowId = Shader.PropertyToID("_CloudShadow");

    [SerializeField, Pow2(2048)] private int resolution = 1024;
    [SerializeField] private AtmosphereProfile atmosphereProfile;
    [SerializeField] private float radius = 150000f;
    [SerializeField] private int samples = 24;
    [SerializeField] private CloudProfile cloudProfile = null;

    [Input] private RenderTargetIdentifier cloudNoise;
    [Input] private RenderTargetIdentifier detailNoise;
    [Input] private RenderTargetIdentifier weather;
    [Input] private CullingResults cullingResults;

    [Output] private readonly RenderTargetIdentifier result = cloudShadowId;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Cloud Shadows", true);

        if (cloudProfile == null)
        {
            scope.Command.SetGlobalTexture("_CloudShadow", Texture2D.whiteTexture);
            return;
        }

        // Rendering above or below is done depending on camera position
        var planetRadius = atmosphereProfile.PlanetRadius;
        var position1 = camera.transform.position;
        var planetCenter = new Vector3(position1.x, -planetRadius, position1.z);

        var computeShader = Resources.Load<ComputeShader>("VolumetricClouds");
        var shadowKernel = 1;

        scope.Command.SetComputeTextureParam(computeShader, shadowKernel, "_CloudNoise", cloudNoise);
        scope.Command.SetComputeTextureParam(computeShader, shadowKernel, "_CloudDetail", detailNoise);
        scope.Command.SetComputeTextureParam(computeShader, shadowKernel, "_WeatherTexture", weather);
        cloudProfile.SetMaterialProperties(computeShader, shadowKernel, scope.Command, planetRadius);

        var startHeight = cloudProfile.StartHeight;
        scope.Command.SetComputeFloatParam(computeShader, "_CloudHeight", startHeight);
        scope.Command.SetComputeFloatParam(computeShader, "_ShadowSamples", samples);

        var lightDirection = Vector3.up;
        var lightRotation = Quaternion.LookRotation(Vector3.down);

        for (var i = 0; i < cullingResults.visibleLights.Length; i++)
        {
            var light = cullingResults.visibleLights[i];
            if (light.lightType != LightType.Directional)
                continue;

            lightDirection = -light.localToWorldMatrix.Forward();
            lightRotation = light.localToWorldMatrix.rotation;

            // Only 1 light supported
            break;
        }

        var res = new Vector4(resolution, resolution, 1f / resolution, 1f / resolution);

        var viewPosition = Vector3.zero + lightDirection * radius - camera.transform.position + new Vector3(camera.transform.position.x, 0f, camera.transform.position.z);
        var viewMatrix = Matrix4x4.TRS(viewPosition, lightRotation, new Vector3(1f, 1f, -1f)).inverse;
        var projectionMatrix = Matrix4x4.Ortho(-radius, radius, -radius, radius, -radius, radius);
        var projectionMatrix2 = Matrix4x4.Ortho(-radius, radius, -radius, radius, 0, radius);

        var viewProjection = projectionMatrix * viewMatrix;
        var worldToShadow = (projectionMatrix2 * viewMatrix).ConvertToAtlasMatrix(false);

        scope.Command.SetComputeFloatParam(computeShader, "_CloudDepthScale", 1f / radius);
        scope.Command.SetComputeVectorParam(computeShader, "_ScreenSizeCloudShadow", res);
        scope.Command.SetComputeMatrixParam(computeShader, "_InvViewProjMatrixCloudShadow", viewProjection.inverse);
        scope.Command.SetGlobalMatrix("_WorldToCloudShadow", worldToShadow);
        scope.Command.SetGlobalFloat("_CloudDepthInvScale", radius);

        scope.Command.SetComputeVectorParam(computeShader, "_LightDirection0", -lightDirection);

        var descriptor = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RGB111110Float) { enableRandomWrite = true };
        scope.Command.GetTemporaryRT(cloudShadowId, descriptor);
        scope.Command.SetComputeTextureParam(computeShader, shadowKernel, "ShadowResult", cloudShadowId);

        using var keywordScope = scope.Command.KeywordScope("LIGHT_COUNT_ONE");
        scope.Command.DispatchNormalized(computeShader, shadowKernel, resolution, resolution, 1);
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(cloudShadowId);
    }
}