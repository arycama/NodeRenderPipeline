using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

public partial class TextureOutputNode : TextureNode
{
    [Input] private RenderTargetIdentifier inputR;
    [Input] private RenderTargetIdentifier inputG;
    [Input] private RenderTargetIdentifier inputB;

    public void GetResult(RenderTargetIdentifier result, CommandBuffer command, Vector3Int resolution)
    {
        var computeShader = Resources.Load<ComputeShader>("TextureOutputNode");

        command.SetComputeTextureParam(computeShader, 0, "InputR", inputR);
        command.SetComputeTextureParam(computeShader, 0, "InputG", inputG);
        command.SetComputeTextureParam(computeShader, 0, "InputB", inputB);
        command.SetComputeTextureParam(computeShader, 0, "Result", result);
        command.EnableShaderKeywordConditional("DIMENSION_3D", resolution.z > 1);
        command.DispatchNormalized(computeShader, 0, resolution.x, resolution.y, resolution.z);
        command.DisableShaderKeywordConditional("DIMENSION_3D", resolution.z > 1);
    }
}