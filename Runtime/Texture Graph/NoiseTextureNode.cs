using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Input/Noise")]
public partial class NoiseTextureNode : TextureNode
{
    [SerializeField, Min(0)] private int frequency = 32;
    [SerializeField, Range(1, 9)] private int layers = 3;
    [SerializeField, Range(0, 1)] private float gain = 0.5f;
    [SerializeField, Range(1, 2)] private float lacunarity = 2f;
    [SerializeField] private NoiseType noiseType = NoiseType.Simplex;
    [SerializeField] private FractalType fractalType = FractalType.Fbm;

    [Output] private RenderTargetIdentifier result;

    public override bool HasPreviewTexture => true;

    public override void Process(Vector3Int resolution, CommandBuffer command)
    {
        // Compute max scale
        var scale = 0f;
        for (var i = 0; i < layers; i++)
        {
            scale += Mathf.Pow(gain, i);
        }

        var computeShader = Resources.Load<ComputeShader>("NoiseTextureNode");
        var descriptor = GetDescriptor(resolution);

        command.GetTemporaryRT(nameId, descriptor);
        command.SetComputeTextureParam(computeShader, 0, "_Result", nameId);

        command.SetComputeFloatParam(computeShader, "_Frequency", frequency);
        command.SetComputeFloatParam(computeShader, "_Gain", gain);
        command.SetComputeFloatParam(computeShader, "_Lacunarity", lacunarity);
        command.SetComputeFloatParam(computeShader, "_Amplitude", 1f);
        command.SetComputeFloatParam(computeShader, "_Octaves", layers);
        command.SetComputeFloatParam(computeShader, "_Scale", scale);

        command.EnableShaderKeyword(noiseType.ToString().ToUpperInvariant());
        command.EnableShaderKeyword(fractalType.ToString().ToUpperInvariant());
        command.EnableShaderKeywordConditional("DIMENSION_3D", resolution.z > 1);

        command.DispatchNormalized(computeShader, 0, resolution.x, resolution.y, resolution.z);

        command.DisableShaderKeyword(noiseType.ToString().ToUpperInvariant());
        command.DisableShaderKeyword(fractalType.ToString().ToUpperInvariant());
        command.DisableShaderKeywordConditional("DIMENSION_3D", resolution.z > 1);

        result = nameId;

        UpdatePreview(result, command, resolution);
    }

    public override void FinishProcessing(CommandBuffer command)
    {
        command.ReleaseTemporaryRT(nameId);
    }
}

public enum NoiseType
{
    Simplex,
    Worley,
    Perlin
}

public enum FractalType
{
    Fbm,
    Billow,
    Ridged
}