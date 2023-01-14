using UnityEngine;

public static class QuaternionExtensions
{
    public static Vector3 Right(this Quaternion q)
    {
        var t = 2 * Vector3.Cross(new Vector3(q.x, q.y, q.z), Vector3.right);
        return Vector3.right + q.w * t + Vector3.Cross(new Vector3(q.x, q.y, q.z), t);
    }

    public static Vector3 Up(this Quaternion q)
    {
        var t = 2 * Vector3.Cross(new Vector3(q.x, q.y, q.z), Vector3.up);
        return Vector3.up + q.w * t + Vector3.Cross(new Vector3(q.x, q.y, q.z), t);
    }

    public static Vector3 Forward(this Quaternion q)
    {
        var t = 2 * Vector3.Cross(new Vector3(q.x, q.y, q.z), Vector3.forward);
        return Vector3.forward + q.w * t + Vector3.Cross(new Vector3(q.x, q.y, q.z), t);
    }
}
