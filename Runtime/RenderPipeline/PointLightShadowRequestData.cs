using System;
using UnityEngine;

public struct PointLightShadowRequestData
{
    public PointLightShadowRequestData(int visibleLightIndex, Bounds bounds) : this()
    {
        VisibleLightIndex = visibleLightIndex;
        Bounds = bounds;
    }

    public int VisibleLightIndex { get; }
    public Bounds Bounds { get; }

    public ShadowRequestData PositiveX { get; private set; }
    public ShadowRequestData NegativeX { get; private set; }
    public ShadowRequestData PositiveY { get; private set; }
    public ShadowRequestData NegativeY { get; private set; }
    public ShadowRequestData PositiveZ { get; private set; }
    public ShadowRequestData NegativeZ { get; private set; }

    public ShadowRequestData this[int face]
    {
        get => face switch
        {
            0 => PositiveX,
            1 => NegativeX,
            2 => PositiveY,
            3 => NegativeY,
            4 => PositiveZ,
            5 => NegativeZ,
            _ => throw new ArgumentOutOfRangeException(),
        };

        set
        {
            switch (face)
            {
                case 0:
                    PositiveX = value;
                    break;
                case 1:
                    NegativeX = value;
                    break;
                case 2:
                    PositiveY = value;
                    break;
                case 3:
                    NegativeY = value;
                    break;
                case 4:
                    PositiveZ = value;
                    break;
                case 5:
                    NegativeZ = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
