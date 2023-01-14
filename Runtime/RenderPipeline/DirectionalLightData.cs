using System;
using UnityEngine;

public struct DirectionalLightData : IEquatable<DirectionalLightData>
{
    private Vector3 color;
    private readonly float angularDiameter;
    private Vector3 direction;
    private readonly int shadowIndex;

    public Vector3 Color => color;
    public float AngularDiameter => angularDiameter;
    public Vector3 Direction => direction;
    public int ShadowIndex => shadowIndex;

    public DirectionalLightData(Vector3 color, float angularDiameter, Vector3 direction, int shadowIndex)
    {
        this.color = color;
        this.angularDiameter = angularDiameter;
        this.direction = direction;
        this.shadowIndex = shadowIndex;
    }

    public override bool Equals(object obj)
    {
        return obj is DirectionalLightData data && Equals(data);
    }

    public bool Equals(DirectionalLightData other)
    {
        return color.Equals(other.color) &&
               angularDiameter == other.angularDiameter &&
               direction.Equals(other.direction) &&
               shadowIndex == other.shadowIndex;
    }

    public override int GetHashCode()
    {
        int hashCode = 334599755;
        hashCode = hashCode * -1521134295 + color.GetHashCode();
        hashCode = hashCode * -1521134295 + angularDiameter.GetHashCode();
        hashCode = hashCode * -1521134295 + direction.GetHashCode();
        hashCode = hashCode * -1521134295 + shadowIndex.GetHashCode();
        hashCode = hashCode * -1521134295 + Color.GetHashCode();
        hashCode = hashCode * -1521134295 + AngularDiameter.GetHashCode();
        hashCode = hashCode * -1521134295 + Direction.GetHashCode();
        hashCode = hashCode * -1521134295 + ShadowIndex.GetHashCode();
        return hashCode;
    }

    public static bool operator ==(DirectionalLightData left, DirectionalLightData right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(DirectionalLightData left, DirectionalLightData right)
    {
        return !(left == right);
    }
}