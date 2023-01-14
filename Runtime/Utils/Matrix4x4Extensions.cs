using UnityEngine;

public static class Matrix4x4Extensions
{
    public static float Near(this Matrix4x4 matrix) => matrix[2, 3] / (matrix[2, 2] - 1f);
    public static float Far(this Matrix4x4 matrix) => matrix[2, 3] / (matrix[2, 2] + 1f);
    public static float Fov(this Matrix4x4 matrix) => matrix[1, 1];
    public static float Aspect(this Matrix4x4 matrix) => matrix.m11 / matrix.m00;
    public static float OrthoWidth(this Matrix4x4 matrix) => 2f / matrix.m00;
    public static float OrthoHeight(this Matrix4x4 matrix) => 2f / matrix.m11;
    public static float OrthoNear(this Matrix4x4 matrix) => (1f + matrix.m23) / matrix.m22;
    public static float OrthoFar(this Matrix4x4 matrix) => (matrix.m23 - 1f) / matrix.m22;

    public static Vector3 Right(this Matrix4x4 matrix) => matrix.GetColumn(0);

    public static Vector3 Up(this Matrix4x4 matrix) => matrix.GetColumn(1);

    public static Vector3 Forward(this Matrix4x4 matrix) => matrix.GetColumn(2);

    public static Vector3 Position(this Matrix4x4 matrix) => matrix.GetColumn(3);

    /// <summary>
    /// Converts a matrix to map from -1:1 to 0:1
    /// </summary>
    /// <param name="m"></param>
    /// <param name="reverseZ"></param>
    /// <returns></returns>
    public static Matrix4x4 ConvertToAtlasMatrix(this Matrix4x4 m, bool reverseZ = true, float width = 1f, float height = 1f, float depth = 1f)
    {
        if (reverseZ && SystemInfo.usesReversedZBuffer)
            m.SetRow(2, -m.GetRow(2));

        m.SetRow(0, 0.5f * (m.GetRow(0) + m.GetRow(3)) * width);
        m.SetRow(1, 0.5f * (m.GetRow(1) + m.GetRow(3)) * height);
        m.SetRow(2, 0.5f * (m.GetRow(2) + m.GetRow(3)) * depth);
        return m;
    }

    public static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Vector2Int resolution, Vector2 lensShift, float fov, float aspect, Matrix4x4 viewToWorld, bool flip, bool isOrthographic = false)
    {
        Matrix4x4 viewSpaceRasterTransform;
        var verticalFoV = fov * Mathf.Deg2Rad;

        if (isOrthographic)
        {
            // For ortho cameras, project the skybox with no perspective
            // the same way as builtin does (case 1264647)
            viewSpaceRasterTransform = new Matrix4x4(
                new Vector4(-2.0f / resolution.x, 0.0f, 0.0f, 0.0f),
                new Vector4(0.0f, -2.0f / resolution.y, 0.0f, 0.0f),
                new Vector4(1.0f, 1.0f, -1.0f, 0.0f),
                new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
        }
        else
        {
            // Compose the view space version first.
            // V = -(X, Y, Z), s.t. Z = 1,
            // X = (2x / resX - 1) * tan(vFoV / 2) * ar = x * [(2 / resX) * tan(vFoV / 2) * ar] + [-tan(vFoV / 2) * ar] = x * [-m00] + [-m20]
            // Y = (2y / resY - 1) * tan(vFoV / 2)      = y * [(2 / resY) * tan(vFoV / 2)]      + [-tan(vFoV / 2)]      = y * [-m11] + [-m21]
            float tanHalfVertFoV = Mathf.Tan(0.5f * verticalFoV);

            // Compose the matrix.
            float m21 = (1f + lensShift.y) * tanHalfVertFoV;
            float m11 = -2f / resolution.y * tanHalfVertFoV;

            float m20 = (1f + lensShift.x) * tanHalfVertFoV * aspect;
            float m00 = -2f / resolution.x * tanHalfVertFoV * aspect;

            if (flip)
            {
                // Flip Y.
                m11 = -m11;
                m21 = -m21;
            }

            viewSpaceRasterTransform = new Matrix4x4(new Vector4(m00, 0.0f, 0.0f, 0.0f),
                new Vector4(0.0f, m11, 0.0f, 0.0f),
                new Vector4(m20, m21, -1.0f, 0.0f),
                new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
        }

        return viewToWorld * viewSpaceRasterTransform;
    }

    public static Matrix4x4 LocalToWorld(Vector3 right, Vector3 up, Vector3 forward, Vector3 position) => new Matrix4x4(right, up, forward, new Vector4(position.x, position.y, position.z, 1f));

    public static Matrix4x4 LocalToWorld(Vector3 position, Quaternion rotation)
    {
        return LocalToWorld(rotation.Right(), rotation.Up(), rotation.Forward(), position);
    }

    public static Matrix4x4 WorldToLocal(Vector3 right, Vector3 up, Vector3 forward, Vector3 position)
    {
        var c0 = new Vector4(right.x, up.x, forward.x, 0f);
        var c1 = new Vector4(right.y, up.y, forward.y, 0f);
        var c2 = new Vector4(right.z, up.z, forward.z, 0f);
        var c3 = new Vector4(-Vector3.Dot(right, position), -Vector3.Dot(up, position), -Vector3.Dot(forward, position), 1f);
        return new Matrix4x4(c0, c1, c2, c3);
    }

    public static Matrix4x4 WorldToLocal(Vector3 position, Quaternion rotation)
    {
        return WorldToLocal(rotation.Right(), rotation.Up(), rotation.Forward(), position);
    }
}
