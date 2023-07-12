using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Deferred Reflection Probe Lighting")]
public partial class DeferredReflectionProbeLightingNode : RenderPipelineNode
{
    [Input] private RenderTargetIdentifier depth;
    [Input] private RenderTargetIdentifier gbuffer0;
    [Input] private RenderTargetIdentifier gbuffer1;
    [Input] private RenderTargetIdentifier gbuffer2;

    [Input] private float previousExposure;
    [Input] private GraphicsBuffer ambient;
    [Input] private RenderTargetIdentifier atmosphereTransmittance;
    [Input] private RenderTargetIdentifier skyReflection;
    [Input] private int resolution;

    [Header("Camera")]
    [Input] private Vector3 cameraPosition;
    [Input] private Matrix4x4 viewProjectionMatrix;

    [Input] private ComputeBuffer lightList;
    [Input] private SmartComputeBuffer<LightData> lightDataBuffer;
    [Input] private SmartComputeBuffer<DirectionalLightData> directionalLightBuffer;
    [Input] private RenderTargetIdentifier lightClusterId;
    [Input] private ComputeBuffer reflectionProbeDataBuffer;
    [Input] private RenderTargetIdentifier reflectionProbeArray;
    [Input] private int reflectionProbeDataBufferCount;

    [Input] private float clusterScale;
    [Input] private float clusterBias;
    [Input] private int tileSize;

    // Lighting
    [Input] private int cascadeCount;
    [Input] private SmartComputeBuffer<Matrix3x4> directionalShadowMatrices;
    [Input] private RenderTargetIdentifier directionalShadows;

    [Input, Output] private RenderTargetIdentifier result;

    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var deferredComputeShader = Resources.Load<ComputeShader>("Core/Deferred");

        using var scope = context.ScopedCommandBuffer("Deferred Reflection Probe Lighting");

        GraphicsUtilities.SetupCameraProperties(scope.Command, 0, camera, context, Vector2Int.one * resolution, out var viewProjectionMatrix, true);

        scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "Depth", depth, 0, RenderTextureSubElement.Depth);
        scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_GBuffer0", gbuffer0, 0, RenderTextureSubElement.Color);
        scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_GBuffer1", gbuffer1, 0, RenderTextureSubElement.Color);
        scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_GBuffer2", gbuffer2, 0, RenderTextureSubElement.Color);
        scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "Result", result);
        scope.Command.SetComputeFloatParam(deferredComputeShader, "_ExposureValue", previousExposure);
        scope.Command.SetComputeFloatParam(deferredComputeShader, "_ExposureValueRcp", 1f / previousExposure);
        scope.Command.SetComputeConstantBufferParam(deferredComputeShader, "AmbientSh", ambient, 0, ambient.count * ambient.stride);
        scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_AtmosphereTransmittance", atmosphereTransmittance);
        scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_SkyReflection", skyReflection);

        scope.Command.SetComputeIntParam(deferredComputeShader, "_Resolution", resolution);

        //scope.Command.SetComputeBufferParam(deferredComputeShader, 0, "_LightClusterList", lightList);
       // scope.Command.SetComputeBufferParam(deferredComputeShader, 0, "_LightData", lightDataBuffer);
        scope.Command.SetComputeBufferParam(deferredComputeShader, 0, "_DirectionalLightData", directionalLightBuffer);
        //scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_LightClusterIndices", lightClusterId);
        scope.Command.SetComputeIntParam(deferredComputeShader, "_DirectionalLightCount", directionalLightBuffer.Count);
       // scope.Command.SetComputeIntParam(deferredComputeShader, "_LightCount", lightDataBuffer.Count);

        scope.Command.SetComputeBufferParam(deferredComputeShader, 0, "_DirectionalShadowMatrices", directionalShadowMatrices);
        scope.Command.SetComputeFloatParam(deferredComputeShader, "_CascadeCount", cascadeCount);

        scope.Command.SetComputeBufferParam(deferredComputeShader, 0, "_ReflectionProbeData", reflectionProbeDataBuffer);
        scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_ReflectionProbes", reflectionProbeArray);
        scope.Command.SetComputeIntParam(deferredComputeShader, "_ReflectionProbeCount", reflectionProbeDataBufferCount);
        scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_DirectionalShadows", directionalShadows);

        scope.Command.SetComputeMatrixParam(deferredComputeShader, "_CameraViewProjectionMatrix", viewProjectionMatrix);
        scope.Command.SetComputeVectorParam(deferredComputeShader, "_OriginalCameraPosition", cameraPosition);
        scope.Command.SetComputeVectorParam(deferredComputeShader, "_ReflectionCameraPosition", camera.transform.position);

        // scope.Command.SetComputeFloatParam(deferredComputeShader, "_ClusterScale", clusterScale);
        //scope.Command.SetComputeFloatParam(deferredComputeShader, "_ClusterBias", clusterBias);
        // scope.Command.SetComputeIntParam(deferredComputeShader, "_TileSize", tileSize);

        var viewToWorld = Matrix4x4.Rotate(camera.transform.rotation);
        var viewDirMatrix = Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(Vector2Int.one * resolution, Vector2.zero, 90, 1, viewToWorld, true);
        scope.Command.SetComputeMatrixParam(deferredComputeShader, "_PixelCoordToViewDirWS", viewDirMatrix);

        // Dispatch
        using (var keywordScope = scope.Command.KeywordScope("REFLECTION_PROBE_RENDERING"))
            scope.Command.DispatchNormalized(deferredComputeShader, 0, resolution, resolution, 1);
    }
}