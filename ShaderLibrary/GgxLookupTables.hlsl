#ifndef GGX_LOOKUP_TABLES_INCLUDED
#define GGX_LOOKUP_TABLES_INCLUDED

#include "Core.hlsl"

Texture3D<float> _GGXDirectionalAlbedoMS;
Texture2D<float2> _GGXDirectionalAlbedo;
Texture2D<float> _GGXAverageAlbedo, _GGXAverageAlbedoMS;

cbuffer GGXLookupConstants
{
	float4 _GGXDirectionalAlbedoRemap;
	float2 _GGXAverageAlbedoRemap;
	float2 _GGXDirectionalAlbedoMSScaleOffset;
	float4 _GGXAverageAlbedoMSRemap;
};

float2 GGXDirectionalAlbedo(float NdotV, float perceptualRoughness)
{
	float2 uv = float2(sqrt(NdotV), perceptualRoughness) * _GGXDirectionalAlbedoRemap.xy + _GGXDirectionalAlbedoRemap.zw;
	return _GGXDirectionalAlbedo.SampleLevel(_LinearClampSampler, uv, 0);
}

float GGXAverageAlbedo(float perceptualRoughness)
{
	float2 averageUv = float2(perceptualRoughness * _GGXAverageAlbedoRemap.x + _GGXAverageAlbedoRemap.y, 0.0);
	return _GGXAverageAlbedo.SampleLevel(_LinearClampSampler, averageUv, 0.0);
}

float GGXDirectionalAlbedoMS(float NdotV, float perceptualRoughness, float f0)
{
	float3 uv = float3(sqrt(NdotV), perceptualRoughness, f0) * _GGXDirectionalAlbedoMSScaleOffset.x + _GGXDirectionalAlbedoMSScaleOffset.y;
	return _GGXDirectionalAlbedoMS.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float GGXAverageAlbedoMS(float perceptualRoughness, float f0)
{
	float2 uv = float2(perceptualRoughness, f0) * _GGXAverageAlbedoMSRemap.xy + _GGXAverageAlbedoMSRemap.zw;
	return _GGXAverageAlbedoMS.SampleLevel(_LinearClampSampler, uv, 0.0);
}

#endif