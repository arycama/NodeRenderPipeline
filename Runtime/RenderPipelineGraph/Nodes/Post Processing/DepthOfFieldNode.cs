using System.Collections;
using System.Collections.Generic;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Depth of Field")]
public partial class DepthOfFieldNode : RenderPipelineNode
{
    private static readonly IndexedString noiseIds = new("STBN/Scalar/stbn_scalar_2Dx1Dx1D_128x128x64x1_");

    [Input, SerializeField] private float sensorWidth = 24.89f;
    [Input, SerializeField] private float sensorHeight = 24.89f;
    [Input, SerializeField] private float focalDistance = 15f;
    [Input, SerializeField] private float apertureSize = 1f;

    [Input, SerializeField] private float sampleRadius = 8f;
    [Input, SerializeField] private int sampleCount = 8;

    [Input, SerializeField] private bool debugNoise = false;

    [Input] private RenderTargetIdentifier color;
    [Input] private RenderTargetIdentifier depth;
    [Output] private RenderTargetIdentifier result;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var computeShader = Resources.Load<ComputeShader>("DepthOfField");
        var blueNoise1D = Resources.Load<Texture2D>(noiseIds.GetString(debugNoise ? 0 : FrameCount % 64));

        using var scope = context.ScopedCommandBuffer("Depth of Field");

        var desc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.RGB111110Float) { enableRandomWrite = true };
        var tempId = Shader.PropertyToID("_DepthOfFieldResult");
        scope.Command.GetTemporaryRT(tempId, desc);

        var focalLength = sensorHeight / (2.0f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2.0f));

        var sensorScale = (0.5f / sensorHeight) * camera.pixelHeight;

        float F = focalLength;
        float A = focalLength / apertureSize;
        float P = focalDistance;
        float maxFarCoC = (A * F) / (P - F) / sensorHeight * camera.pixelHeight;

        scope.Command.SetComputeFloatParam(computeShader, "_FocalDistance", focalDistance);
        scope.Command.SetComputeFloatParam(computeShader, "_FocalLength", focalLength / 1000f);
        scope.Command.SetComputeFloatParam(computeShader, "_ApertureSize", focalLength / apertureSize);
        scope.Command.SetComputeFloatParam(computeShader, "_MaxCoC", maxFarCoC);

        scope.Command.SetComputeFloatParam(computeShader, "_SampleRadius", sampleRadius);
        scope.Command.SetComputeIntParam(computeShader, "_SampleCount", sampleCount);

        scope.Command.SetComputeTextureParam(computeShader, 0, "_Input", color);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_Depth", depth);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_Result", tempId);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_BlueNoise1D", blueNoise1D);

        scope.Command.DispatchNormalized(computeShader, 0, camera.pixelWidth, camera.pixelHeight, 1);
        result = tempId;
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        var tempId = Shader.PropertyToID("_DepthOfFieldResult");
        var scope = context.ScopedCommandBuffer("Depth of Field");
        scope.Command.ReleaseTemporaryRT(tempId);
    }
}
