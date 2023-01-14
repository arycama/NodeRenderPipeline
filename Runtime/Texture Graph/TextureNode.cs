using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

public abstract partial class TextureNode : BaseNode
{

    protected int nameId;

    public override void Initialize()
    {
        nameId = Shader.PropertyToID($"{GetType()}_{GetInstanceID()}");
    }

    public override void Cleanup()
    {
    }

    public virtual void Process(Vector3Int resolution, CommandBuffer command)
    {
    }

    public virtual void FinishProcessing(CommandBuffer command)
    {
    }

    protected RenderTextureDescriptor GetDescriptor(Vector3Int resolution)
    {
        return new RenderTextureDescriptor(resolution.x, resolution.y, RenderTextureFormat.RFloat)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            dimension = resolution.z == 1 ? TextureDimension.Tex2D : TextureDimension.Tex3D,
            volumeDepth = resolution.z,
            useMipMap = true
        };
    }

    protected void UpdatePreview(RenderTargetIdentifier input, CommandBuffer command, Vector3Int resolution)
    {
        if (PreviewTexture == null)
            return;

        command.GenerateMips(input);

        var computeShader = Resources.Load<ComputeShader>("PreviewTextureNode");
        command.SetComputeVectorParam(computeShader, "Resolution", new Vector2(PreviewTexture.width, PreviewTexture.height));
        command.SetComputeTextureParam(computeShader, 0, "Input", input);
        command.SetComputeTextureParam(computeShader, 0, "Result", PreviewTexture);

        command.EnableShaderKeywordConditional("DIMENSION_3D", resolution.z > 1);
        command.DispatchNormalized(computeShader, 0, PreviewTexture.width, PreviewTexture.height, 1);
        command.DisableShaderKeywordConditional("DIMENSION_3D", resolution.z > 1);
    }
}