using UnityEngine;

struct ReflectionProbeData
{
    public Matrix3x4 influenceWorldToLocal;
    public Matrix3x4 worldToLocal;
    public Vector3 center;
    public float index;
    public Vector3 extentsOverBlend;
    public float exposure;

    public ReflectionProbeData(EnvironmentProbe probe, int index, float exposure)
    {
        var t = probe.transform;

        influenceWorldToLocal = Matrix4x4.TRS(t.position + t.rotation * probe.InfluenceOffset, t.rotation, 0.5f * probe.InfluenceSize).inverse;
        this.extentsOverBlend = 0.5f * probe.InfluenceSize / probe.BlendDistance;

        worldToLocal = Matrix4x4.TRS(t.position + t.rotation * probe.ProjectionOffset, t.rotation, 0.5f * probe.ProjectionSize).inverse;
        center = t.position;
        this.index = index;
        this.exposure = exposure;
    }
}
