#ifndef LIGHTING_COMMON_INCLUDED
#define LIGHTING_COMMON_INCLUDED

struct DirectionalLightData
{
	float3 Color;
	float AngularDiameter;

	float3 Direction;
	uint ShadowIndex;
};

// Rerrange this for better cache locality, consider seperating depending on light type
struct LightData
{
	float3 positionWS;
	float range;
	float3 color;
	uint lightType;
	float3 right;
	float angleScale;
	float3 up;
	float angleOffset;
	float3 forward;
	uint shadowIndex;
	float2 size;
	float shadowProjectionX;
	float shadowProjectionY;
};

uint _DirectionalLightCount, _LightCount;
StructuredBuffer<LightData> _LightData;
StructuredBuffer<DirectionalLightData> _DirectionalLightData;

TextureCube<float3> _SkyReflection;

Buffer<uint> _LightClusterList;
Texture3D<uint2> _LightClusterIndices;
uint _TileSize;
float _ClusterScale;
float _ClusterBias;

#endif