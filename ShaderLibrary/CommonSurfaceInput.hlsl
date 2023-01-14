// Contains commonly used variables

#ifndef COMMON_SURFACE_INPUT_INCLUDED
#define COMMON_SURFACE_INPUT_INCLUDED

#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/CommonSurfaceConfig.hlsl"

struct VertexInput
{
	uint instanceID : SV_InstanceID;
	VERTEX_POSITION_INPUT
	VERTEX_PREVIOUS_POSITION_INPUT
	VERTEX_UV0_INPUT
	VERTEX_UV1_INPUT
	VERTEX_UV2_INPUT
	VERTEX_UV3_INPUT
	VERTEX_NORMAL_INPUT
	VERTEX_TANGENT_INPUT
	VERTEX_COLOR_INPUT
};

struct VertexData
{
	float3 worldPos;
	float3 worldNormal;
	float4 worldTangent;
	
	float3 positionOS;
	uint instanceID;
	VERTEX_UV0_TYPE uv0;
	VERTEX_UV1_TYPE uv1;
	VERTEX_UV2_TYPE uv2;
	VERTEX_UV3_TYPE uv3;
	float3 previousPositionOS; // Contain previous transform position (in case of skinning for example)
	float3 normal;
	float4 tangent;
	float4 color;
};

struct FragmentInput
{
	uint instanceID : SV_InstanceID;
	
	float4 positionCS : SV_POSITION;
	
	#ifdef MOTION_VECTORS_ON
		float4 nonJitteredPositionCS : POSITION3;
		float4 previousPositionCS : POSITION4;
	#endif
	
	FRAGMENT_WORLD_POSITION_INPUT
	
	FRAGMENT_UV0_INPUT
	FRAGMENT_UV1_INPUT
	FRAGMENT_UV2_INPUT
	FRAGMENT_UV3_INPUT
	
	FRAGMENT_NORMAL_INPUT
	FRAGMENT_TANGENT_INPUT
	
	FRAGMENT_COLOR_INPUT
	
	#ifdef SHADER_STAGE_FRAGMENT
		bool isFrontFace : SV_IsFrontFace;
	#endif
};

struct FragmentData
{
	float4 positionSS;
	bool isFrontFace;
	float3 positionWS;
	float3 viewDirection;
	float3 normal;
	float3 tangent;
	float binormalSign;
	float3 binormal;
	FRAGMENT_UV0_TYPE uv0;
	FRAGMENT_UV1_TYPE uv1;
	FRAGMENT_UV2_TYPE uv2;
	FRAGMENT_UV3_TYPE uv3;
	float4 color;
	uint instanceID;
};

#endif