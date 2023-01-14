using UnityEngine;

public struct RendererBounds
{
    public Vector3 center;
    public float pad0;
    public Vector3 extents;
    public float pad1;

    public RendererBounds(Bounds bounds)
    {
        center = bounds.min;
        pad0 = 0;
        extents = bounds.size;
        pad1 = 0;
    }
}