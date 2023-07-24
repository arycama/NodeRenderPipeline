using System.Collections.Generic;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

public partial class DiffuseConvolutionNode : RenderPipelineNode
{
    [SerializeField] private bool isSky;

    [Input] private RenderTargetIdentifier input;
    [Input] private int offset;
    [Input, Output] private GraphicsBuffer result;
    [Input, Output] private NodeConnection connection;

    private ComputeShader computeShader;

    private readonly Dictionary<Camera, ComputeBuffer> ambientCache = new();

    public override void Initialize()
    {
        computeShader = Resources.Load<ComputeShader>("AmbientConvolution");
    }

    public override void Cleanup()
    {
        ambientCache.Cleanup(data => data.Release());
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Diffuse Convolution", true);

        if (isSky)
        {
            scope.Command.SetComputeTextureParam(computeShader, 2, "_SkyVisibilityInput", input);
            scope.Command.SetComputeBufferParam(computeShader, 2, "_SkyVisibilityResult", result);
            scope.Command.SetComputeIntParam(computeShader, "_DstOffset", offset);
            scope.Command.DispatchCompute(computeShader, 2, 1, 1, 1);
        }
        else
        {
            scope.Command.SetComputeTextureParam(computeShader, 0, "_AmbientProbeInputCubemap", input);
            scope.Command.SetComputeBufferParam(computeShader, 0, "_AmbientProbeOutputBuffer", result);
            scope.Command.DispatchCompute(computeShader, 0, 1, 1, 1);
        }
    }
}
