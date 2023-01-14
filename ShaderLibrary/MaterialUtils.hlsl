#ifndef MATERIAL_UTILS_INCLUDED
#define MATERIAL_UTILS_INCLUDED

float ConvertAnisotropicPerceptualRoughnessToRoughness(float2 anisotropicPerceptualRoughness)
{
    return saturate((pow(anisotropicPerceptualRoughness.x, 2.0) + pow(anisotropicPerceptualRoughness.y, 2.0)) / 2.0);
}

float ConvertAnisotropicPerceptualRoughnessToPerceptualRoughness(float2 anisotropicPerceptualRoughness)
{
    return sqrt(ConvertAnisotropicPerceptualRoughnessToRoughness(anisotropicPerceptualRoughness));
}

float ConvertAnisotropicRoughnessToRoughness(float2 anisotropicRoughness)
{
    return saturate((anisotropicRoughness.x + anisotropicRoughness.y) / 2.0);
}

float ConvertAnisotropicRoughnessToPerceptualRoughness(float2 anisotropicRoughness)
{
    return sqrt(ConvertAnisotropicRoughnessToRoughness(anisotropicRoughness));
}

#endif