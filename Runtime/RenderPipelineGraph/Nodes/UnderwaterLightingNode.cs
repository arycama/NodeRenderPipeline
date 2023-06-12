using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Underwater Lighting")]
public partial class UnderwaterLightingNode : RenderPipelineNode
{
    [SerializeField] private Material material;

    [Input] private ComputeBuffer ambient;

    [Input] private SmartComputeBuffer<DirectionalLightData> directionalLightBuffer;
    [Input] private SmartComputeBuffer<Matrix4x4> spotlightShadowMatrices;

    [Input] private ComputeBuffer lightList;
    [Input] private SmartComputeBuffer<LightData> lightData;
    [Input] private float clusterScale;
    [Input] private float clusterBias;
    [Input] private int clusterTileSize;

    [Input] private RenderTargetIdentifier depth;
    [Input] private RenderTargetIdentifier underwaterDepth;
    [Input] private RenderTargetIdentifier waterShadow;

    [Input] private RenderTargetIdentifier gBuffer0;
    [Input] private RenderTargetIdentifier gBuffer1;
    [Input] private RenderTargetIdentifier gBuffer2;
    [Input] private RenderTargetIdentifier gBuffer3;
    [Input] private RenderTargetIdentifier gBuffer4;

    // [Input] private RenderTargetIdentifier screenSpaceReflections;
    [Input] private RenderTargetIdentifier lightCluster;
    [Input] private RenderTargetIdentifier exposure;
    [Input] private RenderTargetIdentifier skyReflection;
    [Input] private RenderTargetIdentifier atmosphereTransmittance;

    [Input] private SmartComputeBuffer<ReflectionProbeData> reflectionProbeBuffer;
    [Input] private RenderTargetIdentifier reflectionProbeArray;

    [Output] private RenderTargetIdentifier result;
    [Input, Output] private NodeConnection connection;

    private int underwaterResultId;

    private Material renderMaterial;

    public override void Initialize()
    {
        underwaterResultId = GetShaderPropertyId("_UnderwaterResult");
        renderMaterial = CoreUtils.CreateEngineMaterial("Hidden/Underwater Lighting");
        result = underwaterResultId;
    }

    public override void Cleanup()
    {
        DestroyImmediate(renderMaterial);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Underwater Lighting", true);

        // Render underwater, get a temporary texture to save result
        var underwaterResultDesc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.RGB111110Float) { enableRandomWrite = true };

        GraphicsUtilities.SetupCameraProperties(scope.Command, FrameCount, camera, context, camera.Resolution());

        scope.Command.GetTemporaryRT(underwaterResultId, underwaterResultDesc);

        scope.Command.SetGlobalTexture("_UnderwaterDepth", underwaterDepth);
        scope.Command.SetGlobalTexture("_Depth", depth);
        scope.Command.SetGlobalTexture("_GBuffer0", gBuffer0);
        scope.Command.SetGlobalTexture("_GBuffer1", gBuffer1);
        scope.Command.SetGlobalTexture("_GBuffer2", gBuffer2);
        scope.Command.SetGlobalTexture("_GBuffer3", gBuffer3);
        scope.Command.SetGlobalTexture("_GBuffer4", gBuffer4);

        scope.Command.SetGlobalTexture("_Exposure", exposure);
        scope.Command.SetGlobalTexture("_AtmosphereTransmittance", atmosphereTransmittance);
        scope.Command.SetGlobalTexture("_SkyReflection", skyReflection);
        scope.Command.SetGlobalTexture("_WaterShadows", waterShadow);

        scope.Command.SetGlobalTexture("_LightClusterIndices", lightCluster);
        scope.Command.SetGlobalTexture("_ReflectionProbes", reflectionProbeArray);

        var propertyBlock = GenericPool<MaterialPropertyBlock>.Get();
        propertyBlock.Clear();

        propertyBlock.SetBuffer("_AmbientSh", ambient);
        propertyBlock.SetBuffer("_LightClusterList", lightList);
        propertyBlock.SetBuffer("_LightData", lightData);
        propertyBlock.SetBuffer("_DirectionalLightData", directionalLightBuffer);
        propertyBlock.SetBuffer("_SpotlightShadowMatrices", spotlightShadowMatrices);
        propertyBlock.SetInt("_DirectionalLightCount", directionalLightBuffer.Count);
        propertyBlock.SetInt("_LightCount", lightData.Count);

        propertyBlock.SetBuffer("_ReflectionProbeData", reflectionProbeBuffer);
        propertyBlock.SetInt("_ReflectionProbeCount", reflectionProbeBuffer.Count);

        propertyBlock.SetInt("_TileSize", clusterTileSize);
        propertyBlock.SetFloat("_ClusterScale", clusterScale);
        propertyBlock.SetFloat("_ClusterBias", clusterBias);

        propertyBlock.SetVector("_WaterExtinction", material.GetColor("_Extinction"));

        scope.Command.SetRenderTarget(underwaterResultId, depth);
        scope.Command.DrawProcedural(Matrix4x4.identity, renderMaterial, 0, MeshTopology.Triangles, 3, 1, propertyBlock);

        GenericPool<MaterialPropertyBlock>.Release(propertyBlock);
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(underwaterResultId);
    }
}