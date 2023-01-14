#ifndef GGX_EXTENSIONS_INCLUDED
#define GGX_EXTENSIONS_INCLUDED

#include "GgxLookupTables.hlsl"

Texture3D<float> _GGXSpecularOcclusion;

float3 AverageFresnel(float3 f0)
{
	return f0 + (1.0 - f0) / 21.0;
}

float GGXDiffuse(float NdotL, float NdotV, float perceptualRoughness, float f0)
{
	return INV_PI;
	float Ewi = GGXDirectionalAlbedoMS(NdotL, perceptualRoughness, f0);
	float Ewo = GGXDirectionalAlbedoMS(NdotV, perceptualRoughness, f0);
	float Eavg = GGXAverageAlbedoMS(perceptualRoughness, f0);
	return INV_PI * (1.0 - Ewo) * (1.0 - Ewi) / (max(REAL_EPS, 1.0 - Eavg));
}

float3 GGXMultiScatter(float NdotV, float NdotL, float perceptualRoughness, float3 f0)
{
	float Ewi = GGXDirectionalAlbedo(NdotV, perceptualRoughness).g;
	float Ewo = GGXDirectionalAlbedo(NdotL, perceptualRoughness).g;
	float Eavg = GGXAverageAlbedo(perceptualRoughness);

	float ms = INV_PI * (1.0 - Ewi) * (1.0 - Ewo) * rcp(max(REAL_EPS, 1.0 - Eavg));

	float3 FAvg = AverageFresnel(f0);
	float3 f = Square(FAvg) * Eavg * rcp(max(REAL_EPS, 1.0 - FAvg * (1.0 - Eavg)));
	return ms * f;
}

float SpecularOcclusion(float NdotV, float perceptualRoughness, float visibility, float BdotR)
{
	float4 specUv = float4(NdotV, Square(perceptualRoughness), visibility, BdotR);
	
	// Remap to half texel
	float4 start = 0.5 * rcp(32.0);
	float4 len   = 1.0 - rcp(32.0);
	specUv = specUv * len + start;

	// 4D LUT
	float3 uvw0;
	uvw0.xy = specUv.xy;
	float q0Slice = clamp(floor(specUv.w * 32 - 0.5), 0, 31.0);
	q0Slice = clamp(q0Slice, 0, 32 - 1.0);
	float qWeight = max(specUv.w * 32 - 0.5 - q0Slice, 0);
	float2 sliceMinMaxZ = float2(q0Slice, q0Slice + 1) / 32 + float2(0.5, -0.5) / (32 * 32); //?
	uvw0.z = (q0Slice + specUv.z) / 32.0;
	uvw0.z = clamp(uvw0.z, sliceMinMaxZ.x, sliceMinMaxZ.y);

	float q1Slice = min(q0Slice + 1, 32 - 1);
	float nextSliceOffset = (q1Slice - q0Slice) / 32;
	float3 uvw1 = uvw0 + float3(0, 0, nextSliceOffset);

	float specOcc0 = _GGXSpecularOcclusion.SampleLevel(_LinearClampSampler, uvw0, 0.0);
	float specOcc1 = _GGXSpecularOcclusion.SampleLevel(_LinearClampSampler, uvw1, 0.0);
	return lerp(specOcc0, specOcc1, qWeight);
}

#endif