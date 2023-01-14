using UnityEngine;

public struct SpotShadowRequestData
{
    public int VisibleLightIndex { get; }
    public ShadowRequestData ShadowRequestData { get; }
    public Bounds Bounds { get; }

    public SpotShadowRequestData(int visibleLightIndex, Bounds bounds, ShadowRequestData shadowRequestData)
    {
        VisibleLightIndex = visibleLightIndex;
        Bounds = bounds;
        ShadowRequestData = shadowRequestData;
    }
}