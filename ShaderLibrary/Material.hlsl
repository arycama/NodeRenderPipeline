#pragma once

#include "Math.hlsl"
#include "Utility.hlsl"

// This is actuall the last mip index, we generate 7 mips of convolution
const static float UNITY_SPECCUBE_LOD_STEPS = 6.0;

float PerceptualSmoothnessToPerceptualRoughness(float smoothness)
{
	return 1.0 - smoothness;
}

float RoughnessToPerceptualRoughness(float roughness)
{
	return sqrt(roughness);
}

float RoughnessToPerceptualSmoothness(float roughness)
{
	return 1.0 - RoughnessToPerceptualRoughness(roughness);
}

float PerceptualRoughnessToRoughness(float perceptualRoughness)
{
	return Sq(perceptualRoughness);
}

float LengthToRoughness(float len)
{
	len = 3.0 * len - 2.0; // Remap from 2/3:1 to 0:1
	float2 uv = Remap01ToHalfTexelCoord(float2(len, 0.5), float2(256.0, 1));
	return _LengthToRoughness.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float LengthToPerceptualRoughness(float len)
{
	return RoughnessToPerceptualRoughness(LengthToRoughness(len));
}

float LengthToSmoothness(float len)
{
	return RoughnessToPerceptualSmoothness(LengthToRoughness(len));
}

float RoughnessToNormalLength(float roughness)
{
	if (roughness < 1e-3)
		return 1.0;
	if (roughness >= 1.0)
		return 2.0 / 3.0;

	float a = sqrt(saturate(1.0 - pow(roughness, 2.0)));
	return (a - (1.0 - a * a) * atanh(a)) / (a * a * a);
}

float PerceptualRoughnessToNormalLength(float perceptualRoughness)
{
	return RoughnessToNormalLength(PerceptualRoughnessToRoughness(perceptualRoughness));
}

float PerceptualSmoothnessToRoughness(float perceptualSmoothness)
{
	return PerceptualRoughnessToRoughness(1.0 - perceptualSmoothness);
}

float SmoothnessToNormalLength(float smoothness)
{
	return RoughnessToNormalLength(PerceptualSmoothnessToRoughness(smoothness));
}

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

// The inverse of the *approximated* version of perceptualRoughnessToMipmapLevel().
float MipmapLevelToPerceptualRoughness(float mipmapLevel)
{
	float perceptualRoughness = saturate(mipmapLevel / UNITY_SPECCUBE_LOD_STEPS);
	return saturate(1.7 / 1.4 - sqrt(2.89 / 1.96 - (2.8 / 1.96) * perceptualRoughness));
}

float3 ComputeDiffuseColor(float3 albedo, float metallic)
{
	return lerp(albedo, 0.0, metallic);
}

// The *accurate* version of the non-linear remapping. It works by
// approximating the cone of the specular lobe, and then computing the MIP map level
// which (approximately) covers the footprint of the lobe with a single texel.
// Improves the perceptual roughness distribution and adds reflection (contact) hardening.
// TODO: optimize!
float PerceptualRoughnessToMipmapLevel(float perceptualRoughness, float NdotR)
{
	float m = PerceptualRoughnessToRoughness(perceptualRoughness);

    // Remap to spec power. See eq. 21 in --> https://dl.dropboxusercontent.com/u/55891920/papers/mm_brdf.pdf
	float n = (2.0 / max(HalfEps, m * m)) - 2.0;

    // Remap from n_dot_h formulation to n_dot_r. See section "Pre-convolved Cube Maps vs Path Tracers" --> https://s3.amazonaws.com/docs.knaldtech.com/knald/1.0.0/lys_power_drops.html
	n /= (4.0 * max(NdotR, HalfEps));

    // remap back to square root of float roughness (0.25 include both the sqrt root of the conversion and sqrt for going from roughness to perceptualRoughness)
	perceptualRoughness = pow(2.0 / (n + 2.0), 0.25);

	return perceptualRoughness * UNITY_SPECCUBE_LOD_STEPS;
}
