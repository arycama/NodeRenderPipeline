#ifndef REFLECTION_PROBE_INCLUDED
#define REFLECTION_PROBE_INCLUDED

#include "Core.hlsl"
#include "MatrixUtils.hlsl"
#include "Lighting.hlsl"

struct ReflectionProbeData
{
	float3x4 influenceWorldToLocal;
	float3x4 worldToLocal;
	float3 center;
	float index;
	float3 extentsOverBlend;
	float exposure;
};

TextureCubeArray<float3> _ReflectionProbes;
StructuredBuffer<ReflectionProbeData> _ReflectionProbeData;
Buffer<float4> _AmbientData;
uint _ReflectionProbeCount;
Buffer<float> _SkyOcclusion;

// From Moving Frostbite to PBR document
// This function fakes the roughness based integration of reflection probes by adjusting the roughness value
float ComputeDistanceBaseRoughness(float distIntersectionToShadedPoint, float distIntersectionToProbeCenter, float perceptualRoughness)
{
	float newPerceptualRoughness = clamp(distIntersectionToShadedPoint / distIntersectionToProbeCenter * perceptualRoughness, 0, perceptualRoughness);
	return lerp(newPerceptualRoughness, perceptualRoughness, perceptualRoughness);
}

float3 GetReflectionVector(float3 R, float3x4 worldToLocal, float3 positionWS, float3 probeOrigin)
{
	// Intersection with OBB convertto unit box space
	// Transform in local unit parallax cube space (scaled and rotated)
	float3 localR = MultiplyVector(worldToLocal, R, false);
	float3 localP = MultiplyPoint3x4(worldToLocal, positionWS);
	float3 furthestPlane = ((localR >= 0.0 ? 1.0 : -1.0) - localP) / localR;
	float intersectDist = Min3(furthestPlane);

	// Use Distance in WS directly to recover intersection
	float3 intersectPositionWS = R * intersectDist + positionWS;
	return intersectPositionWS - probeOrigin;
}

float4 SampleReflectionProbe(float3 positionWS, const float3 R, float mip, float3 N, float3 albedo, float occlusion, out float3 ambient)
{
	// todo: camera relative
	positionWS += _WorldSpaceCameraPos;
	
	float4 result = 0.0;
	
	float4 sh[7];
	for (uint i = 0; i < _ReflectionProbeCount; i++)
	{
		ReflectionProbeData probe = _ReflectionProbeData[i];
			
		// Calculate distance from AABB center
		float3 localPosition = MultiplyPoint3x4(probe.influenceWorldToLocal, positionWS);
		float3 dist = max(0.0, (1.0 - abs(localPosition)) * probe.extentsOverBlend);
		float weight = Min3(dist);
		if (weight <= 0.0)
			continue;
			
		float3 probeR = GetReflectionVector(R, probe.worldToLocal, positionWS, probe.center);
		float3 probeSample = _ReflectionProbes.SampleLevel(_TrilinearClampSampler, float4(probeR, probe.index), mip);
		
		// Remove the exposure the probe was baked with, before applying the current exposure
		float exposureFactor = ApplyExposure(rcp(probe.exposure)).r;
		probeSample *= exposureFactor;
		result += float4(probeSample, 1.0) * weight;
		
		[unroll]
		for (uint j = 0; j < 7; j++)
			sh[j] = _AmbientData[probe.index * 7 + j] * weight * exposureFactor;
	}
	
	if(result.a <= 0.0)
	{
		ambient = 0.0;
		return 0.0;
	}
	
	// Normalize
	result.rgb /= result.a;
	
	[unroll]
	for (i = 0; i < 7; i++)
		sh[i] /= result.a;
	
	// Also clamp a to 1.0, so that calling code can use it to lerp to skybox
	result.a = saturate(result.a);
	
	ambient = AmbientLight(N, occlusion, albedo, sh);
	
	return result;
}

float4 SampleReflectionProbe(float3 positionWS, float3 R, float mip)
{
	float3 ambient;
	return SampleReflectionProbe(positionWS, R, mip, 0, 0, 0, ambient);
}

float GetSkyVisibility(float3 positionWS, float3 N)
{
	// todo: camera relative
	positionWS += _WorldSpaceCameraPos;
	
	float result = 0.0, weightSum = 0.0;
	for (uint i = 0; i < _ReflectionProbeCount; i++)
	{
		ReflectionProbeData probe = _ReflectionProbeData[i];
		
		// Calculate distance from AABB center
		float3 localPosition = MultiplyPoint3x4(probe.worldToLocal, positionWS);
		float3 dist = max(0.0, (1.0 - abs(localPosition)) * probe.extentsOverBlend);
		float weight = Min3(dist);
		if (weight <= 0.0)
			continue;

		uint index = probe.index * 9;
		float skyVisibility = _SkyOcclusion[index + 0];
		skyVisibility += _SkyOcclusion[index + 1] * N.y;
		skyVisibility += _SkyOcclusion[index + 2] * N.z;
		skyVisibility += _SkyOcclusion[index + 3] * N.x;
		skyVisibility += _SkyOcclusion[index + 4] * N.y * N.x;
		skyVisibility += _SkyOcclusion[index + 5] * N.y * N.z;
		skyVisibility += _SkyOcclusion[index + 6] * (3.0 * N.z * N.z - 1.0);
		skyVisibility += _SkyOcclusion[index + 7] * N.x * N.z;
		skyVisibility += _SkyOcclusion[index + 8] * (N.x * N.x - N.y * N.y);
		
		result += skyVisibility * weight;
		weightSum += weight;
	}
	
	return weightSum > 0.0 ? result / weightSum : 1.0;
}

float GetSkyVisibilityL0(float3 positionWS)
{
	// todo: camera relative
	positionWS += _WorldSpaceCameraPos;
	
	float result = 0.0, weightSum = 0.0;
	for (uint i = 0; i < _ReflectionProbeCount; i++)
	{
		ReflectionProbeData probe = _ReflectionProbeData[i];
		
		// Calculate distance from AABB center
		float3 localPosition = MultiplyPoint3x4(probe.worldToLocal, positionWS);
		float3 dist = max(0.0, (1.0 - abs(localPosition)) * probe.extentsOverBlend);
		float weight = Min3(dist);
		if (weight <= 0.0)
			continue;

		result += _SkyOcclusion[probe.index * 9] * weight;
		weightSum += weight;
	}
	
	return weightSum > 0.0 ? result / weightSum : 1.0;
}

#endif