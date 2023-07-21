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
	float3 normal = normalize(input.normal);
	float3 view = normalize(-input.positionWS);
	float3 reflection = reflect(-view, normal);
	
	// Remove the exposure the probe was baked with, before applying the current exposure
	ReflectionProbeData probe = _ReflectionProbeData[_Layer];
	float exposureFactor = ApplyExposure(rcp(probe.exposure + (probe.exposure == 0.0))).r;
	return float4(_ReflectionProbes.SampleLevel(_TrilinearClampSampler, float4(reflection, _Layer), 0.0), 1.0) * exposureFactor * _Color;
}