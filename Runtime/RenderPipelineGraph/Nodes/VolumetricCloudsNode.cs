using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;
using static PlasticPipe.PlasticProtocol.Messages.NegotiationCommand;

[NodeMenuItem("Rendering/Volumetric Clouds")]
public partial class VolumetricCloudsNode : RenderPipelineNode
{
    private static readonly IndexedString noiseIds = new("STBN/Scalar/stbn_scalar_2Dx1Dx1D_128x128x64x1_");

    [SerializeField] private CloudProfile cloudProfile;
    [SerializeField] private AtmosphereProfile atmosphereProfile;

    [Input] private RenderTargetIdentifier noise;
    [Input] private RenderTargetIdentifier detailNoise;
    [Input] private RenderTargetIdentifier weather;
    [Input] private RenderTargetIdentifier depth;
    [Input] private RenderTargetIdentifier atmosphereTransmittance;
    [Input] private RenderTargetIdentifier atmosphereMultiScatter;
    [Input] private ComputeBuffer ambient;
    [Input] private SmartComputeBuffer<DirectionalLightData> directionalLightBuffer;
    [Input] private CullingResults cullingResults;
    [Input] private RenderTargetIdentifier exposure;

    [Output] private RenderTargetIdentifier result;
    [Output] private RenderTargetIdentifier cloudDepth;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        if (cloudProfile == null)
            return;

        var width = camera.pixelWidth >> 1;
        var height = camera.pixelHeight >> 1;

        var projMatrix = camera.projectionMatrix;
        var jitterX = projMatrix[0, 2];
        var jitterY = projMatrix[1, 2];
        var lensShift = new Vector2(jitterX, jitterY);
        var resolution = new Vector4(camera.pixelWidth, camera.pixelHeight, 1f / camera.pixelWidth, 1f / camera.pixelHeight);
        var viewToWorld = Matrix4x4.Rotate(camera.transform.rotation);
        var mat = Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera.Resolution(), lensShift, camera.fieldOfView, camera.aspect, viewToWorld, false);

        var computeShader = Resources.Load<ComputeShader>("VolumetricClouds");
        var cloudKernel = 2;
        var blueNoise1D = Resources.Load<Texture2D>(noiseIds.GetString(FrameCount % 64));

        var tempId = Shader.PropertyToID("_TempCloud5");
        var tempCloudDepthId = Shader.PropertyToID("_TempCloudDepth");
        var tempCloudDescriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
        var tempCloudDepthDescriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.RFloat) { enableRandomWrite = true };

        using var scope = context.ScopedCommandBuffer("Volumetric Clouds", true);
        cloudProfile.SetMaterialProperties(computeShader, cloudKernel, scope.Command, atmosphereProfile.PlanetRadius);

        scope.Command.GetTemporaryRT(tempId, tempCloudDescriptor);
        scope.Command.GetTemporaryRT(tempCloudDepthId, tempCloudDepthDescriptor);
        scope.Command.SetComputeTextureParam(computeShader, cloudKernel, "_BlueNoise1D", blueNoise1D);
        scope.Command.SetComputeTextureParam(computeShader, cloudKernel, "_Depth", depth);
        scope.Command.SetComputeTextureParam(computeShader, cloudKernel, "_CloudNoise", noise);
        scope.Command.SetComputeTextureParam(computeShader, cloudKernel, "_CloudDetail", detailNoise);
        scope.Command.SetComputeTextureParam(computeShader, cloudKernel, "_WeatherTexture", weather);
        scope.Command.SetComputeTextureParam(computeShader, cloudKernel, "_AtmosphereTransmittance", atmosphereTransmittance);
        scope.Command.SetComputeTextureParam(computeShader, cloudKernel, "_MultipleScatter", atmosphereMultiScatter);
        scope.Command.SetComputeTextureParam(computeShader, cloudKernel, "_Result", tempId);
        scope.Command.SetComputeTextureParam(computeShader, cloudKernel, "_DepthResult", tempCloudDepthId);
        scope.Command.SetComputeTextureParam(computeShader, cloudKernel, "_Exposure", exposure);

        scope.Command.SetComputeBufferParam(computeShader, cloudKernel, "_AmbientSh", ambient);
        scope.Command.SetComputeBufferParam(computeShader, cloudKernel, "_DirectionalLightData", directionalLightBuffer);
        scope.Command.SetComputeMatrixParam(computeShader, "_PixelCoordToViewDirWS", mat);
        scope.Command.SetComputeIntParam(computeShader, "_DirectionalLightCount", directionalLightBuffer.Count);
        scope.Command.SetComputeFloatParam(computeShader, "_FogEnabled", CoreUtils.IsSceneViewFogEnabled(camera) ? 1f : 0f);

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
                scope.Command.SetComputeVectorParam(computeShader, "_LightDirection1", -light.localToWorldMatrix.Forward());
                scope.Command.SetComputeVectorParam(computeShader, "_LightColor1", light.finalColor);
            }
            else
            {
                // Only 2 lights supported
                break;
            }
        }

        // If no lights, add a default one
        if (dirLightCount == 0)
        {
            dirLightCount = 1;
            scope.Command.SetComputeVectorParam(computeShader, "_LightDirection0", Vector3.up);
            scope.Command.SetComputeVectorParam(computeShader, "_LightColor0", Vector3.one * 120000);
        }

        var keyword = dirLightCount == 2 ? "LIGHT_COUNT_TWO" : (dirLightCount == 1 ? "LIGHT_COUNT_ONE" : string.Empty);
        using var keywordScope = scope.Command.KeywordScope(keyword);

        scope.Command.DispatchNormalized(computeShader, cloudKernel, width, height, 1);

        cloudDepth = tempCloudDepthId;
        result = tempId;
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(Shader.PropertyToID("_TempCloud5"));
        scope.Command.ReleaseTemporaryRT(Shader.PropertyToID("_TempCloudDepth"));
    }
}