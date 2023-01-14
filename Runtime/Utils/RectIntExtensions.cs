using System;
using UnityEngine;

public static class RectIntExtensions
{
    public static RectInt Encapsulate(this RectInt rectInt, int x, int y)
    {
        rectInt.xMin = Math.Min(x, rectInt.xMin);
        rectInt.xMax = Math.Max(x + 1, rectInt.xMax);
        rectInt.yMin = Math.Min(y, rectInt.yMin);
        rectInt.yMax = Math.Max(y + 1, rectInt.yMax);
        return rectInt;
    }

    public static RectInt Encapsulate(this RectInt rectInt, Vector2Int position)
    {
        rectInt.xMin = Math.Min(position.x, rectInt.xMin);
        rectInt.xMax = Math.Max(position.x + 1, rectInt.xMax);
        rectInt.yMin = Math.Min(position.y, rectInt.yMin);
        rectInt.yMax = Math.Max(position.y + 1, rectInt.yMax);
        return rectInt;
    }
}