using UnityEngine;
using UnityEngine.Rendering;

public static class RenderTextureDescriptorExtensions
{
    public static RenderTextureDescriptor EnableRandomWrite(this RenderTextureDescriptor desc)
    {
        desc.enableRandomWrite = true;
        return desc;
    }

    public static RenderTextureDescriptor Downsample(this RenderTextureDescriptor desc, int downsample)
    {
        desc.width >>= downsample;
        desc.height >>= downsample;
        return desc;
    }

    public static RenderTextureDescriptor Dimension(this RenderTextureDescriptor desc, TextureDimension dimension)
    {
        desc.dimension = dimension;
        return desc;
    }

    public static RenderTextureDescriptor VolumeDepth(this RenderTextureDescriptor desc, int depth)
    {
        desc.volumeDepth = depth;
        return desc;
    }

    public static RenderTextureDescriptor Tex2DArray(this RenderTextureDescriptor desc, int depth)
    {
        desc.Dimension(TextureDimension.Tex2DArray);
        desc.VolumeDepth(depth);
        return desc;
    }
}
