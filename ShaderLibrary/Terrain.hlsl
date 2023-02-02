#ifndef TERRAIN_INCLUDED
#define TERRAIN_INCLUDED

#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Core.hlsl"

struct LayerData
{
	float Scale;
	float Blending;
	float Stochastic;
	float Rotation;
};

struct IdLayerData
{
	float index, offsetX, offsetY, rotation, weight;
};

struct IdMapData
{
	IdLayerData layer0, layer1;
	float triplanar;
};

StructuredBuffer<LayerData> _TerrainLayerData;
Texture2D<float2> _TerrainNormalMap;
Texture2D<float> _TerrainHeightmapTexture, _TerrainHolesTexture;
SamplerState sampler_TerrainNormalMap;

float4 _TerrainScaleOffset, _TerrainRemapHalfTexel;
float _TerrainHeightScale, _TerrainHeightOffset;

IdMapData UnpackIdMapData(uint data)
{
	float blend = ((data >> 26) & 0xF) / 16.0;
	
	IdMapData result;
	result.layer0.index = ((data >> 0) & 0xF);
	result.layer0.offsetX = ((data >> 4) & 0x3);
	result.layer0.offsetY = ((data >> 6) & 0x3);
	result.layer0.rotation = ((data >> 8) & 0x1F) / 32.0;
	result.layer0.weight = 1.0 - blend;
	
	result.layer1.index = ((data >> 13) & 0xF);
	result.layer1.offsetX = ((data >> 17) & 0x3);
	result.layer1.offsetY = ((data >> 19) & 0x3);
	result.layer1.rotation = ((data >> 21) & 0x1F) / 32.0;
	result.layer1.weight = blend;
	
	result.triplanar = ((data >> 30) & 0x3) / 4.0;
	return result;
}

float2 WorldToTerrainPosition(float3 positionWS)
{
	return positionWS.xz * _TerrainScaleOffset.xy + _TerrainScaleOffset.zw;
}

float2 WorldToTerrainPositionHalfTexel(float3 positionWS)
{
	return positionWS.xz * _TerrainRemapHalfTexel.xy + _TerrainRemapHalfTexel.zw;
}

float GetTerrainHeight(float2 uv)
{
	return _TerrainHeightmapTexture.SampleLevel(_LinearClampSampler, uv, 0) * _TerrainHeightScale + _TerrainHeightOffset;
}

float GetTerrainHeight(float3 positionWS)
{
	float2 uv = WorldToTerrainPositionHalfTexel(positionWS);
	return GetTerrainHeight(uv);
}

float3 GetTerrainNormal(float2 uv, float2 dx, float2 dy)
{
	return normalize(float3(_TerrainNormalMap.SampleGrad(sampler_TerrainNormalMap, uv, dx, dy), 1.0).xzy);
}

float3 GetTerrainNormal(float2 uv)
{
	return GetTerrainNormal(uv, ddx(uv), ddy(uv));
}

float3 GetTerrainNormal(float3 positionWS)
{
	float2 uv = WorldToTerrainPositionHalfTexel(positionWS);
	return GetTerrainNormal(uv);
}

void ClipHoles(float2 uv)
{
    #ifdef _ALPHATEST_ON
		float hole = _TerrainHolesTexture.Sample(_LinearClampSampler, uv);
		clip(hole == 0.0f ? -1 : 1);
	#endif
}

#endif