#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Deferred.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Geometry.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/ImposterCommon.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/IndirectRendering.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Random.hlsl"

struct VertexInput
{
	float3 position : POSITION;
	uint instanceID : SV_InstanceID;
};

struct FragmentInput
{
	linear centroid float4 positionCS : SV_Position;
	uint instanceID : SV_InstanceID;
	float4 uvWeights[3] : TEXCOORD1;
	float4 viewDirTS[3] : TEXCOORD4;
	
#ifndef UNITY_PASS_SHADOWCASTER
	float hueVariation : TEXCOORD0;
#endif
};

struct FragmentOutput
{
#ifndef UNITY_PASS_SHADOWCASTER
	GBufferOut gbufferOut;
#endif
	
	float depth : SV_DepthLessEqual;
};

Texture2DArray<float4> _MainTex, _NormalSmoothness, _SubsurfaceOcclusion;
Texture2DArray<float> _ParallaxMap;
SamplerState _TrilinearClampAniso4Sampler;

cbuffer UnityPerMaterial
{
	float3 _WorldOffset;
	float _Cutoff, _ImposterFrames, _FramesMinusOne, _RcpFramesMinusOne;
};

static const float4 _HueVariationColor = float4(0.7, 0.25, 0.1, 0.2);

FragmentInput Vertex(VertexInput input)
{
	float3 worldPosition = ObjectToWorld(input.position, input.instanceID);
	
	FragmentInput output;
	output.instanceID = input.instanceID;
	output.positionCS = WorldToClip(worldPosition);
	
#ifdef UNITY_PASS_SHADOWCASTER
		float3 viewDirOS = WorldToObjectDir(-_ViewMatrix[2].xyz, input.instanceID);
		float3 view = viewDirOS;
#else
	float3 view = WorldToObject(0.0, input.instanceID);
	float3 viewDirOS = view - input.position;
#endif
	
	float l1norm = dot(abs(view), 1.0);
	float2 res = view.xz * rcp(l1norm);
	float2 result = float2(res.x + res.y, res.x - res.y);
	
	float2 atlasUv = (0.5 * result + 0.5) * _FramesMinusOne;
	float2 cell = floor(atlasUv);
	float2 localUv = frac(atlasUv);
	
	float2 mask = (localUv.x + localUv.y) > 1.0;
	float2 offsets[3] = { float2(0, 1), mask, float2(1, 0) };
	float3 weights = float3(min(1.0 - localUv, localUv.yx), abs(localUv.x + localUv.y - 1.0)).xzy;

	for (uint i = 0; i < 3; i++)
	{
		float2 localCell = cell + offsets[i];
		float2 f = 2.0 * (localCell * _RcpFramesMinusOne) - 1.0;
	
		float2 val = float2(f.x + f.y, f.x - f.y) * 0.5;
		float3 normal = normalize(float3(val.x, 1.0 - dot(abs(val), 1.0), val.y));
		float3 tangent = abs(normal.y) == 1 ? float3(normal.y, 0, 0) : normalize(cross(normal, float3(0, 1, 0)));
		float3 bitangent = cross(tangent, normal);
		float3x3 objectToTangent = float3x3(tangent, bitangent, normal);
		
		float3 rayOrigin = mul(objectToTangent, input.position);
		output.uvWeights[i] = float4(rayOrigin.xy + 0.5, localCell.y * _ImposterFrames + localCell.x, weights[i]);
		
		float3 rayDirection = mul(objectToTangent, viewDirOS);
		output.viewDirTS[i] = float4(rayDirection, rayOrigin.z);
	}
	
#ifndef UNITY_PASS_SHADOWCASTER
	float3 treePos = MultiplyPoint3x4(GetObjectToWorld(input.instanceID, false), _WorldOffset);
	float hueVariationAmount = frac(treePos.x + treePos.y + treePos.z);
	output.hueVariation = saturate(hueVariationAmount * _HueVariationColor.a);
#endif
	
	return output;
}

FragmentOutput Fragment(FragmentInput input)
{
#ifdef LOD_FADE_CROSSFADE
	float dither = InterleavedGradientNoise(input.positionCS.xy, 0);
	float fade = GetLodFade(input.instanceID).x;
	clip(fade + (fade < 0.0 ? dither : -dither));
#endif

	float4 color = 0.0, normalSmoothness = 0.0, subsurfaceOcclusion = 0.0;
	float depth = 0.0;
	
	for (uint i = 0; i < 3; i++)
	{
		float3 uv = input.uvWeights[i].xyz;
		uv.xy -= input.viewDirTS[i].xy * rcp(input.viewDirTS[i].z) * input.viewDirTS[i].w;
		
		float height = _ParallaxMap.Sample(_TrilinearClampAniso4Sampler, uv) - 0.5;
		uv.xy += input.viewDirTS[i].xy * rcp(input.viewDirTS[i].z) * height;
		if (any(saturate(uv.xy) != uv.xy))
			continue;
		
		color += _MainTex.Sample(_TrilinearClampAniso4Sampler, uv) * input.uvWeights[i].w;
		normalSmoothness += _NormalSmoothness.Sample(_TrilinearClampAniso4Sampler, uv) * input.uvWeights[i].w;
		subsurfaceOcclusion += _SubsurfaceOcclusion.Sample(_TrilinearClampAniso4Sampler, uv) * input.uvWeights[i].w;
		
		depth += (height - input.viewDirTS[i].w) * rcp(input.viewDirTS[i].z) * input.uvWeights[i].w;
	}

	clip(color.a - _Cutoff);
	
	FragmentOutput output;
	
#ifdef UNITY_PASS_SHADOWCASTER
	output.depth = -_ShadowProjMatrix._m22 * depth + input.positionCS.z;
#else
	output.depth = (-_ProjMatrix._m22 * depth + input.positionCS.z) * rcp(1.0 - depth);
	
	SurfaceData surface = DefaultSurface();
	surface.Albedo = color.rgb;
	surface.Normal = surface.bentNormal = ObjectToWorldNormal(normalSmoothness.rgb * 2 - 1, input.instanceID, true);
	surface.PerceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(normalSmoothness.a);
	surface.Translucency = subsurfaceOcclusion.rgb;
	surface.Occlusion = subsurfaceOcclusion.a;
	
	// Hue varation
	float3 shiftedColor = lerp(surface.Albedo, _HueVariationColor.rgb, input.hueVariation);
	surface.Albedo = saturate(shiftedColor * (Max3(surface.Albedo) * rcp(Max3(shiftedColor)) * 0.5 + 0.5));
	
	shiftedColor = lerp(surface.Translucency, _HueVariationColor.rgb, input.hueVariation);
	surface.Translucency = saturate(shiftedColor * (Max3(surface.Translucency) * rcp(Max3(shiftedColor)) * 0.5 + 0.5));
	
	output.gbufferOut = SurfaceToGBuffer(surface, input.positionCS.xy);
#endif
	
	return output;
}