using UnityEngine;

public static class Vector3Extensions
{
    public static Vector3 Clamp(this Vector3 vector, float min, float max)
    {
        return new Vector3(Mathf.Clamp(vector.x, min, max), Mathf.Clamp(vector.y, min, max), Mathf.Clamp(vector.z, min, max));
    }

    public static Vector3 Y0(this Vector3 v) => new Vector3(v.x, 0f, v.z);

    public static Vector2 XZ(this Vector3 v) => new Vector3(v.x, v.z);
    public static Vector2 XY(this Vector3 v) => new Vector3(v.x, v.y);
}