using UnityEngine;
using UnityEngine.Rendering;

public readonly struct ShadowRequestData
{
    public bool IsValid { get; }
    public Matrix4x4 ViewMatrix { get; }
    public Matrix4x4 ProjectionMatrix { get; }
    public ShadowSplitData ShadowSplitData { get; }
    public bool RenderShadowCasters { get; }
    public float Near { get; }
    public float Far { get; }

    public ShadowRequestData(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix, ShadowSplitData shadowSplitData, float near, float far, bool renderShadowCasters)
    {
        IsValid = true;
        ViewMatrix = viewMatrix;
        ProjectionMatrix = projectionMatrix;
        ShadowSplitData = shadowSplitData;
        Near = near;
        Far = far;
        RenderShadowCasters = renderShadowCasters;
    }
}