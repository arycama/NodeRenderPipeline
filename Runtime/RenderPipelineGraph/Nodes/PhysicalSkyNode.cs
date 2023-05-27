using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Physical Sky")]
public partial class PhysicalSkyNode : RenderPipelineNode
{
    private static readonly IndexedString noiseIds = new("STBN/Scalar/stbn_scalar_2Dx1Dx1D_128x128x64x1_");

    [SerializeField] private AtmosphereProfile atmosphereProfile;
    [Input, SerializeField, Range(1, 64)] private int sampleCount = 16;

    [Header("Sky")]
    [Input] private RenderTargetIdentifier exposure;
    [Input] private RenderTargetIdentifier transmittance;
    [Input] private RenderTargetIdentifier multiScatter;
    [Input] private RenderTargetIdentifier depth;
    [Input] private ComputeBuffer ambient;
    [Input] private CullingResults cullingResults;
    [Input] private RenderTargetIdentifier directionalShadows;
    [Input, Output] private RenderTargetIdentifier result;
    [Input, Output] private NodeConnection connection;

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

        var blueNoise1D = Resources.Load<Texture2D>(noiseIds.GetString(FrameCount % 64 * 1));
        var planetCenterRws = new Vector3(0f, (float)((double)atmosphereProfile.PlanetRadius + camera.transform.position.y), 0f);

        using var scope = context.ScopedCommandBuffer("Physical Sky");

        scope.Command.SetComputeBufferParam(computeShader, 0, "_AmbientSh", ambient);

        scope.Command.SetComputeTextureParam(computeShader, 0, "_Exposure", exposure);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_AtmosphereTransmittance", transmittance);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_MultipleScatter", multiScatter);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_BlueNoise1D", blueNoise1D);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_Depth", depth);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_DirectionalShadows", directionalShadows);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_Result", result);

        scope.Command.SetComputeMatrixParam(computeShader, "_PixelCoordToViewDirWS", mat);

        scope.Command.SetComputeVectorParam(computeShader, "_PlanetOffset", planetCenterRws);
        scope.Command.SetComputeVectorParam(computeShader, "_LightDirection0", lightDirection);
        scope.Command.SetComputeVectorParam(computeShader, "_LightColor0", lightColor);
        scope.Command.SetComputeVectorParam(computeShader, "_GroundColor", atmosphereProfile.GroundColor.linear);

        scope.Command.SetComputeFloatParam(computeShader, "_SampleCount", sampleCount);
        scope.Command.SetComputeFloatParam(computeShader, "_ViewHeight", (float)((double)atmosphereProfile.PlanetRadius + camera.transform.position.y));
        scope.Command.SetComputeFloatParam(computeShader, "_RayleighHeight", atmosphereProfile.AirAverageHeight);

        // For some testing
        scope.Command.SetComputeVectorParam(computeShader, "_RayleighScatter", atmosphereProfile.AirScatter);
        scope.Command.SetComputeVectorParam(computeShader, "_OzoneAbsorption", atmosphereProfile.AirAbsorption);
        scope.Command.SetComputeFloatParam(computeShader, "_MieScatter", atmosphereProfile.AerosolScatter);
        scope.Command.SetComputeFloatParam(computeShader, "_MieAbsorption", atmosphereProfile.AerosolAbsorption);

        scope.Command.DispatchNormalized(computeShader, 0, camera.pixelWidth, camera.pixelHeight, 1);
    }

}
