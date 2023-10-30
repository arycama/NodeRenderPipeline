using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

public partial class ReflectionProbeShadowsNode : RenderPipelineNode
{
    [SerializeField] private DepthBits depth = DepthBits.Depth16;
    [SerializeField, Tooltip("Higher values reduce self-shadowing, but can result in peter-panning")] private float bias = 1f;

    [SerializeField] private RenderPipelineSubGraph shadowsSubGraph;

    [Input] private RenderTargetIdentifier shadowmap;
    [Input] private CullingResults cullingResults;
    [Input] private Vector3 boundsCenter;
    [Input] private Vector3 boundsExtents;
    [Input] private GpuInstanceBuffers gpuInstanceBuffers;

    [Output] private Matrix4x4 shadowMatrix;

    public override void Initialize()
    {

        if (shadowsSubGraph != null)
            shadowsSubGraph.Initialize();
    }

    public override void Cleanup()
    {
        if (shadowsSubGraph != null)
            shadowsSubGraph.Cleanup();
    }

    public override void NodeChanged()
    {
        Cleanup();
        Initialize();

        if (shadowsSubGraph != null)
        {
            shadowsSubGraph.Cleanup();
            shadowsSubGraph.Initialize();
        }
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        VisibleLight light = default;

        var lightIndex = -1;
        for(var i = 0; i < cullingResults.visibleLights.Length; i++)
        {
            var currentLight = cullingResults.visibleLights[i];
            if (currentLight.lightType != LightType.Directional)
                continue;

            light = currentLight;
            lightIndex = i;
            break;
        }

        if (lightIndex == -1)
            return;

        var viewMatrix = light.localToWorldMatrix.inverse;
        var boundsMin = boundsCenter - boundsExtents;
        var boundsSize = 2.0f * boundsExtents;

        // Transform the 8 corners into light space and calculate the min/max
        Vector3 minValue = Vector3.positiveInfinity, maxValue = Vector3.negativeInfinity;
        for(var z = 0; z < 2; z++)
        {
            for(var y=  0; y < 2; y++)
            {
                for(var x = 0; x < 2; x++)
                {
                    var worldPoint = boundsMin + Vector3.Scale(boundsSize, new(x, y, z));
                    var localPoint = viewMatrix.MultiplyPoint3x4(worldPoint);
                    minValue = Vector3.Min(minValue, localPoint);
                    maxValue = Vector3.Max(maxValue, localPoint);
                }
            }
        }

        var projectionMatrix = Matrix4x4.Ortho(minValue.x, maxValue.x, minValue.y, maxValue.y, minValue.z, maxValue.z);
        projectionMatrix.SetColumn(2, -projectionMatrix.GetColumn(2));

        var shadowSplitData = new ShadowSplitData()
        {
            cullingPlaneCount = 5,
            shadowCascadeBlendCullingFactor = 1
        };

        // First get the planes from the view projection matrix
        var lightFrustumPlanes = GeometryUtilities.CalculateFrustumPlanes(projectionMatrix * viewMatrix);
        for (var j = 0; j < lightFrustumPlanes.Count; j++)
        {
            if (j < 4)
            {
                shadowSplitData.SetCullingPlane(j, lightFrustumPlanes.GetCullingPlane(j));
            }
            else if (j == 5)
            {
                shadowSplitData.SetCullingPlane(4, lightFrustumPlanes.GetCullingPlane(j));
            }
        }

        var hasShadows = cullingResults.GetShadowCasterBounds(lightIndex, out var shadowBounds);

        var near = 0f;
        var far = maxValue.z - minValue.z;

        using var scope = context.ScopedCommandBuffer("Reflection Probe Shadows", true);

        scope.Command.SetGlobalFloat("_ZClip", 0);
        scope.Command.SetGlobalDepthBias(bias, light.light.shadowBias);
        scope.Command.BeginSample("Reflection Probe Shadows");
        scope.Command.SetRenderTarget(shadowmap);

        context.ExecuteCommandBuffer(scope.Command);
        scope.Command.Clear();

        var viewMatrixRWS = Matrix4x4Extensions.WorldToLocal(light.localToWorldMatrix.GetPosition() - camera.transform.position, light.localToWorldMatrix.rotation);

        if (shadowsSubGraph != null)
        {
            // Extract culling planes
            var cullingPlanes = new CullingPlanes { Count = 5 };
            for (var i = 0; i < shadowSplitData.cullingPlaneCount; i++)
            {
                // Translate planes from world space to camera-relative space
                var plane = shadowSplitData.GetCullingPlane(i);
                plane.distance += Vector3.Dot(plane.normal, camera.transform.position);
                cullingPlanes.SetCullingPlane(i, plane);
            }

            shadowsSubGraph.AddRelayInput("CullingResults", cullingResults);
            shadowsSubGraph.AddRelayInput("VisibleLightIndex", lightIndex);
            shadowsSubGraph.AddRelayInput("ShadowSplitData", shadowSplitData);
            shadowsSubGraph.AddRelayInput("RenderShadowCasters", hasShadows);
            shadowsSubGraph.AddRelayInput("CullingPlanes", cullingPlanes);
            shadowsSubGraph.AddRelayInput("CullingPlanesCount", cullingPlanes.Count);
            shadowsSubGraph.AddRelayInput("GpuInstanceBuffers", gpuInstanceBuffers);

            // Matrices
            shadowsSubGraph.AddRelayInput("ViewMatrix", viewMatrixRWS);
            shadowsSubGraph.AddRelayInput("ProjMatrix", GL.GetGPUProjectionMatrix(projectionMatrix, true));
            shadowsSubGraph.AddRelayInput("ViewProjMatrix", GL.GetGPUProjectionMatrix(projectionMatrix, true) * viewMatrixRWS);
            shadowsSubGraph.AddRelayInput("InvViewMatrix", viewMatrixRWS.inverse);

            shadowsSubGraph.AddRelayInput("Output", shadowmap);
            shadowsSubGraph.Render(context, camera, FrameCount);
        }

        scope.Command.SetGlobalFloat("_ZClip", 1);
        scope.Command.SetGlobalDepthBias(0, 0);
        scope.Command.EndSample("Reflection Probe Shadows");

        shadowMatrix = (projectionMatrix * viewMatrixRWS).ConvertToAtlasMatrix();
    }
}
