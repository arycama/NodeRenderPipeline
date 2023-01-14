using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Combine/Math")]
public partial class MathTextureNode : TextureNode
{
    [SerializeField] private MathOperation operation;

    [Input] private RenderTargetIdentifier inputA;
    [Input] private RenderTargetIdentifier inputB;

    [Output] private RenderTargetIdentifier result;

    public override bool HasPreviewTexture => true;

    public override void Process(Vector3Int resolution, CommandBuffer command)
    {
        var computeShader = Resources.Load<ComputeShader>("MathTextureNode");
        var descriptor = GetDescriptor(resolution);

        command.GetTemporaryRT(nameId, descriptor);
        command.SetComputeTextureParam(computeShader, 0, "InputA", inputA);
        command.SetComputeTextureParam(computeShader, 0, "InputB", inputB);
        command.SetComputeTextureParam(computeShader, 0, "Result", nameId);
        command.SetComputeIntParam(computeShader, "Operation", (int)operation);
        command.EnableShaderKeywordConditional("DIMENSION_3D", resolution.z > 1);
        command.DispatchNormalized(computeShader, 0, resolution.x, resolution.y, resolution.z);
        command.DisableShaderKeywordConditional("DIMENSION_3D", resolution.z > 1);

        result = nameId;

        UpdatePreview(result, command, resolution);
    }

    public override void FinishProcessing(CommandBuffer command)
    {
        command.ReleaseTemporaryRT(nameId);
    }
}

public enum MathOperation
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Pow,
    Min,
    Max
}