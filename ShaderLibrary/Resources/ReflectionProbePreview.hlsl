#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Core.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/SpaceTransforms.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/ReflectionProbe.hlsl"

float4 _Color;

struct VertexInput
{
	float3 position : POSITION;
	float3 normal : NORMAL;
	uint instanceID : SV_InstanceID;
};

struct FragmentInput
{
	float4 position : SV_POSITION;
	float3 positionWS : POSITION_1;
	float3 normal : NORMAL;
};

FragmentInput Vertex(VertexInput input)
{
	FragmentInput output;
	output.positionWS = ObjectToWorld(input.position, input.instanceID);
	output.position = WorldToClip(output.positionWS);
	output.normal = input.normal;
	return output;
}

float _Layer;

float4 Fragment(FragmentInput input) : SV_Target
{
	discard;
	
	float3 N = normalize(input.normal);
	float3 V = normalize(-input.positionWS);
	float3 R = reflect(-V, N);
	
	// todo: camera relative
	float3 positionWS = input.positionWS + _WorldSpaceCameraPos;
	
	float4 result = 0.0;
	for (uint i = 0; i < _ReflectionProbeCount; i++)
	{
		ReflectionProbeData probe = _ReflectionProbeData[i];
		float blend = abs(probe.blendDistance);
		float3 localPosition = MultiplyPoint3x4(probe.worldToLocal, positionWS);
		float3 dist = max(0, (1 - abs(localPosition)) / (blend / ((probe.max - probe.min) * 0.5)));
		float weight = Min3(dist);
		
		if (weight <= 0.0)
			continue;
			
		// Box 
		float3 localR = R;
		bool isBox = probe.blendDistance < 0;
		if (isBox)
		{
			float3 localR = MultiplyVector(probe.worldToLocal, R, false);
			float3 factors = ((localR >= 0.0 ? 1.0 : -1.0) - (localPosition)) / localR;
			float scalar = Min3(factors);
			localR = localR * scalar + (localPosition - 0.0);
			localR = MultiplyVector(probe.localToWorld, R, false);
		}
			
		float3 probeSample = _ReflectionProbes.SampleLevel(_TrilinearClampSampler, float4(localR, probe.index), 0.0);
		
		// Remove the exposure the probe was baked with, before applying the current exposure
		float exposureFactor = ApplyExposure(rcp(probe.exposure));
		probeSample *= exposureFactor;
		result += float4(probeSample, 1.0) * weight;
	}
	
	if(result.a <= 0.0)
		return 0.0;
	
	// Normalize
	result.rgb /= result.a;
	return float4(result.rgb, 1.0);
}