using System.Collections.Generic;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Camera/Setup Camera Properties")]
public partial class SetupCameraPropertiesNode : RenderPipelineNode
{
    [Input, SerializeField, Range(0f, 1f)] private float jitterSpread = 1f;
    [Input, SerializeField, Pow2(64)] private int temporalSamples = 8;
    [SerializeField] private bool jitterDebug = false;
    [SerializeField] private Vector2 jitterOverride = Vector2.zero;

    [Output] private int width;
    [Output] private int height;
    [Output] private Vector2 jitter;
    [Output] private CullingPlanes cullingPlanes;
    [Output] private Matrix4x4 viewProjectionMatrix;
    [Output] private Vector3 cameraPosition;

    [Input, Output] private NodeConnection connection;

    private readonly Dictionary<Camera, (Vector3, Quaternion)> previousCameraData = new();

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        width = camera.pixelWidth;
        height = camera.pixelHeight;

        // Need to set this or it won't provide motion vectors
        camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;

        // Setup Matrices and Jitter
        var jitterX = HaltonSequence.Get(FrameCount % temporalSamples, 2);
        var jitterY = HaltonSequence.Get(FrameCount % temporalSamples, 3);

        var jitterOffsetX = jitterX < 0.5f ? 1 : 0;
        var jitterOffsetY = jitterY < 0.5f ? 1 : 0;

        Vector2 jitter;
        jitter.x = (jitterX - 0.5f) / camera.pixelWidth;
        jitter.y = (jitterY - 0.5f) / camera.pixelHeight;
        jitter *= jitterSpread;

        if (jitterDebug)
            jitter = new Vector2(jitterOverride.x / camera.pixelWidth, jitterOverride.y / camera.pixelHeight);

        camera.ResetProjectionMatrix();
        var projection = camera.projectionMatrix;
        projection[0, 2] = 2f * jitter.x;
        projection[1, 2] = 2f * jitter.y;
        camera.projectionMatrix = projection;

        // Calculate non-jittered GPU projection matrix
        var cotangent = 1f / Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);

        var nonJitteredProjectionMatrix = new Matrix4x4();
        nonJitteredProjectionMatrix[0, 0] = cotangent / camera.aspect;
        nonJitteredProjectionMatrix[1, 1] = cotangent;
        nonJitteredProjectionMatrix[2, 2] = -camera.nearClipPlane / (camera.farClipPlane - camera.nearClipPlane);
        nonJitteredProjectionMatrix[2, 3] = camera.farClipPlane * camera.nearClipPlane / (camera.farClipPlane - camera.nearClipPlane);
        nonJitteredProjectionMatrix[3, 2] = 1f;

        // Get cameras position/rotation from last frame
        if (!previousCameraData.TryGetValue(camera, out var previousData))
            previousData = (camera.transform.position, camera.transform.rotation);

        // Save data for next frame
        previousCameraData[camera] = (camera.transform.position, camera.transform.rotation);

        var nonJitteredViewProjectionMatrix = nonJitteredProjectionMatrix * Matrix4x4.Rotate(Quaternion.Inverse(camera.transform.rotation));

        var cameraDelta = camera.transform.position - previousData.Item1;
        var prevViewMatrix = Matrix4x4.Rotate(Quaternion.Inverse(previousData.Item2)) * Matrix4x4.Translate(cameraDelta);
        var prevViewProjMatrix = nonJitteredProjectionMatrix * prevViewMatrix;

        using var scope = context.ScopedCommandBuffer();
        scope.Command.SetGlobalVector("_PreviousCameraPosition", previousData.Item1);
        scope.Command.SetGlobalVector("_PreviousCameraDelta", cameraDelta);
        scope.Command.SetGlobalMatrix("_PrevViewMatrix", prevViewMatrix);
        scope.Command.SetGlobalMatrix("_PrevViewProjMatrix", prevViewProjMatrix);
        scope.Command.SetGlobalMatrix("_PrevInvViewProjMatrix", prevViewProjMatrix.inverse);
        scope.Command.SetGlobalMatrix("_PrevInvProjMatrix", nonJitteredProjectionMatrix.inverse);
        scope.Command.SetGlobalMatrix("_NonJitteredViewProjMatrix", nonJitteredViewProjectionMatrix);
        scope.Command.SetGlobalVector("_Jitter", jitter);
        scope.Command.SetGlobalVector("_JitterRaw", new Vector2(jitterX, jitterY));
        scope.Command.SetGlobalInt("_JitterOffsetX", jitterOffsetX);
        scope.Command.SetGlobalInt("_JitterOffsetY", jitterOffsetY);
        GraphicsUtilities.SetupCameraProperties(scope.Command, FrameCount, camera, context, camera.Resolution(), out cullingPlanes, out viewProjectionMatrix);
        cameraPosition = camera.transform.position;

#if UNITY_EDITOR
        //ScriptableRenderContext.EmitGeometryForCamera(camera);

        // Emit scene view UI
        if (camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
#endif
    }
}
