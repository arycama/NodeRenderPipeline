using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Volumetric Lighting")]
public partial class VolumetricLightingNode : RenderPipelineNode
{
    private static readonly IndexedString noiseIds = new("STBN/Scalar/stbn_scalar_2Dx1Dx1D_128x128x64x1_");
    private static readonly int id = Shader.PropertyToID("_VolLightBuffer");

    [SerializeField, Pow2(32)] private int tileSize = 8;
    [SerializeField, Pow2(256)] private int depthSlices = 64;
    [SerializeField, Min(0f)] private float maxDepth = 128f;

    [Input] private int clusterTileSize;
    [Input] private int cascadeCount;

    [Input] private SmartComputeBuffer<DirectionalLightData> directionalLightBuffer;
    [Input] private ComputeBuffer lightList;
    [Input] private SmartComputeBuffer<LightData> lightData;
    [Input] private SmartComputeBuffer<Matrix4x4> spotlightShadowMatrices;

    [Input] private RenderTargetIdentifier maxZ;
    [Input] private RenderTargetIdentifier exposure;
    [Input] private RenderTargetIdentifier lightCluster;
    [Input] private RenderTargetIdentifier multipleScatter;
    [Input] private RenderTargetIdentifier cloudShadow;

    [Output] private RenderTargetIdentifier result = id;
    [Input, Output] private NodeConnection connection;

    private CameraTextureCache textureCache;

    public override void Initialize()
    {
        textureCache = new("Volumetric Lighting");
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var width = Mathf.CeilToInt(camera.pixelWidth / (float)tileSize);
        var height = Mathf.CeilToInt(camera.pixelHeight / (float)tileSize);
        var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.RGB111110Float)
        {
            dimension = TextureDimension.Tex3D,
            enableRandomWrite = true,
            volumeDepth = depthSlices,
        };

        var computeShader = Resources.Load<ComputeShader>("Lighting/VolumetricLighting");

        textureCache.GetTexture(camera, desc, out var current, out var previous, FrameCount);

        using var scope = context.ScopedCommandBuffer("Volumetric Lighting", true);
        scope.Command.GetTemporaryRT(id, desc);
        scope.Command.SetGlobalFloat("_VolumeWidth", width);
        scope.Command.SetGlobalFloat("_VolumeHeight", height);
        scope.Command.SetGlobalFloat("_VolumeSlices", depthSlices);
        scope.Command.SetGlobalFloat("_VolumeDepth", maxDepth);

        var blueNoise1D = Resources.Load<Texture2D>(noiseIds.GetString(FrameCount % 64));

        var projMatrix = camera.projectionMatrix;
        var jitterX = projMatrix[0, 2];
        var jitterY = projMatrix[1, 2];
        var viewToWorld = Matrix4x4.Rotate(camera.transform.rotation);
        var mat = Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera.Resolution(), new Vector2(jitterX, jitterY), camera.fieldOfView, camera.aspect, viewToWorld, false);

        scope.Command.SetComputeMatrixParam(computeShader, "_PixelCoordToViewDirWS", mat);

        // Lighting
        scope.Command.SetComputeBufferParam(computeShader, 0, "_LightClusterList", lightList);
        scope.Command.SetComputeBufferParam(computeShader, 0, "_LightData", lightData);
        scope.Command.SetComputeBufferParam(computeShader, 0, "_DirectionalLightData", directionalLightBuffer);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_MaxZ", maxZ);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_LightClusterIndices", lightCluster);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_MultipleScatter", multipleScatter);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_CloudShadow", cloudShadow);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_BlueNoise1D", blueNoise1D);
        scope.Command.SetComputeIntParam(computeShader, "_DirectionalLightCount", directionalLightBuffer.Count);
        scope.Command.SetComputeIntParam(computeShader, "_LightCount", lightData.Count);
        scope.Command.SetComputeIntParam(computeShader, "_TileSize", clusterTileSize);
        scope.Command.SetComputeFloatParam(computeShader, "_CascadeCount", cascadeCount);
        scope.Command.SetComputeIntParam(computeShader, "_VolumeTileSize", tileSize);
        scope.Command.SetComputeBufferParam(computeShader, 0, "_SpotlightShadowMatrices", spotlightShadowMatrices);

        scope.Command.SetComputeTextureParam(computeShader, 0, "_Exposure", exposure);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_History", previous);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_Result", current);

        scope.Command.DispatchNormalized(computeShader, 0, width, height, depthSlices);

        scope.Command.SetComputeTextureParam(computeShader, 1, "_BlueNoise1D", blueNoise1D);
        scope.Command.SetComputeTextureParam(computeShader, 1, "_MaxZ", maxZ);
        scope.Command.SetComputeTextureParam(computeShader, 1, "_Exposure", exposure);
        scope.Command.SetComputeTextureParam(computeShader, 1, "_Input", current);
        scope.Command.SetComputeTextureParam(computeShader, 1, "_Result", id);
        scope.Command.DispatchNormalized(computeShader, 1, width, height, 1);
    }

    public override void Cleanup()
    {
        textureCache.Dispose();
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(id);
    }
}
