using UnityEngine;

struct ReflectionProbeData
{
    public Matrix4x4 worldToLocal;
    public Matrix4x4 localToWorld;
    public Vector3 min;
    public float blendDistance;
    public Vector3 max;
    public float index;
    public Vector3 center;
    public float exposure;

    public ReflectionProbeData(Vector3 position, Quaternion rotation, Vector3 min, float blendDistance, Vector3 max, float index, Vector3 center, bool boxProjection, float exposure)
    {
        this.localToWorld = Matrix4x4.TRS(position, rotation, 0.5f * (max - min));
        this.worldToLocal = localToWorld.inverse;
        this.min = min;
        this.blendDistance = blendDistance;
        this.max = max;
        this.index = index;
        this.center = center;
        this.exposure = exposure;

        if (boxProjection)
        {
            this.blendDistance = -Mathf.Max(1e-6f, this.blendDistance);
        }
    }
}
