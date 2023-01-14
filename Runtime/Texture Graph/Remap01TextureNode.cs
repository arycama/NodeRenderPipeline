using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Combine/Remap 01")]
public partial class Remap01TextureNode : TextureNode
{
    [Input] private RenderTargetIdentifier value;
    [Input] private RenderTargetIdentifier oldMin;
    [Input] private RenderTargetIdentifier oldMax;
    [Input] private RenderTargetIdentifier newMin;
    [Input] private RenderTargetIdentifier newMax;

    [Output] private RenderTargetIdentifier result;

    public override bool HasPreviewTexture => true;

    public override void Process(Vector3Int resolution, CommandBuffer command)
    {
        var computeShader = Resources.Load<ComputeShader>("Remap01TextureNode");
        var descriptor = GetDescriptor(resolution);

        command.GetTemporaryRT(nameId, descriptor);

        command.SetComputeTextureParam(computeShader, 0, "Value", value);
        command.SetComputeTextureParam(computeShader, 0, "OldMin", oldMin);
        command.SetComputeTextureParam(computeShader, 0, "OldMax", oldMax);
        command.SetComputeTextureParam(computeShader, 0, "NewMin", newMin);
        command.SetComputeTextureParam(computeShader, 0, "NewMax", newMax);
        command.SetComputeTextureParam(computeShader, 0, "Result", nameId);
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