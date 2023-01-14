#ifndef REFLECTION_PROBE_INCLUDED
#define REFLECTION_PROBE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

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
Buffer<float4>_AmbientData;
uint _ReflectionProbeCount;

float4 SampleReflectionProbe(float3 positionWS, float3 R, float mip, float3 N, float3 albedo, float occlusion, out float3 ambient)
{
	// todo: camera relative
	positionWS += _WorldSpaceCameraPos;
	
	float4 result = 0.0;
	
	float4 data0 = 0.0, data1 = 0.0, data2 = 0.0;
	
	for (uint i = 0; i < _ReflectionProbeCount; i++)
	{
		ReflectionProbeData probe = _ReflectionProbeData[i];
			
		// Calculate distance from AABB center
		float blend = abs(probe.blendDistance);
		
		float3 localPosition = MultiplyPoint3x4(probe.worldToLocal, positionWS);
		
		float3 dist = max(0, (1 - abs(localPosition)) / (blend / ((probe.max - probe.min) * 0.5)));
		float weight = Min3(dist);
		
		if(weight <= 0.0)
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
		
		data0 += _AmbientData[i * 3 + 0] * weight * exposureFactor;
		data1 += _AmbientData[i * 3 + 1] * weight * exposureFactor;
		data2 += _AmbientData[i * 3 + 2] * weight * exposureFactor;
	}
	
	// Normalize if two probes are strongly blending
	if (result.a > 1.0)
	{
		result.rgb /= result.a;
		data0 /= result.a;
		data1 /= result.a;
		data2 /= result.a;
		
		// Also clamp a to 1.0, so that calling code can use it to lerp to skybox
		result.a = 1.0;
	}
	
	// Calculate the zonal harmonics expansion for V(x, ωi)*(n.l)
	float3 mb = GTAOMultiBounce(occlusion, albedo);
	float3 t;
	t.x = FastACosPos(sqrt(saturate(1.0 - mb.x)));
	t.y = FastACosPos(sqrt(saturate(1.0 - mb.y)));
	t.z = FastACosPos(sqrt(saturate(1.0 - mb.z)));
	
	float3 a = sin(t);
	float3 b = cos(t);

	float3 A0 = sqrt(4.0 * PI / 1.0) * (sqrt(1.0 * PI) / 2.0) * a * a;
	float3 A1 = sqrt(4.0 * PI / 3.0) * (sqrt(3.0 * PI) / 3.0) * (1.0 - b * b * b);
	float3 A2 = sqrt(4.0 * PI / 5.0) * (sqrt(5.0 * PI) / 16.0) * a * a * (2.0 + 6.0 * b * b);

	float3 irradiance =
        float3(data0.x, data1.x, data2.x) * A0 +
        float3(data0.y, data1.y, data2.y) * A1 * N.y +
        float3(data0.z, data1.z, data2.z) * A1 * N.z +
        float3(data0.w, data1.w, data2.w) * A1 * N.x;

	ambient = max(irradiance, 0) * INV_PI;
	
	return result;
}

float4 SampleReflectionProbe(float3 positionWS, float3 R, float mip)
{
	float3 ambient;
	return SampleReflectionProbe(positionWS, R, mip, 0, 0, 0, ambient);
}


#endif