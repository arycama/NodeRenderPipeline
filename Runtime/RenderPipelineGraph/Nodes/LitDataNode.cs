using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Lit Data")]
public partial class LitDataNode : RenderPipelineNode
{
    [SerializeField, Pow2(128)] private int directionalAlbedoResolution = 32;
    [SerializeField, Pow2(8192)] private uint directionalAlbedoSamples = 4096;
    [SerializeField, Pow2(128)] private int averageAlbedoResolution = 16;
    [SerializeField, Pow2(8192)] private uint averageAlbedoSamples = 4096;
    [SerializeField, Pow2(128)] private int directionalAlbedoMsResolution = 16;
    [SerializeField, Pow2(8192)] private uint directionalAlbedoMSamples = 4096;
    [SerializeField, Pow2(128)] private int averageAlbedoMsResolution = 16;
    [SerializeField, Pow2(8192)] private uint averageAlbedoMsSamples = 4096;

    [Input, Output] private NodeConnection connection;

    private RenderTexture directionalAlbedo, averageAlbedo, directionalAlbedoMs, averageAlbedoMs, specularOcclusion;
    private Texture2D ltcData;

    private const int k_LtcLUTMatrixDim = 3; // size of the matrix (3x3)
    private const int k_LtcLUTResolution = 64;

    private struct GGXLookupConstants
    {
        private Vector4 ggxDirectionalAlbedoRemap;
        private Vector2 ggxAverageAlbedoRemap;
        private Vector2 ggxDirectionalAlbedoMSScaleOffset;
        private Vector4 ggxAverageAlbedoMSRemap;

        public GGXLookupConstants(Vector4 ggxDirectionalAlbedoRemap, Vector2 ggxAverageAlbedoRemap, Vector2 ggxDirectionalAlbedoMSScaleOffset, Vector4 ggxAverageAlbedoMSRemap)
        {
            this.ggxDirectionalAlbedoRemap = ggxDirectionalAlbedoRemap;
            this.ggxAverageAlbedoRemap = ggxAverageAlbedoRemap;
            this.ggxDirectionalAlbedoMSScaleOffset = ggxDirectionalAlbedoMSScaleOffset;
            this.ggxAverageAlbedoMSRemap = ggxAverageAlbedoMSRemap;
        }
    }

    private struct GGXLookupCalculationConstants
    {
        private Vector2 directionalAlbedoScaleOffset;
        private readonly uint directionalAlbedoSamples;
        private readonly float directionalAlbedoSamplesRcp;

        private Vector4 directionalAlbedoRemap;
        private Vector2 averageAlbedoRemap;
        private readonly float averageAlbedoScaleOffset;
        private readonly uint averageAlbedoSamples;
        private readonly float averageAlbedoSamplesRcp;
        private readonly float averageAlbedoSamplesMinusOneRcp;

        private readonly uint directionalAlbedoMsSamples;
        private readonly float directionalAlbedoMsSamplesRcp;
        private Vector3 directionalAlbedoMsScaleOffset;

        private readonly uint averageAlbedoMsSamples;
        private readonly float averageAlbedoMsSamplesRcp;
        private Vector2 averageAlbedoMsScaleOffset;
        private readonly float averageAlbedoMsSamplesMinusOneRcp;

        public GGXLookupCalculationConstants(Vector2 directionalAlbedoScaleOffset, uint directionalAlbedoSamples, float directionalAlbedoSamplesRcp, Vector4 directionalAlbedoRemap, Vector2 averageAlbedoRemap, float averageAlbedoScaleOffset, uint averageAlbedoSamples, float averageAlbedoSamplesRcp, float averageAlbedoSamplesMinusOneRcp, uint directionalAlbedoMsSamples, float directionalAlbedoMsSamplesRcp, Vector3 directionalAlbedoMsScaleOffset, uint averageAlbedoMsSamples, float averageAlbedoMsSamplesRcp, Vector2 averageAlbedoMsScaleOffset, float averageAlbedoMsSamplesMinusOneRcp)
        {
            this.directionalAlbedoScaleOffset = directionalAlbedoScaleOffset;
            this.directionalAlbedoSamples = directionalAlbedoSamples;
            this.directionalAlbedoSamplesRcp = directionalAlbedoSamplesRcp;
            this.directionalAlbedoRemap = directionalAlbedoRemap;
            this.averageAlbedoRemap = averageAlbedoRemap;
            this.averageAlbedoScaleOffset = averageAlbedoScaleOffset;
            this.averageAlbedoSamples = averageAlbedoSamples;
            this.averageAlbedoSamplesRcp = averageAlbedoSamplesRcp;
            this.averageAlbedoSamplesMinusOneRcp = averageAlbedoSamplesMinusOneRcp;
            this.directionalAlbedoMsSamples = directionalAlbedoMsSamples;
            this.directionalAlbedoMsSamplesRcp = directionalAlbedoMsSamplesRcp;
            this.directionalAlbedoMsScaleOffset = directionalAlbedoMsScaleOffset;
            this.averageAlbedoMsSamples = averageAlbedoMsSamples;
            this.averageAlbedoMsSamplesRcp = averageAlbedoMsSamplesRcp;
            this.averageAlbedoMsScaleOffset = averageAlbedoMsScaleOffset;
            this.averageAlbedoMsSamplesMinusOneRcp = averageAlbedoMsSamplesMinusOneRcp;
        }
    }

    public override void Initialize()
    {
        directionalAlbedo = new RenderTexture(directionalAlbedoResolution, directionalAlbedoResolution, 0, RenderTextureFormat.RG32, RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = true,
            hideFlags = HideFlags.HideAndDontSave,
            name = "GGX Directional Albedo"
        }.Created();

        averageAlbedo = new RenderTexture(averageAlbedoResolution, 1, 0, RenderTextureFormat.R16, RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = true,
            hideFlags = HideFlags.HideAndDontSave,
            name = "GGX Average Albedo"
        }.Created();

        directionalAlbedoMs = new RenderTexture(directionalAlbedoMsResolution, directionalAlbedoMsResolution, 0, RenderTextureFormat.R16, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.Tex3D,
            enableRandomWrite = true,
            hideFlags = HideFlags.HideAndDontSave,
            name = "GGX Directional Albedo MS",
            volumeDepth = directionalAlbedoMsResolution
        }.Created();

        averageAlbedoMs = new RenderTexture(averageAlbedoMsResolution, averageAlbedoMsResolution, 0, RenderTextureFormat.R16, RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = true,
            hideFlags = HideFlags.HideAndDontSave,
            name = "GGX Average Albedo MS"
        }.Created();

        specularOcclusion = new RenderTexture(32, 32, 0, RenderTextureFormat.R16)
        {
            dimension = TextureDimension.Tex3D,
            enableRandomWrite = true,
            hideFlags = HideFlags.HideAndDontSave,
            name = "GGX Specular Occlusion",
            volumeDepth = 32 * 32
        }.Created();

        var ggxLookupCalculationConstants = new GGXLookupCalculationConstants
        (
            GraphicsUtilities.ThreadIdScaleOffset01(directionalAlbedoResolution, directionalAlbedoResolution),
            directionalAlbedoSamples,
            1f / directionalAlbedoSamples,
            GraphicsUtilities.HalfTexelRemap(averageAlbedoResolution, averageAlbedoResolution),
            GraphicsUtilities.HalfTexelRemap(averageAlbedoResolution),
            1f / averageAlbedoResolution,
            averageAlbedoSamples,
            1f / averageAlbedoSamples,
            1f / (averageAlbedoSamples - 1),
            directionalAlbedoMSamples,
            1f / directionalAlbedoMSamples,
            GraphicsUtilities.ThreadIdScaleOffset01(directionalAlbedoMsResolution, directionalAlbedoMsResolution, directionalAlbedoMsResolution),
            averageAlbedoMsSamples,
            1f / averageAlbedoMsSamples,
            GraphicsUtilities.ThreadIdScaleOffset01(averageAlbedoMsResolution, averageAlbedoMsResolution),
            1f / (averageAlbedoMsSamples - 1)
        );

        // I think a lot of these can be floats instead of vectors
        var ggxLookupConstants = new GGXLookupConstants
        (
            GraphicsUtilities.HalfTexelRemap(directionalAlbedoResolution, directionalAlbedoResolution),
            GraphicsUtilities.HalfTexelRemap(averageAlbedoResolution),
            GraphicsUtilities.HalfTexelRemap(directionalAlbedoMsResolution),
            GraphicsUtilities.HalfTexelRemap(averageAlbedoMsResolution, averageAlbedoMsResolution)
        );

        var command = CommandBufferPool.Get("Lit Data");
        var computeShader = Resources.Load<ComputeShader>("PreIntegratedFGD");
        ConstantBuffer.Push(command, ggxLookupCalculationConstants, computeShader, Shader.PropertyToID("GGXLookupCalculationConstants"));
        ConstantBuffer.PushGlobal(command, ggxLookupConstants, Shader.PropertyToID("GGXLookupConstants"));

        command.SetComputeTextureParam(computeShader, 0, "_DirectionalAlbedoResult", directionalAlbedo);
        command.DispatchNormalized(computeShader, 0, directionalAlbedoResolution, directionalAlbedoResolution, 1);
        command.SetGlobalTexture("_GGXDirectionalAlbedo", directionalAlbedo);

        command.SetComputeTextureParam(computeShader, 1, "_AverageAlbedoResult", averageAlbedo);
        command.DispatchNormalized(computeShader, 1, averageAlbedoResolution, 1, 1);
        command.SetGlobalTexture("_GGXAverageAlbedo", averageAlbedo);

        command.SetComputeTextureParam(computeShader, 2, "_DirectionalAlbedoMsResult", directionalAlbedoMs);
        command.DispatchNormalized(computeShader, 2, directionalAlbedoMsResolution, directionalAlbedoMsResolution, directionalAlbedoMsResolution);
        command.SetGlobalTexture("_GGXDirectionalAlbedoMS", directionalAlbedoMs);

        command.SetComputeTextureParam(computeShader, 3, "_AverageAlbedoMsResult", averageAlbedoMs);
        command.DispatchNormalized(computeShader, 3, averageAlbedoMsResolution, averageAlbedoMsResolution, 1);
        command.SetGlobalTexture("_GGXAverageAlbedoMS", averageAlbedoMs);

        // Specular occlusion
        command.SetComputeTextureParam(computeShader, 4, "_SpecularOcclusionResult", specularOcclusion);
        command.SetComputeIntParam(computeShader, "_SpecularOcclusionResolution", 32);
        command.DispatchNormalized(computeShader, 4, 32, 32, 32 * 32);
        command.SetGlobalTexture("_GGXSpecularOcclusion", specularOcclusion);

        ltcData = new Texture2D(k_LtcLUTResolution, k_LtcLUTResolution, TextureFormat.RGBAHalf, false /*mipmap*/, true /* linear */)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        int count = k_LtcLUTResolution * k_LtcLUTResolution;
        Color[] pixels = new Color[count];

        float clampValue = 65504.0f;

        for (int i = 0; i < count; i++)
        {
            // Both GGX and Disney Diffuse BRDFs have zero values in columns 1, 3, 5, 7.
            // Column 8 contains only ones.
            pixels[i] = new Color(Mathf.Min(clampValue, (float)s_LtcGGXMatrixData[i, 0]),
                Mathf.Min(clampValue, (float)s_LtcGGXMatrixData[i, 2]),
                Mathf.Min(clampValue, (float)s_LtcGGXMatrixData[i, 4]),
                Mathf.Min(clampValue, (float)s_LtcGGXMatrixData[i, 6]));
        }

        ltcData.SetPixels(pixels);
        ltcData.Apply();
        command.SetGlobalTexture("_LtcData", ltcData);

        Graphics.ExecuteCommandBuffer(command);
        CommandBufferPool.Release(command);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.SetGlobalTexture("_GGXDirectionalAlbedo", directionalAlbedo);
        scope.Command.SetGlobalTexture("_GGXAverageAlbedo", averageAlbedo);
        scope.Command.SetGlobalTexture("_LtcData", ltcData);
        scope.Command.SetGlobalTexture("_GGXDirectionalAlbedoMS", directionalAlbedoMs);
        scope.Command.SetGlobalTexture("_GGXAverageAlbedoMS", averageAlbedoMs);
        scope.Command.SetGlobalTexture("_GGXSpecularOcclusion", specularOcclusion);

        // I think a lot of these can be floats instead of vectors
        var ggxLookupConstants = new GGXLookupConstants
        (
            GraphicsUtilities.HalfTexelRemap(directionalAlbedoResolution, directionalAlbedoResolution),
            GraphicsUtilities.HalfTexelRemap(averageAlbedoResolution),
            GraphicsUtilities.HalfTexelRemap(directionalAlbedoMsResolution),
            GraphicsUtilities.HalfTexelRemap(averageAlbedoMsResolution, averageAlbedoMsResolution)
        );

        ConstantBuffer.PushGlobal(scope.Command, ggxLookupConstants, Shader.PropertyToID("GGXLookupConstants"));
    }

    public override void Cleanup()
    {
        GraphicsUtilities.SafeDestroy(ref directionalAlbedo);
        GraphicsUtilities.SafeDestroy(ref directionalAlbedoMs);
        GraphicsUtilities.SafeDestroy(ref averageAlbedo);
        GraphicsUtilities.SafeDestroy(ref averageAlbedoMs);
        GraphicsUtilities.SafeDestroy(ref specularOcclusion);
        GraphicsUtilities.SafeDestroy(ref ltcData);
    }
}