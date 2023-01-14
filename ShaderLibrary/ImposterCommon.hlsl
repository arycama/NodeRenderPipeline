#ifndef IMPOSTER_COMMON_INCLUDED
#define IMPOSTER_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

float3 GridToVector(float2 coord, bool isOctahedron)
{
	if (isOctahedron)
		return UnpackNormalOctQuadEncode(2.0 * coord - 1.0).xzy;
	else
		return UnpackNormalHemiOctEncode(2.0 * coord - 1.0).xzy;
}

float2 VectorToGrid(float3 vec, bool isOctahedron)
{
	if (isOctahedron)
		return 0.5 * PackNormalOctQuadEncode(vec.xzy) + 0.5;
	else
		return 0.5 * PackNormalHemiOctEncode(vec.xzy) + 0.5;
}

float3x3 ObjectToTangentMatrix(float3 normal)
{
	float3 tangent = abs(normal.y) == 1 ? float3(normal.y, 0, 0) : normalize(cross(normal, float3(0, 1, 0)));
	float3 binormal = cross(tangent, normal);
	return float3x3(tangent, binormal, normal);
}

#endif