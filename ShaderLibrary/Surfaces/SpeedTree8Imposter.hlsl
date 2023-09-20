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
	float3 screenRay : TEXCOORD0;
	float4 uvWeights[3] : TEXCOORD1;
	float4 viewDirTS[3] : TEXCOORD4;
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
SamplerState _TrilinearClampAniso16Sampler;

cbuffer UnityPerMaterial
{
	float4 _Scale;
	float3 _CenterOffset;
	float _Cutoff, _ImposterFrames, _FramesMinusOne, _RcpFramesMinusOne;
};

static const float4 _HueVariationColor = float4(0.5, 0.3, 0.1, 0.3);

//float3 IntersectRayPlane(float3 rayOrigin, float3 rayDirection, float3 planeOrigin, float3 planeNormal, out float dist)
//{
//	dist = dot(planeNormal, planeOrigin - rayOrigin) / dot(planeNormal, rayDirection);
//	return rayOrigin + rayDirection * dist;
//}

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
	float3 weights = float3(min(1.0 - localUv, localUv.yx), abs(localUv.x + localUv.y - 1.0)).xzy;
	float2 offsets[3] = { float2(0, 1), mask, float2(1, 0) };

	float depths = 0.0;
	float4 color = 0.0, normalSmoothness = 0.0, subsurfaceOcclusion = 0.0;
	
	[unroll]
	for (uint i = 0; i < 3; i++)
	{
		float2 localCell = cell + offsets[i];
		float2 f = 2.0 * (localCell * _RcpFramesMinusOne) - 1.0;
	
		float2 val = float2(f.x + f.y, f.x - f.y) * 0.5;
		float3 normal = normalize(float3(val.x, 1.0 - dot(abs(val), 1.0), val.y));
		float3 tangent = abs(normal.y) == 1 ? float3(normal.y, 0, 0) : normalize(cross(normal, float3(0, 1, 0)));
		float3 bitangent = cross(tangent, normal);
	
		float3x3 objectToTangent = float3x3(tangent, bitangent, normal);
		float3 rayOriginTS = mul(objectToTangent, input.position);
		float3 rayDirectionTS = mul(objectToTangent, viewDirOS);
		
		//float dist;
		//float3 hit = IntersectRayPlane(rayOriginTS, rayDirectionTS, float3(0.0, 0.0, 0.0), float3(0.0, 0.0, -1.0), dist);
		//float3 planeNormal = float3(0, 0, -0.5);
		////float3 planeOrigin = planeNormal * 0.5;
		
		//dist = (-0.5 - rayOrigin.z) / (rayDirection.z * 0.5);
		//return rayOrigin + rayDirection * dist;
		
		output.uvWeights[i].z = localCell.y * _ImposterFrames + localCell.x;
		output.uvWeights[i].w = weights[i];
		
		output.uvWeights[i].xy = rayOriginTS.xy + 0.5;
		output.viewDirTS[i].xy = rayDirectionTS.xy;
		output.viewDirTS[i].z = rayDirectionTS.z;
		output.viewDirTS[i].w = rayOriginTS.z;
	}
	
	float3 worldRay = ObjectToWorldDir(viewDirOS, input.instanceID, false);
	float4 clipRay = mul(_ViewProjMatrix, float4(worldRay, 0.0));
	output.screenRay.xy = clipRay.zw;
	
	float3 treePos = MultiplyPoint3x4(GetObjectToWorld(input.instanceID, false), -_CenterOffset / _Scale.w);
	float hueVariationAmount = frac(treePos.x + treePos.y + treePos.z);
	output.screenRay.z = saturate(hueVariationAmount * _HueVariationColor.a); 
	
	return output;
}

FragmentOutput Fragment(FragmentInput input)
{
	#ifdef LOD_FADE_CROSSFADE
		float dither = InterleavedGradientNoise(input.positionCS.xy, 0);
		float fade = GetLodFade(input.instanceID).x;
		clip(fade + (fade < 0.0 ? dither : -dither));
	#endif

	float depths = 0.0;
	float4 color = 0.0, normalSmoothness = 0.0, subsurfaceOcclusion = 0.0;
	
	[unroll]
	for (uint i = 0; i < 3; i++)
	{
		float3 uv = input.uvWeights[i].xyz;
		uv.xy -= input.viewDirTS[i].w * input.viewDirTS[i].xy / input.viewDirTS[i].z;
		
		uv.xy = UnjitterTextureUV(uv.xy);
		float height = _ParallaxMap.Sample(_TrilinearClampAniso16Sampler, uv) - 0.5;
		
		uv.xy += input.viewDirTS[i].xy / input.viewDirTS[i].z * height;
		
		float weight = input.uvWeights[i].w;
		depths += (height - input.viewDirTS[i].w) / input.viewDirTS[i].z * weight;
		color += _MainTex.Sample(_TrilinearClampAniso16Sampler, uv) * weight;
		normalSmoothness += _NormalSmoothness.Sample(_TrilinearClampAniso16Sampler, uv) * weight;
		subsurfaceOcclusion += _SubsurfaceOcclusion.Sample(_TrilinearClampAniso16Sampler, uv) * weight;
	}

	clip(color.a - _Cutoff);
		
	FragmentOutput output;
	
	float depth = (input.screenRay.x * depths + input.positionCS.z * input.positionCS.w) * rcp(input.screenRay.y * depths + input.positionCS.w);
	output.depth = depth;
	
#ifndef UNITY_PASS_SHADOWCASTER
	SurfaceData surface = DefaultSurface();
	surface.Albedo = color.rgb;
	surface.Normal = surface.bentNormal = ObjectToWorldNormal(normalSmoothness.rgb * 2 - 1, input.instanceID, true);
	surface.PerceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(normalSmoothness.a);
	surface.Translucency = subsurfaceOcclusion.rgb;
	surface.Occlusion = subsurfaceOcclusion.a;
	
	// Hue varation
	float3 shiftedColor = lerp(surface.Albedo, _HueVariationColor.rgb, input.screenRay.z);
	surface.Albedo = saturate(shiftedColor * (Max3(surface.Albedo) / Max3(shiftedColor) * 0.5 + 0.5));
	
	shiftedColor = lerp(surface.Translucency, _HueVariationColor.rgb, input.screenRay.z);
	surface.Translucency = saturate(shiftedColor * (Max3(surface.Translucency) / Max3(shiftedColor) * 0.5 + 0.5));
	
	output.gbufferOut = SurfaceToGBuffer(surface, input.positionCS.xy);
#endif
	
	return output;
}