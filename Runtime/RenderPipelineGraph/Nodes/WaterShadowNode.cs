using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Water/Water Shadow")]
public partial class WaterShadowNode : RenderPipelineNode
{
    private static readonly Vector4[] cullingPlanes = new Vector4[6];

    [SerializeField] private WaterProfile profile;
    [SerializeField] private float shadowRadius = 8192;
    [SerializeField, Pow2(4096)] private int shadowResolution = 512;

    [Input] private CullingResults cullingResults;
    [Output] private RenderTargetIdentifier waterShadow;

    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Water Shadow", true);

        GraphicsUtilities.SetupCameraProperties(scope.Command, FrameCount, camera, context, camera.Resolution(), out var viewProjectionMatrix);

        // Render
        var waterShadowId = Shader.PropertyToID("_WaterShadow");
        var shadowDescriptor = new RenderTextureDescriptor(shadowResolution, shadowResolution, RenderTextureFormat.Shadowmap, 16);
        scope.Command.GetTemporaryRT(waterShadowId, shadowDescriptor);

        for (var i = 0; i < cullingResults.visibleLights.Length; i++)
        {
            var visibleLight = cullingResults.visibleLights[i];
            if (visibleLight.lightType != LightType.Directional)
                continue;

            var size = new Vector3(shadowRadius * 2, profile.MaxWaterHeight * 2, shadowRadius * 2);
            var min = new Vector3(camera.transform.position.x - shadowRadius, -profile.MaxWaterHeight, camera.transform.position.z - shadowRadius);

            var localMatrix = Matrix4x4.Rotate(Quaternion.Inverse(visibleLight.light.transform.rotation));
            Vector3 localMin = Vector3.positiveInfinity, localMax = Vector3.negativeInfinity;

            for (var z = 0; z < 2; z++)
            {
                for (var y = 0; y < 2; y++)
                {
                    for (var x = 0; x < 2; x++)
                    {
                        var localPosition = localMatrix.MultiplyPoint(min + Vector3.Scale(size, new Vector3(x, y, z)));
                        localMin = Vector3.Min(localMin, localPosition);
                        localMax = Vector3.Max(localMax, localPosition);
                    }
                }
            }

            // Snap texels
            var localSize = localMax - localMin;
            var worldUnitsPerTexel = localSize.XY() / shadowResolution;
            localMin.x = Mathf.Floor(localMin.x / worldUnitsPerTexel.x) * worldUnitsPerTexel.x;
            localMin.y = Mathf.Floor(localMin.y / worldUnitsPerTexel.y) * worldUnitsPerTexel.y;
            localMax.x = Mathf.Floor(localMax.x / worldUnitsPerTexel.x) * worldUnitsPerTexel.x;
            localMax.y = Mathf.Floor(localMax.y / worldUnitsPerTexel.y) * worldUnitsPerTexel.y;
            localSize = localMax - localMin;

            var localCenter = (localMax + localMin) * 0.5f;
            var worldMatrix = Matrix4x4.Rotate(visibleLight.light.transform.rotation);
            var position = worldMatrix.MultiplyPoint(new Vector3(localCenter.x, localCenter.y, localMin.z)) - camera.transform.position;

            var lookMatrix = Matrix4x4.LookAt(position, position + visibleLight.light.transform.forward, visibleLight.light.transform.up);

            // Matrix that mirrors along Z axis, to match the camera space convention.
            var scaleMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1));
            // Final view matrix is inverse of the LookAt matrix, and then mirrored along Z.
            var viewMatrix = scaleMatrix * lookMatrix.inverse;

            var projection = Matrix4x4.Ortho(-localSize.x * 0.5f, localSize.x * 0.5f, -localSize.y * 0.5f, localSize.y * 0.5f, 0, localSize.z);
            //lhsProj.SetColumn(2, -lhsProj.GetColumn(2));

            scope.Command.SetGlobalMatrix("_WaterShadowMatrix", GL.GetGPUProjectionMatrix(projection, true) * viewMatrix);
            scope.Command.SetGlobalFloat("_WaterShadowNear", 0f);
            scope.Command.SetGlobalFloat("_WaterShadowFar", localSize.z);
            //scope.Command.SetGlobalDepthBias(constantBias, slopeBias);

            scope.Command.SetRenderTarget(waterShadowId);
            scope.Command.ClearRenderTarget(true, false, new Color());

            GeometryUtilities.CalculateFrustumPlanes(projection * viewMatrix, cullingPlanes);

            foreach (var waterRenderer in WaterRenderer.WaterRenderers)
            {
                waterRenderer.Cull(scope.Command, camera.transform.position, cullingPlanes, 6);
                waterRenderer.Render(scope.Command, "WaterShadow", camera.transform.position);
            }

            scope.Command.SetGlobalMatrix("_WaterShadowMatrix", (projection * viewMatrix).ConvertToAtlasMatrix());

            // Only render 1 light
            break;
        }

        waterShadow = waterShadowId;
        scope.Command.SetRenderTarget(BuiltinRenderTextureType.None);
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var command = context.ScopedCommandBuffer();
        command.Command.ReleaseTemporaryRT(Shader.PropertyToID("_WaterShadow"));
    }
}