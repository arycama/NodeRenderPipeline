#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Core.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/MatrixUtils.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Packing.hlsl"

Texture2D<float4> _MainTex, _BumpMap;
Texture2D<float3> _ExtraTex, _SubsurfaceTex;
SamplerState sampler_MainTex;
float _WindEnabled;
uint _RenderArraySliceIndex;

float4x4 _ViewProjectionMatrices[256];
float4x4 unity_MatrixVP;

struct VertexInput
{
	float3 positionOS : POSITION;
	float2 uv : TEXCOORD0;
	float3 normalOS : NORMAL;
	float4 tangentOS : TANGENT;
	float4 color : COLOR;
	uint instanceID : SV_InstanceID;
};

struct FragmentInput
{
	float4 positionCS : SV_POSITION;
	float2 uv : TEXCOORD0;
	float3 normalWS : NORMAL;
	float4 tangentWS : TANGENT;
    float4 color : COLOR;
	uint instanceID : SV_InstanceID;
	
	#ifdef SHADER_STAGE_GEOMETRY
		uint sliceIndex : SV_RenderTargetArrayIndex;
	#endif
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
	output.positionCS = MultiplyPoint(_ViewProjectionMatrices[input.instanceID], MultiplyPoint3x4(unity_ObjectToWorld, input.positionOS));
	output.uv = input.uv;
	output.normalWS = normalize(mul((float3x3) unity_ObjectToWorld, input.normalOS));
	output.tangentWS = float4(normalize(mul((float3x3) unity_ObjectToWorld, input.tangentOS.xyz)), input.tangentOS.w);
	output.color = input.color;
	output.instanceID = input.instanceID;
	return output;
}

[maxvertexcount(3)]
void Geometry(triangle FragmentInput input[3], inout TriangleStream<FragmentInput> stream)
{
	[unroll]
	for (uint i = 0; i < 3;i ++)
	{
		FragmentInput output = input[i];
		
		#ifdef SHADER_STAGE_GEOMETRY
			output.sliceIndex = output.instanceID;
		#endif
		
		stream.Append(output);
	}
}

FragmentOutput Fragment(FragmentInput input, bool isFrontFace : SV_IsFrontFace)
{
    float4 color = _MainTex.Sample(sampler_MainTex, input.uv);
	//color.a = (color.a - 1.0 / 3.0) / max(fwidth(color.a), 0.0001) + 0.5;
	clip(color.a - 1.0 / 3.0);
	
	float3 normal = UnpackNormalAG(_BumpMap.Sample(sampler_MainTex, input.uv));
	float3 extra = _ExtraTex.Sample(sampler_MainTex, input.uv);
	float3 translucency = _SubsurfaceTex.Sample(sampler_MainTex, input.uv);
	
	// Flip normal on backsides
	if (!isFrontFace)
		normal.z = -normal.z;
	
	input.normalWS = normalize(input.normalWS);
	input.tangentWS.xyz = normalize(input.tangentWS.xyz);
	
	float3 binormal = normalize(cross(input.normalWS, input.tangentWS.xyz)) * input.tangentWS.w;
	float3x3 tangentToWorld = float3x3(input.tangentWS.xyz, binormal, input.normalWS);
	input.normalWS = normalize(mul(normal, tangentToWorld));
	
	FragmentOutput output;
	output.colorAlpha = float4(color.rgb, 1.0);
	output.normalSmoothness = float4(input.normalWS * 0.5 + 0.5, extra.r);
	output.depth = input.positionCS.z;
	output.subsurfaceOcclusion = float4(translucency, extra.b * input.color.r);
	return output;
}