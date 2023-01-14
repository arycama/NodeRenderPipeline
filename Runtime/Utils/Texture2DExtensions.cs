using System;
using UnityEngine;

public static class Texture2DExtensions
{
    // Resolution of a texture resolution at a mip level
    public static int MipResolution(int mip, int resolution)
    {
        return resolution >> mip;
    }

    public static int PixelCount(int resolution)
    {
        return (4 * resolution * resolution - 1) / 3;
    }

    public static int MipCount(int resolution)
    {
        return (int)Math.Log(resolution, 2) + 1;
    }

    public static int MipCount(int width, int height) => MipCount(Math.Max(width, height));

    /// <summary>
    /// Calculates the index offset to access a mip-level of a texture stored as a 1D array. 
    /// </summary>
    /// <param name="mip">The desired mip level</param>
    /// <param name="resolution">The resolution of the texture</param>
    /// <returns></returns>
    public static int MipOffset(int mip, int resolution)
    {
        var pixelCount = PixelCount(resolution);
        var mipCount = MipCount(resolution);
        var endMipOffset = ((1 << (2 * (mipCount - mip))) - 1) / 3;
        return pixelCount - endMipOffset;
    }

    // Converts a 1D index to a mip level
    public static int IndexToMip(int index, int resolution)
    {
        var z = PixelCount(resolution);
        var w = MipCount(resolution);
        return (int)(w - Math.Log(3 * (z - index) + 1, 2) / 2);
    }

    // Converts a texture byte offset to an XYZ coordinate. (Where Z is the mip level)
    public static Vector3Int TextureByteOffsetToCoord(int index, int resolution)
    {
        var mip = IndexToMip(index, resolution);
        var localMipCoord = index - MipOffset(mip, resolution);
        var mipSize = MipResolution(mip, resolution);
        return new Vector3Int(localMipCoord % mipSize, localMipCoord / mipSize, mip);
    }

    public static int TextureCoordToOffset(Vector3Int position, int resolution)
    {
        var mipSize = MipResolution(position.z, resolution);
        var coord = position.x + position.y * mipSize;
        var mipOffset = MipOffset(position.z, resolution);
        return mipOffset + coord;
    }
}