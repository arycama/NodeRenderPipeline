using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Physical Sky")]
public partial class PhysicalSkyNode : RenderPipelineNode
{
    private static readonly IndexedString noiseIds = new("STBN/Vec2/stbn_vec2_2Dx1D_128x128x64_");

    [SerializeField] private AtmosphereProfile atmosphereProfile;
    [Input, SerializeField, Range(1, 64)] private int sampleCount = 16;
    [Input, SerializeField] private bool debugNoise;

    [Input] private RenderTargetIdentifier velocity;
    [Input] private RenderTargetIdentifier exposure;
    [Input] private RenderTargetIdentifier transmittance;
    [Input] private RenderTargetIdentifier multiScatter;
    [Input] private RenderTargetIdentifier depth;

    [Input] private ComputeBuffer ambient;
    [Input] private CullingResults cullingResults;
    [Input] private RenderTargetIdentifier directionalShadows;
    [Input, Output] private RenderTargetIdentifier result;
    [Input, Output] private NodeConnection connection;

    private CameraTextureCache previousFrameCache;

    public override void Initialize()
    {
        previousFrameCache = new("Physical Sky");
    }

    public override void Cleanup()
    {
        previousFrameCache.Dispose();
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var lightDirection = Vector3.up;
        var lightColor = Color.black;
        for (var i = 0; i < cullingResults.visibleLights.Length; i++)
        {
            var light = cullingResults.visibleLights[i];
            if (light.lightType != LightType.Directional)
                continue;

            lightDirection = -light.localToWorldMatrix.Forward();
            lightColor = light.finalColor;
            break;
        }

        var computeShader = Resources.Load<ComputeShader>("PhysicalSkyNew");

        var projMatrix = camera.projectionMatrix;
        var jitterX = projMatrix[0, 2];
        var jitterY = projMatrix[1, 2];
        var viewToWorld = Matrix4x4.Rotate(camera.transform.rotation);
        var mat = Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera.Resolution(), new Vector2(jitterX, jitterY), camera.fieldOfView, camera.aspect, viewToWorld, false);

        var blueNoise2D = Resources.Load<Texture2D>(noiseIds.GetString(debugNoise ? 0 : FrameCount % 64));
        var planetCenterRws = new Vector3(0f, (float)((double)atmosphereProfile.PlanetRadius + camera.transform.position.y), 0f);

        var desc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.RGB111110Float) { enableRandomWrite = true };
        previousFrameCache.GetTexture(camera, desc, out var current, out var previous, FrameCount);

        using var scope = context.ScopedCommandBuffer("Physical Sky");

        var luminanceTemp = Shader.PropertyToID("_LuminanceTemp");
        var transmittanceTemp = Shader.PropertyToID("_TransmittanceTemp");
        scope.Command.GetTemporaryRT(luminanceTemp, desc);
        scope.Command.GetTemporaryRT(transmittanceTemp, desc);

        scope.Command.SetComputeBufferParam(computeShader, 0, "_AmbientSh", ambient);

        scope.Command.SetComputeTextureParam(computeShader, 0, "_Exposure", exposure);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_AtmosphereTransmittance", transmittance);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_MultipleScatter", multiScatter);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_BlueNoise2D", blueNoise2D);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_Depth", depth);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_DirectionalShadows", directionalShadows);

        scope.Command.SetComputeTextureParam(computeShader, 0, "_LuminanceResult", luminanceTemp);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_TransmittanceResult", transmittanceTemp);
        
        scope.Command.SetComputeMatrixParam(computeShader, "_PixelCoordToViewDirWS", mat);

        scope.Command.SetComputeVectorParam(computeShader, "_PlanetOffset", planetCenterRws);
        scope.Command.SetComputeVectorParam(computeShader, "_LightDirection0", lightDirection);
        scope.Command.SetComputeVectorParam(computeShader, "_LightColor0", lightColor);
        scope.Command.SetComputeVectorParam(computeShader, "_GroundColor", atmosphereProfile.GroundColor.linear);

        scope.Command.SetComputeFloatParam(computeShader, "_SampleCount", sampleCount);
        scope.Command.SetComputeFloatParam(computeShader, "_ViewHeight", (float)((double)atmosphereProfile.PlanetRadius + camera.transform.position.y));
       
        scope.Command.DispatchNormalized(computeShader, 0, camera.pixelWidth, camera.pixelHeight, 1);

        scope.Command.SetComputeTextureParam(computeShader, 1, "_Luminance", luminanceTemp);
        scope.Command.SetComputeTextureParam(computeShader, 1, "_Transmittance", transmittanceTemp);
        scope.Command.SetComputeTextureParam(computeShader, 1, "_Current", current);
        scope.Command.SetComputeTextureParam(computeShader, 1, "_Previous", previous);
        scope.Command.SetComputeTextureParam(computeShader, 1, "_Velocity", velocity);
        scope.Command.SetComputeTextureParam(computeShader, 1, "_Result", result);

        scope.Command.DispatchNormalized(computeShader, 1, camera.pixelWidth, camera.pixelHeight, 1);

        scope.Command.ReleaseTemporaryRT(luminanceTemp);
        scope.Command.ReleaseTemporaryRT(transmittanceTemp);
    }

}
