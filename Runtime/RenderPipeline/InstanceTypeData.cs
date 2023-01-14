using UnityEngine;
// Data for an instance type. An instance type represents a specific object to be rendered, eg a prefab. It may have multiple lods, which may have multiple renderers, which may have multiple submeshes/materials.
public struct InstanceTypeData
{
    public Vector3 localReferencePoint;
    public float radius;
    public int lodCount, lodSizeBufferPosition, instanceCount, lodRendererOffset;

    public InstanceTypeData(Vector3 localReferencePoint, float radius, int lodCount, int lodSizeBufferPosition, int instanceCount, int lodRendererOffset)
    {
        this.localReferencePoint = localReferencePoint;
        this.radius = radius;
        this.lodCount = lodCount;
        this.lodSizeBufferPosition = lodSizeBufferPosition;
        this.instanceCount = instanceCount;
        this.lodRendererOffset = lodRendererOffset;
    }
}
