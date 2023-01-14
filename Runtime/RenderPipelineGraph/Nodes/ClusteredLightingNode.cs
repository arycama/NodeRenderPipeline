using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Clustered Lighting")]
public partial class ClusteredLightingNode : RenderPipelineNode
{
    [SerializeField, Pow2(64), Output] private int tileSize = 16;
    [SerializeField, Pow2(64)] private int clusterDepth = 32;
    [SerializeField, Pow2(64)] private int maxLightsPerTile = 32;

    [Input] private SmartComputeBuffer<LightData> lightData;

    [Output] private ComputeBuffer lightList;
    [Output] private RenderTargetIdentifier lightCluster;

    [Output] private float clusterScale;
    [Output] private float clusterBias;
    [Input, Output] private NodeConnection connection;

    private ComputeBuffer counterBuffer;

    private static readonly uint[] zeroArray = new uint[1] { 0 };
    private int lightClusterId;

    private int DivRoundUp(int x, int y) => (x + y - 1) / y;

    public override void Initialize()
    {
        counterBuffer = new ComputeBuffer(1, sizeof(uint)) { name = nameof(counterBuffer) };
        lightClusterId = GetShaderPropertyId();
        lightCluster = lightClusterId;
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var clusterWidth = DivRoundUp(camera.pixelWidth, tileSize);
        var clusterHeight = DivRoundUp(camera.pixelHeight, tileSize);
        var clusterCount = clusterWidth * clusterHeight * clusterDepth;

        GraphicsUtilities.SafeExpand(ref lightList, clusterCount * maxLightsPerTile, sizeof(int), ComputeBufferType.Default);

        var descriptor = new RenderTextureDescriptor(clusterWidth, clusterHeight, RenderTextureFormat.RGInt)
        {
            dimension = TextureDimension.Tex3D,
            enableRandomWrite = true,
            volumeDepth = clusterDepth
        };

        clusterScale = clusterDepth / Mathf.Log(camera.farClipPlane / camera.nearClipPlane, 2f);
        clusterBias = -(clusterDepth * Mathf.Log(camera.nearClipPlane, 2f) / Mathf.Log(camera.farClipPlane / camera.nearClipPlane, 2f));

        var computeShader = Resources.Load<ComputeShader>("ClusteredLightCulling");

        using var scope = context.ScopedCommandBuffer("Clustered Light Culling", true, true);
        scope.Command.GetTemporaryRT(lightClusterId, descriptor);
        scope.Command.SetBufferData(counterBuffer, zeroArray);
        scope.Command.SetComputeBufferParam(computeShader, 0, "_LightData", lightData);
        scope.Command.SetComputeBufferParam(computeShader, 0, "_LightCounter", counterBuffer);
        scope.Command.SetComputeBufferParam(computeShader, 0, "_LightClusterListWrite", lightList);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_LightClusterIndicesWrite", lightClusterId);
        scope.Command.SetComputeIntParam(computeShader, "_LightCount", lightData.Count);
        scope.Command.SetComputeIntParam(computeShader, "_TileSize", tileSize);
        scope.Command.SetComputeFloatParam(computeShader, "_RcpClusterDepth", 1f / clusterDepth);
        scope.Command.DispatchNormalized(computeShader, 0, clusterWidth, clusterHeight, clusterDepth);
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(lightClusterId);
    }

    public override void Cleanup()
    {
        GraphicsUtilities.SafeDestroy(ref counterBuffer);
        GraphicsUtilities.SafeDestroy(ref lightList);
    }
}