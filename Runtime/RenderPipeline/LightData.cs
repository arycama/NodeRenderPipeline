using UnityEngine;

public struct LightData
{
    public Vector3 positionWS;
    public float range;
    public Vector3 color;
    public uint lightType;
    public Vector3 right;
    public float angleScale;
    public Vector3 up;
    public float angleOffset;
    public Vector3 forward;
    public uint shadowIndex;
    public Vector2 size;
    public float shadowProjectionX;
    public float shadowProjectionY;

    public LightData(Vector3 positionWS, float range, Vector3 color, uint lightType, Vector3 right, float angleScale, Vector3 up, float angleOffset, Vector3 forward, uint shadowIndex, Vector2 size, float shadowProjectionX, float shadowProjectionY) : this()
    {
        this.positionWS = positionWS;
        this.range = range;
        this.color = color;
        this.lightType = lightType; // (uint)LightingUtils.GetLightType(light);
        this.right = right;
        this.angleScale = angleScale;
        this.up = up;
        this.angleOffset = angleOffset;
        this.forward = forward;
        this.shadowIndex = shadowIndex;
        this.size = size;
        this.shadowProjectionX = shadowProjectionX; // 1 + far / (near - far);
        this.shadowProjectionY = shadowProjectionY; // -(near * far) / (near - far);
    }
}