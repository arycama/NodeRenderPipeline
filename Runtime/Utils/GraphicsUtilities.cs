using UnityEngine;
using UnityEngine.Rendering;

public static class GraphicsUtilities
{
    private static readonly Vector4[] cullingPlanes = new Vector4[6];

    public static void SafeDestroy(ref ComputeBuffer buffer)
    {
        if (buffer != null)
        {
            buffer.Release();
            buffer = null;
        }
    }

    public static void SafeDestroy(ref GraphicsBuffer buffer)
    {
        if (buffer != null)
        {
            buffer.Release();
            buffer = null;
        }
    }

    public static void SafeDestroy<T>(ref T buffer) where T : Object
    {
        if (buffer != null)
        {
            Object.DestroyImmediate(buffer);
            buffer = null;
        }
    }

    public static void SafeResize(ref ComputeBuffer computeBuffer, int size = 1, int stride = sizeof(int), ComputeBufferType type = ComputeBufferType.Default)
    {
        if (computeBuffer == null || computeBuffer.count != size)
        {
            if (computeBuffer != null)
            {
                computeBuffer.Release();
                computeBuffer = null;
            }

            if (size > 0)
                computeBuffer = new ComputeBuffer(size, stride, type);
        }
    }

    public static void SafeExpand(ref ComputeBuffer computeBuffer, int size = 1, int stride = sizeof(int), ComputeBufferType type = ComputeBufferType.Default)
    {
        size = Mathf.Max(size, 1);

        if (computeBuffer == null || computeBuffer.count < size)
        {
            if (computeBuffer != null)
                computeBuffer.Release();

            computeBuffer = new ComputeBuffer(size, stride, type);
        }
    }

    /// <summary>
    /// Calculates ScaleOffset to Remap a CS thread to UV coordinate that stretches from 0:1. (No half-texel offset)
    /// </summary>
    public static Vector2 ThreadIdScaleOffset01(int width, int height)
    {
        return new Vector2(1f / (width - 1), 1f / (height - 1));
    }

    /// <summary>
    /// Calculates ScaleOffset to Remap a CS thread to UV coordinate that stretches from 0:1. (No half-texel offset)
    /// </summary>
    public static Vector3 ThreadIdScaleOffset01(int width, int height, int depth)
    {
        return new Vector3(1f / (width - 1), 1f / (height - 1), 1f / (depth - 1));
    }

    /// <summary>
    /// Calculates scaleOffset for remapping ComupteShader thread IDs to uv coordinates centered on pixels
    /// </summary>
    public static Vector4 ThreadIdScaleOffset(int width, int height)
    {
        return new Vector4((float)(1.0 / width), (float)(1.0 / height), (float)(0.5 / width), (float)(0.5 / height));
    }

    /// <summary>
    /// Calculates a scale and offset for remapping a UV from a 0-1 range to a halfTexel to (1-halfTexel) range
    /// </summary>
    public static Vector2 HalfTexelRemap(float width)
    {
        var invWidth = 1f / width;
        return new Vector2(1f - invWidth, 0.5f * invWidth);
    }

    /// <summary>
    /// Calculates a scale and offset for remapping a UV from a 0-1 range to a halfTexel to (1-halfTexel) range.
    /// This is for a 3D texture, but we need 6 values total, so output 2*vector3s.
    /// </summary>
    public static Vector3 HalfTexelRemap(int width, int height, int depth, out Vector3 offset)
    {
        offset = new Vector3(1f / width, 1f / height, 1f / depth) * 0.5f;
        return new Vector3(1f - 1f / width, 1f - 1f / height, 1f - 1f / depth);
    }

    /// <summary>
    /// Calculates a scale and offset for remapping a UV from a 0-1 range to a halfTexel to (1-halfTexel) range
    /// </summary>
    public static Vector4 HalfTexelRemap(float width, float height)
    {
        var invWidth = 1f / width;
        var invHeight = 1f / height;
        return new Vector4(1f - invWidth, 1f - invHeight, 0.5f * invWidth, 0.5f * invHeight);
    }

    public static Vector4 RemapHalfTexelTo01(float width, float height)
    {
        return new Vector4(width / (width - 1f), height / (height - 1f), -0.5f / (width - 1f), -0.5f / (height - 1f));
    }

    public static Vector4 HalfTexelRemap(Vector2 position, Vector2 size, Vector2 resolution)
    {
        Vector4 result;
        result.x = (resolution.x - 1f) / (size.x * resolution.x);
        result.y = (resolution.y - 1f) / (size.x * resolution.y);
        result.z = (0.5f * size.x + position.x - position.x * resolution.x) / (size.x * resolution.x);
        result.w = (0.5f * size.y + position.y - position.y * resolution.y) / (size.y * resolution.y);
        return result;
    }

    public static void GenerateMaxHiZ(CommandBuffer command, Vector2Int resolution, RenderTargetIdentifier source, RenderTargetIdentifier dest, float scale = 1f, float offset = 0f)
    {
        var computeShader = Resources.Load<ComputeShader>("Utility/GenerateHiZ");

        command.EnableShaderKeyword("MAX_ONLY");
        command.SetComputeTextureParam(computeShader, 1, "Input", source);
        command.SetComputeFloatParam(computeShader, "_InputScale", scale);
        command.SetComputeFloatParam(computeShader, "_InputOffset", offset);

        command.SetComputeTextureParam(computeShader, 1, "_MaxResult", dest);

        var width = resolution.x;
        var height = resolution.y;

        command.DispatchNormalized(computeShader, 1, width, height, 1);

        var mipCount = Texture2DExtensions.MipCount(width, height);
        for (var i = 1; i < mipCount; i++)
        {
            // For some resolutions, we can end up with a 1x2, followed by a 1x1.
            var xSize = Mathf.Max(1, width >> i);
            var ySize = Mathf.Max(1, height >> i);

            var prevWidth = Mathf.Max(1, width >> (i - 1));
            var prevHeight = Mathf.Max(1, height >> (i - 1));

            command.SetComputeIntParam(computeShader, "_Width", prevWidth);
            command.SetComputeIntParam(computeShader, "_Height", prevHeight);

            command.SetComputeTextureParam(computeShader, 5, "_MaxSource", dest, i - 1);
            command.SetComputeTextureParam(computeShader, 5, "_MaxResult", dest, i);

            command.DispatchNormalized(computeShader, 5, xSize, ySize, 1);
        }

        command.DisableShaderKeyword("MAX_ONLY");
    }

    public static void GenerateMinMaxHiZ(CommandBuffer command, int width, int height, RenderTargetIdentifier source, RenderTargetIdentifier minZTexture, RenderTargetIdentifier maxZTexture, bool combined, float scale = 1f, float offset = 0f)
    {
        var computeShader = Resources.Load<ComputeShader>("Utility/GenerateHiZ");

        var firstIndex = combined ? 3 : 2;
        command.SetComputeTextureParam(computeShader, firstIndex, "Input", source);
        command.SetComputeFloatParam(computeShader, "_InputScale", scale);
        command.SetComputeFloatParam(computeShader, "_InputOffset", offset);

        if (combined)
        {
            command.EnableShaderKeyword("MIN_MAX_COMBINED");
            command.SetComputeTextureParam(computeShader, firstIndex, "_Result", minZTexture);
            //command.ConvertTexture(source, minZTexture);
        }
        else
        {
            command.EnableShaderKeyword("MIN_MAX_SEPERATE");
            command.SetComputeTextureParam(computeShader, firstIndex, "_MinResult", minZTexture);
            command.SetComputeTextureParam(computeShader, firstIndex, "_MaxResult", maxZTexture);
            //command.ConvertTexture(source, minZTexture);
            //command.ConvertTexture(source, maxZTexture);
        }

        command.DispatchNormalized(computeShader, firstIndex, width, height, 1);

        var secondIndex = combined ? 7 : 6;
        var mipCount = Texture2DExtensions.MipCount(width, height);
        for (var i = 1; i < mipCount; i++)
        {
            // For some resolutions, we can end up with a 1x2, followed by a 1x1.
            var xSize = Mathf.Max(1, width >> i);
            var ySize = Mathf.Max(1, height >> i);

            var prevWidth = Mathf.Max(1, width >> (i - 1));
            var prevHeight = Mathf.Max(1, height >> (i - 1));

            command.SetComputeIntParam(computeShader, "_Width", prevWidth);
            command.SetComputeIntParam(computeShader, "_Height", prevHeight);

            if (combined)
            {
                command.SetComputeTextureParam(computeShader, secondIndex, "_Source", minZTexture, i - 1);
                command.SetComputeTextureParam(computeShader, secondIndex, "_Result", minZTexture, i);
            }
            else
            {
                command.SetComputeTextureParam(computeShader, secondIndex, "_MinSource", minZTexture, i - 1);
                command.SetComputeTextureParam(computeShader, secondIndex, "_MaxSource", maxZTexture, i - 1);
                command.SetComputeTextureParam(computeShader, secondIndex, "_MinResult", minZTexture, i);
                command.SetComputeTextureParam(computeShader, secondIndex, "_MaxResult", maxZTexture, i);
            }

            command.DispatchNormalized(computeShader, secondIndex, xSize, ySize, 1);
        }

        if (combined)
            command.DisableShaderKeyword("MIN_MAX_COMBINED");
        else
            command.DisableShaderKeyword("MIN_MAX_SEPERATE");
    }

    public static Vector4 ZBufferParams(double near, double far, bool reverseZ)
    {
        var n = near;
        var f = far;
        var reversedZ = reverseZ;

        var x = reversedZ ? -1.0 + f / n : 1.0 - f / n;
        var y = reversedZ ? 1.0 : f / n;
        var z = x / f;
        var w = reversedZ ? 1.0 / f : y / f;
        return new Vector4((float)x, (float)y, (float)z, (float)w);
    }

    public static void SetupCameraProperties(CommandBuffer command, int frameCount, Camera camera, ScriptableRenderContext context, Vector2Int resolution, Vector4[] cullingPlanes, out Matrix4x4 viewProjectionMatrix, bool flip = false)
    {
        context.SetupCameraProperties(camera);

        var viewToWorld = Matrix4x4.Rotate(camera.transform.rotation);
        var worldToView = Matrix4x4.Rotate(Quaternion.Inverse(camera.transform.rotation));

        // Unity doesn't provide previousObjectMatrices in edit mode, so we avoid relying on them if this is 0
        command.SetGlobalFloat("_InPlayMode", Application.isPlaying ? 1f : 0f);
        command.SetGlobalInt("_FrameIndex", frameCount);

        // New?
        var cotangent = 1f / Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);

        var jitterX = camera.projectionMatrix[0, 2];
        var jitterY = camera.projectionMatrix[1, 2];
        var near = camera.nearClipPlane;
        var far = camera.farClipPlane;

        var projectionMatrix = new Matrix4x4();
        projectionMatrix[0, 0] = cotangent / camera.aspect;
        projectionMatrix[0, 2] = jitterX;
        projectionMatrix[1, 1] = cotangent;
        projectionMatrix[1, 2] = jitterY;
        projectionMatrix[2, 2] = -(far + near) / (near - far);
        projectionMatrix[2, 3] = 2f * near * far / (near - far);
        projectionMatrix[3, 2] = 1f;

        var gpuProjectionMatrix = new Matrix4x4();
        gpuProjectionMatrix[0, 0] = cotangent / camera.aspect;
        gpuProjectionMatrix[0, 2] = jitterX;
        gpuProjectionMatrix[1, 1] = flip ? -cotangent : cotangent;
        gpuProjectionMatrix[1, 2] = jitterY;
        gpuProjectionMatrix[2, 2] = -near / (far - near);
        gpuProjectionMatrix[2, 3] = far * near / (far - near);
        gpuProjectionMatrix[3, 2] = 1f;

        GeometryUtilities.CalculateFrustumPlanes(projectionMatrix * worldToView, cullingPlanes);

        command.SetGlobalVectorArray("_CullingPlanes", cullingPlanes);

        command.SetGlobalMatrix("_ViewMatrix", worldToView);
        command.SetGlobalMatrix("_InvViewMatrix", viewToWorld);
        command.SetGlobalMatrix("_ProjMatrix", gpuProjectionMatrix);

        command.SetGlobalMatrix("_InvViewProjMatrix", viewToWorld * gpuProjectionMatrix.inverse);
        command.SetGlobalVector("_ScreenSize", new Vector4(resolution.x, resolution.y, 1f / resolution.x, 1f / resolution.y));

        gpuProjectionMatrix[1, 1] = -gpuProjectionMatrix[1, 1];
        gpuProjectionMatrix[1, 2] = -gpuProjectionMatrix[1, 2];
        viewProjectionMatrix = gpuProjectionMatrix * worldToView;
        command.SetGlobalMatrix("_ViewProjMatrix", viewProjectionMatrix);

        //var zBufferParams = ZBufferParams(near, far, SystemInfo.usesReversedZBuffer);
        //command.SetGlobalVector("_ZBufferParams", zBufferParams);
    }

    public static void SetupCameraProperties(CommandBuffer command, int frameCount, Camera camera, ScriptableRenderContext context, Vector2Int resolution, out Matrix4x4 viewProjectionMatrix, bool flip = false)
    {
        SetupCameraProperties(command, frameCount, camera, context, resolution, cullingPlanes, out viewProjectionMatrix, flip);
    }
}
