#define REQUIRES_VERTEX_POSITION

#ifdef __INTELLISENSE__
	#define TERRAIN_BLENDING_ON
	#define _PARALLAXMAP
#endif

#ifdef UNITY_PASS_SHADOWCASTER
	#ifdef MODE_CUTOUT
		#define REQUIRES_VERTEX_UV0
		#define REQUIRES_FRAGMENT_UV0
	#endif
#else
	#define REQUIRES_VERTEX_UV0
	#define REQUIRES_VERTEX_NORMAL
	#define REQUIRES_VERTEX_TANGENT

	#define REQUIRES_FRAGMENT_UV0
	#define REQUIRES_FRAGMENT_NORMAL
	#define REQUIRES_FRAGMENT_TANGENT
#endif

#ifdef _PARALLAXMAP
	#define REQUIRES_FRAGMENT_UV1
	#define VERTEX_UV1_TYPE float3
	#define FRAGMENT_UV1_TYPE float3
#endif

#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/CommonSurface.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/GGXExtensions.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/IndirectRendering.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Packing.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Terrain.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Utility.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/VirtualTexturing.hlsl"

Texture2D<float4> _BentNormal, _MainTex, _BumpMap, _MetallicGlossMap, _DetailAlbedoMap, _DetailNormalMap, _EmissionMap, _OcclusionMap, _ParallaxMap;
Texture2D<float> _AnisotropyMap;
SamplerState sampler_MainTex;

cbuffer UnityPerMaterial
{
	float4 _DetailAlbedoMap_ST, _MainTex_ST;
	float4 _EmissionColor, _Color;
	float _BumpScale, _Cutoff, _DetailNormalMapScale, _Metallic, _Smoothness;
	float _HeightBlend, _NormalBlend;
	float BentNormal, _EmissiveExposureWeight;
	float _Anisotropy;
	float Smoothness_Source;
	float _Parallax;
	float Terrain_Blending;
	float Blurry_Refractions;
	float Anisotropy;
};

void vert(inout VertexData data)
{
	#ifdef _PARALLAXMAP
		float4 tangentWS = data.worldTangent;
		tangentWS.w *= GetOddNegativeScale();
	
		float3 viewDirWS = normalize(-data.worldPos);
		data.uv1 = GetViewDirectionTangentSpace(tangentWS, data.normal, viewDirWS);
	#endif
}

void surf(inout FragmentData input, inout SurfaceData surface)
{
	input.uv0.xy = UnjitterTextureUV(input.uv0.xy);
	
	#ifdef _PARALLAXMAP
		float height = _ParallaxMap.Sample(sampler_MainTex, ApplyScaleOffset(input.uv0, _MainTex_ST)).b;
		input.uv0.xy += ParallaxOffset1Step(height, _Parallax, input.uv1);
	#endif

	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(MODE_CUTOUT) || defined(MODE_FADE)|| defined(MODE_TRANSPARENT)
		float2 uv = ApplyScaleOffset(input.uv0, _MainTex_ST);
		float2 detailUv = ApplyScaleOffset(input.uv0, _DetailAlbedoMap_ST);

		float4 albedo = _MainTex.Sample(sampler_MainTex, uv);
		float4 detail = _DetailAlbedoMap.Sample(sampler_MainTex, detailUv);
		albedo.rgb *= detail.rgb * 2;

		surface.Albedo = albedo.rgb * _Color.rgb;
		surface.Alpha = albedo.a * _Color.a;

		#ifdef MODE_CUTOUT
			clip(surface.Alpha - _Cutoff);
		#endif
	#endif

	#ifdef UNITY_PASS_SHADOWCASTER
		return;
	#else
		#ifdef SHADER_STAGE_FRAGMENT
			input.normal *= input.isFrontFace ? 1 : - 1;
		#endif

		surface.Normal = UnpackNormalAG(_BumpMap.Sample(sampler_MainTex, uv), _BumpScale);

		// Detail Normal Map
		float3 detailNormalTangent = UnpackNormalAG(_DetailNormalMap.Sample(sampler_MainTex, detailUv), _DetailNormalMapScale);
		surface.Normal = BlendNormalRNM(surface.Normal, detailNormalTangent);

		float4 metallicGloss = _MetallicGlossMap.Sample(sampler_MainTex, uv);
		surface.Metallic = metallicGloss.r * _Metallic;

		float anisotropy = Anisotropy ? _AnisotropyMap.Sample(sampler_MainTex, uv).r : _Anisotropy;
		anisotropy = 2 * anisotropy - 1;
		surface.tangentWS = input.tangent;

		float perceptualSmoothness;
		if (Smoothness_Source)
			perceptualSmoothness = albedo.a * _Smoothness;
		else
			perceptualSmoothness = metallicGloss.a * _Smoothness;

		//perceptualSmoothness = GeometricNormalFiltering(perceptualSmoothness, input.normal,  0.25, 0.25);
		float roughness = PerceptualSmoothnessToPerceptualRoughness(perceptualSmoothness);

		// convert pRoughness/aniso to pRoughnessT/B
		surface.PerceptualRoughness = roughness * sqrt(1 + anisotropy * float2(1, -1));

		float3 emission = _EmissionMap.Sample(sampler_MainTex, uv).rgb * _EmissionColor.rgb;
		surface.Emission = lerp(ApplyExposure(emission), (emission), _EmissiveExposureWeight);

		// Occlusion, no keyword?
		surface.Occlusion = _OcclusionMap.Sample(sampler_MainTex, uv).g;
		float3 V = normalize(-input.positionWS);

		// Gross terrain blend stuff, will be improved later
		// Could we do this via gbuffer and stencil instead?
		if (Terrain_Blending)
		{
			// Calculate blending factors


			// Normal blending factor
			float3 terrainNormalWS = GetTerrainNormal(input.positionWS);

			// Use angle between world normal and terrain normal to blend a height factor. TODO: Optimize
			float terrainHeight = GetTerrainHeight(input.positionWS);
			float blend = saturate(1.0 - (abs(input.positionWS.y - terrainHeight) * (1.0 - _HeightBlend) / (_HeightBlend * saturate(dot(input.normal, terrainNormalWS)) * _NormalBlend)));
			if (blend > 0)
			{
				float2 terrainUv = WorldToTerrainPosition(input.positionWS);
				float3 virtualUv = CalculateVirtualUv(terrainUv);

				float4 albedoSmoothness = _VirtualTexture.Sample(sampler_VirtualTexture, virtualUv);
				float4 normalMetalOcclusion = _VirtualNormalTexture.Sample(sampler_VirtualNormalTexture, virtualUv);

				float3 virtualNormalMap;
				virtualNormalMap.xy = normalMetalOcclusion.ag * 2 - 1;
				virtualNormalMap.z = sqrt(1 - saturate(dot(virtualNormalMap.xy, virtualNormalMap.xy)));

				float3x3 tangentToWorld = float3x3(input.tangent, input.binormal, input.normal);
				float3 normalWS = mul(surface.Normal, tangentToWorld);

				input.normal = normalize(lerp(normalWS, virtualNormalMap.xzy, blend));
				surface.Normal = float3(0, 0, 1);

				surface.Albedo = lerp(surface.Albedo, albedoSmoothness.rgb, blend);
				surface.PerceptualRoughness = lerp(surface.PerceptualRoughness, PerceptualSmoothnessToPerceptualRoughness(albedoSmoothness.a), blend);
				surface.Metallic = lerp(surface.Metallic, normalMetalOcclusion.r, blend);
				surface.Occlusion = lerp(surface.Occlusion, normalMetalOcclusion.b, blend);
			}
		}

		// Extract some additional data from input structure + surface function result
		float3x3 tangentToWorld = float3x3(input.tangent, input.binormal, input.normal);
		float3 N = normalize(mul(surface.Normal, tangentToWorld));
		surface.bentNormal = BentNormal ? UnpackNormalAG(_BentNormal.Sample(sampler_MainTex, uv)) : surface.Normal;
		surface.blurryRefractions = Blurry_Refractions;
	#endif
}