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
	//discard;
	
	float3 N = normalize(input.normal);
	float3 V = normalize(-input.positionWS);
	float3 R = reflect(-V, N);
	
	float4 result = SampleReflectionProbe(input.positionWS, R, 0.0);
	return float4(result.rgb, 1.0);
}