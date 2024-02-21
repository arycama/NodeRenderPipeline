using NodeGraph;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Deferred Lighting")]
public partial class DeferredLightingNode : RenderPipelineNode
{
    [Input] private GraphicsBuffer ambient;

    [Input] private SmartComputeBuffer<DirectionalLightData> directionalLightBuffer;
    [Input] private SmartComputeBuffer<Matrix4x4> spotlightShadowMatrices;

    [Input] private ComputeBuffer lightList;
    [Input] private SmartComputeBuffer<LightData> lightData;
    [Input] private float clusterScale;
    [Input] private float clusterBias;
    [Input] private int clusterTileSize;

    [Input] private RenderTargetIdentifier gBuffer0;
    [Input] private RenderTargetIdentifier gBuffer1;
    [Input] private RenderTargetIdentifier gBuffer2;
    [Input] private RenderTargetIdentifier gBuffer3;
    [Input] private RenderTargetIdentifier gBuffer4;

    [Input] private RenderTargetIdentifier screenSpaceReflections;
    [Input] private RenderTargetIdentifier lightCluster;
    [Input] private RenderTargetIdentifier exposure;
    [Input] private RenderTargetIdentifier skyReflection;
    [Input] private RenderTargetIdentifier atmosphereTransmittance;

    [Input] private SmartComputeBuffer<ReflectionProbeData> reflectionProbeBuffer;
    [Input] private RenderTargetIdentifier reflectionProbeArray;

    [Input, Output] private NodeConnection connection;

    private Material material;

    public override void Initialize()
    {
        material = new Material(Shader.Find("Hidden/Deferred Lighting")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public override void Cleanup()
    {
        DestroyImmediate(material);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Deferred Lighting", false);

        //GraphicsUtilities.SetupCameraProperties(scope.Command, FrameCount, camera, context, camera.Resolution());

        var propertyBlock = GenericPool<MaterialPropertyBlock>.Get();
        propertyBlock.Clear();

        scope.Command.SetGlobalTexture("_GBuffer0", gBuffer0);
        scope.Command.SetGlobalTexture("_GBuffer1", gBuffer1);
        scope.Command.SetGlobalTexture("_GBuffer2", gBuffer2);
        scope.Command.SetGlobalTexture("_GBuffer3", gBuffer3);
        scope.Command.SetGlobalTexture("_GBuffer4", gBuffer4);

        scope.Command.SetGlobalTexture("_Exposure", exposure);
        propertyBlock.SetConstantBuffer("AmbientSh", ambient, 0, ambient.count * ambient.stride);
        scope.Command.SetGlobalTexture("_AtmosphereTransmittance", atmosphereTransmittance);
        scope.Command.SetGlobalTexture("_SkyReflection", skyReflection);
        scope.Command.SetGlobalTexture("_ReflectionBuffer", screenSpaceReflections);

        // Lighting
        propertyBlock.SetBuffer("_LightClusterList", lightList);
        propertyBlock.SetBuffer("_LightData", lightData);
        propertyBlock.SetBuffer("_DirectionalLightData", directionalLightBuffer);
        scope.Command.SetGlobalTexture("_LightClusterIndices", lightCluster);
        propertyBlock.SetBuffer("_SpotlightShadowMatrices", spotlightShadowMatrices);
        propertyBlock.SetInt("_DirectionalLightCount", directionalLightBuffer.Count);
        propertyBlock.SetInt("_LightCount", lightData.Count);

        propertyBlock.SetBuffer("_ReflectionProbeData", reflectionProbeBuffer);
        scope.Command.SetGlobalTexture("_ReflectionProbes", reflectionProbeArray);
        propertyBlock.SetInt("_ReflectionProbeCount", reflectionProbeBuffer.Count);

        propertyBlock.SetInt("_TileSize", clusterTileSize);
        propertyBlock.SetFloat("_ClusterScale", clusterScale);
        propertyBlock.SetFloat("_ClusterBias", clusterBias);

        scope.Command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, propertyBlock);
        GenericPool<MaterialPropertyBlock>.Release(propertyBlock);
    }
}