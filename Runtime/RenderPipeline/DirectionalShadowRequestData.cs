using System;

public struct DirectionalShadowRequestData
{
    public DirectionalShadowRequestData(int visibleLightIndex) : this()
    {
        VisibleLightIndex = visibleLightIndex;
    }

    public int VisibleLightIndex { get; }

    public ShadowRequestData Cascade0 { get; private set; }
    public ShadowRequestData Cascade1 { get; private set; }
    public ShadowRequestData Cascade2 { get; private set; }
    public ShadowRequestData Cascade3 { get; private set; }
    public ShadowRequestData Cascade4 { get; private set; }
    public ShadowRequestData Cascade5 { get; private set; }

    public ShadowRequestData this[int cascade]
    {
        get => cascade switch
        {
            0 => Cascade0,
            1 => Cascade1,
            2 => Cascade2,
            3 => Cascade3,
            4 => Cascade4,
            5 => Cascade5,
            _ => throw new ArgumentOutOfRangeException(),
        };

        set
        {
            switch (cascade)
            {
                case 0:
                    Cascade0 = value;
                    break;
                case 1:
                    Cascade1 = value;
                    break;
                case 2:
                    Cascade2 = value;
                    break;
                case 3:
                    Cascade3 = value;
                    break;
                case 4:
                    Cascade3 = value;
                    break;
                case 5:
                    Cascade3 = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}