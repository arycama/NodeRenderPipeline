using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Input/Constant")]
public partial class ConstantTextureNode : TextureNode
{
    [SerializeField] private float value;

    [Output] private RenderTargetIdentifier result;

    public override void Process(Vector3Int resolution, CommandBuffer command)
    {
        var computeShader = Resources.Load<ComputeShader>("ConstantTextureNode");
        var descriptor = GetDescriptor(resolution);

        command.GetTemporaryRT(nameId, descriptor);
        command.SetComputeFloatParam(computeShader, "Value", value);

        command.SetComputeTextureParam(computeShader, 0, "Result", nameId);
        command.EnableShaderKeywordConditional("DIMENSION_3D", resolution.z > 1);
        command.DispatchNormalized(computeShader, 0, resolution.x, resolution.y, resolution.z);
        command.DisableShaderKeywordConditional("DIMENSION_3D", resolution.z > 1);

        result = nameId;
    }

    public override void FinishProcessing(CommandBuffer command)
    {
        command.ReleaseTemporaryRT(nameId);
    }
}