#ifndef REFLECTION_PROBE_INCLUDED
#define REFLECTION_PROBE_INCLUDED

#include "Core.hlsl"
#include "MatrixUtils.hlsl"
#include "Lighting.hlsl"

struct ReflectionProbeData
{
	float4x4 worldToLocal;
	float4x4 localToWorld;
	float3 min;
	float blendDistance;
	float3 max;
	float index;
	float3 center;
	float exposure;
};

TextureCubeArray<float3> _ReflectionProbes;
StructuredBuffer<ReflectionProbeData> _ReflectionProbeData;
Buffer<float4> _AmbientData;
uint _ReflectionProbeCount;

float4 SampleReflectionProbe(float3 positionWS, float3 R, float mip, float3 N, float3 albedo, float occlusion, out float3 ambient)
{
	// todo: camera relative
	positionWS += _WorldSpaceCameraPos;
	
	float4 result = 0.0;
	
	float4 sh[7];
	for (uint i = 0; i < _ReflectionProbeCount; i++)
	{
		ReflectionProbeData probe = _ReflectionProbeData[i];
			
		// Calculate distance from AABB center
		float blend = abs(probe.blendDistance);
		
		float3 localPosition = MultiplyPoint3x4(probe.worldToLocal, positionWS);
		
		float3 dist = max(0, (1 - abs(localPosition)) / (blend / ((probe.max - probe.min) * 0.5)));
		float weight = Min3(dist);
		
		if (weight <= 0.0)
			continue;
			
		// Box 
		bool isBox = probe.blendDistance < 0;
		if (isBox)
		{
			float3 localR = MultiplyVector(probe.worldToLocal, R, false);
			float3 factors = ((localR >= 0.0 ? 1.0 : -1.0) - (localPosition)) / localR;
			float scalar = Min3(factors);
			R = localR * scalar + (localPosition - 0.0);
			R = MultiplyVector(probe.localToWorld, R, false);
		}
			
		float3 probeSample = _ReflectionProbes.SampleLevel(_TrilinearClampSampler, float4(R, i), mip);
		
		// Remove the exposure the probe was baked with, before applying the current exposure
		float exposureFactor = ApplyExposure(rcp(probe.exposure + (probe.exposure == 0.0))).r;
		probeSample *= exposureFactor;
		result += float4(probeSample, 1.0) * weight;
		
		[unroll]
		for (uint j = 0; j < 7; j++)
			sh[j] = _AmbientData[i * 7 + j] * weight * exposureFactor;
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
	
	ambient = EvaluateSH(N, albedo, occlusion, sh);
	
	return result;
}

float4 SampleReflectionProbe(float3 positionWS, float3 R, float mip)
{
	float3 ambient;
	return SampleReflectionProbe(positionWS, R, mip, 0, 0, 0, ambient);
}

#endif