#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Core.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/MatrixUtils.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Packing.hlsl"

Texture2D<float4> _MainTex, _BumpMap;
Texture2D<float3> _ExtraTex, _SubsurfaceTex;
SamplerState _TrilinearRepeatAniso16Sampler;
float _Subsurface;
matrix unity_MatrixVP;

struct VertexInput
{
	float3 position : POSITION;
	float2 uv : TEXCOORD0;
	float3 normal : NORMAL;
	float4 tangent : TANGENT;
	float4 color : COLOR;
};

struct FragmentInput
{
	float4 position : SV_POSITION;
	float2 uv : TEXCOORD0;
	float3 normal : NORMAL;
	float4 tangent : TANGENT;
	float4 color : COLOR;
};

struct FragmentOutput
{
	float4 colorAlpha : SV_Target0;
	float4 normalSmoothness : SV_Target1;
	float depth : SV_Target2;
	float4 subsurfaceOcclusion : SV_Target3;
};

FragmentInput Vertex(VertexInput input)
{
	FragmentInput output;
	output.position = MultiplyPoint(unity_MatrixVP, MultiplyPoint3x4(unity_ObjectToWorld, input.position));
	output.uv = input.uv;
	output.normal = normalize(mul(input.normal, (float3x3) unity_WorldToObject));
	output.tangent = float4(normalize(mul((float3x3) unity_ObjectToWorld, input.tangent.xyz)), input.tangent.w);
	output.color = input.color;
	return output;
}

FragmentOutput Fragment(FragmentInput input, bool isFrontFace : SV_IsFrontFace)
{
	float4 color = _MainTex.Sample(_TrilinearRepeatAniso16Sampler, input.uv);
	
	#ifdef _CUTOUT_ON
	    clip(color.a - 1.0 / 3.0);
    #endif
	
	float3 normal = UnpackNormalAG(_BumpMap.Sample(_TrilinearRepeatAniso16Sampler, input.uv));
	float3 extra = _ExtraTex.Sample(_TrilinearRepeatAniso16Sampler, input.uv);
	float3 translucency = _Subsurface ? _SubsurfaceTex.Sample(_TrilinearRepeatAniso16Sampler, input.uv) : 0.0;
	
	// Flip normal on backsides
	if (!isFrontFace)
		normal.z = -normal.z;
	
	float3 binormal = cross(input.normal, input.tangent.xyz) * (input.tangent.w * unity_WorldTransformParams.w);
	float3x3 tangentToWorld = float3x3(input.tangent.xyz, binormal, input.normal);
	normal = normalize(mul(normal, tangentToWorld));
	
	FragmentOutput output;
	output.colorAlpha = float4(color.rgb, 1.0);
	output.normalSmoothness = float4(normal * 0.5 + 0.5, extra.r);
	output.depth = input.position.z;
	output.subsurfaceOcclusion = float4(translucency, extra.b * input.color.r);
	return output;
}