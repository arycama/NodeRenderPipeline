﻿#ifndef BRDF_INCLUDED
#define BRDF_INCLUDED

#include "AreaLighting.hlsl"
#include "GgxExtensions.hlsl"
#include "Lighting.hlsl"
#include "LightingCommon.hlsl"
#include "Material.hlsl"
#include "Random.hlsl"
#include "ReflectionProbe.hlsl"

struct PbrInput
{
	float3 albedo;
	float3 f0;
	float2 roughness;
	float3 translucency;
	float occlusion;
	float opacity;
	float3 bentNormal;
};

Texture2D<float4> _LtcData;

PbrInput SurfaceDataToPbrInput(SurfaceData surface)
{
	PbrInput output;
	output.f0 = lerp(0.04, surface.Albedo, surface.Metallic);
	output.albedo = ComputeDiffuseColor(surface.Albedo, surface.Metallic);
	output.roughness = Sq(surface.PerceptualRoughness);
	output.translucency = surface.Translucency * surface.Alpha;
	output.occlusion = surface.Occlusion;
	output.opacity = surface.Alpha;
	output.bentNormal = surface.bentNormal;
	return output;
}

// Helper function to return a set of common angle used when evaluating BSDF
// NdotL and NdotV are unclamped
void GetBSDFAngle(float3 V, float3 L, float NdotL, float NdotV,
                  out float LdotV, out float NdotH, out float LdotH, out float invLenLV)
{
    // Optimized math. Ref: PBR Diffuse Lighting for GGX + Smith Microsurfaces (slide 114), assuming |L|=1 and |V|=1
	LdotV = dot(L, V);
	invLenLV = rsqrt(max(2.0 * LdotV + 2.0, FloatEps)); // invLenLV = rcp(length(L + V)), clamp to avoid rsqrt(0) = inf, inf * 0 = NaN
	NdotH = saturate((NdotL + NdotV) * invLenLV);
	LdotH = saturate(invLenLV * LdotV + invLenLV);
}

float DV_SmithJointGGXAniso(float TdotH, float BdotH, float NdotH, float TdotV, float BdotV, float NdotV, float TdotL, float BdotL, float NdotL, float roughnessT, float roughnessB)
{
	// Inline D_GGXAniso() * V_SmithJointGGXAniso() together for better code generation.
	float a2 = roughnessT * roughnessB;
	float3 v = float3(roughnessB * TdotH, roughnessT * BdotH, a2 * NdotH);
	float s = dot(v, v);

	float lambdaV = NdotL * length(float3(roughnessT * TdotV, roughnessB * BdotV, NdotV));
	float lambdaL = NdotV * length(float3(roughnessT * TdotL, roughnessB * BdotL, NdotL));

	float2 D = float2(a2 * a2 * a2, s * s); // Fraction without the multiplier (1/Pi)
	float2 G = float2(1, lambdaV + lambdaL); // Fraction without the multiplier (1/2)

    // This function is only used for direct lighting.
    // If roughness is 0, the probability of hitting a punctual or directional light is also 0.
    // Therefore, we return 0. The most efficient way to do it is with a max().
	return (RcpPi * 0.5) * (D.x * G.x) / max(D.y * G.y, FloatMin);
}

float3 EvaluateLight(PbrInput input, float3 T, float3 B, float3 N, float3 L, float3 V, float3 bentNormal, out float3 illuminance, float NdotV)
{
	float NdotL = dot(N, L);
	bool t = (NdotL > 0.0);

	// Diffuse
	float perceptualRoughness = ConvertAnisotropicRoughnessToPerceptualRoughness(input.roughness);
	illuminance = saturate(NdotL);
	
	float diffuseTerm = GGXDiffuse(saturate(NdotL), NdotV, perceptualRoughness, Max3(input.f0));
	float diffuseTerm1 = GGXDiffuse(saturate(-NdotL), NdotV, perceptualRoughness, Max3(input.f0));

	#ifdef THIN_SURFACE_BSDF
		if (!t)
		{
			L = L + 2 * N * dot(-L, N);
			NdotL = dot(N, L);
			diffuseTerm *= 1.0 - input.opacity;
		}
	#endif
	
	// Impl from Cod WWII, but with bent NdotL
	float microShadow = saturate(Sq(saturate(dot(bentNormal, L)) * rsqrt(saturate(1.0 - input.occlusion))));
	float microShadow1 = saturate(Sq(saturate(dot(-bentNormal, L)) * rsqrt(saturate(1.0 - input.occlusion))));
	
	float3 diffuse = input.albedo * input.opacity * microShadow * diffuseTerm * saturate(NdotL);
	diffuse += input.translucency * microShadow1 * diffuseTerm1 * saturate(-NdotL);

	NdotL = saturate(NdotL);
	float LdotV, NdotH, LdotH, invLenLV;
	GetBSDFAngle(V, L, NdotL, NdotV, LdotV, NdotH, LdotH, invLenLV);
	
	#ifdef REFLECTION_PROBE_RENDERING
		return diffuse;
	#endif

	// Specular
	float3 F = F_Schlick(input.f0, LdotH);
	float3 H = (L + V) * invLenLV;

	// For anisotropy we must not saturate these values
	float TdotV = dot(T, V);
	float TdotH = dot(T, H);
	float TdotL = dot(T, L);

	float BdotV = dot(B, V);
	float BdotH = dot(B, H);
	float BdotL = dot(B, L);

	float DV = DV_SmithJointGGXAniso(TdotH, BdotH, NdotH, TdotV, BdotV, NdotV, TdotL, BdotL, NdotL, input.roughness.x, input.roughness.y);
	float3 ms = GGXMultiScatter(NdotV, NdotL, perceptualRoughness, input.f0);

	#ifdef THIN_SURFACE_BSDF
		if (!t)
		{
			F = 1.0 - F_Schlick(input.f0, LdotH);
			DV *= 1.0 - input.opacity;
			ms *= 1.0 - input.opacity;
		}
	#endif

	return diffuse + (F * DV + ms) * NdotL * microShadow;
}

float3 LtcLight(PbrInput input, float3 positionWS, LightData lightData, bool isLine, float3 N, float NdotV)
{
	float3 V = normalize(-positionWS);

	// Precompute
	float3x3 orthoBasisViewNormal = GetOrthoBasisViewNormal(V, N, NdotV);

	// UVs for sampling the LUTs
	float theta = FastACosPos(NdotV); // For Area light - UVs for sampling the LUTs
	float perceptualRoughness = ConvertAnisotropicRoughnessToPerceptualRoughness(input.roughness);
	float2 ltcUv = Remap01ToHalfTexelCoord(float2(perceptualRoughness, theta * RcpHalfPi), 64);

	float2 ggxE = GGXDirectionalAlbedo(NdotV, perceptualRoughness);
	float3 specularFGD = lerp(ggxE.x, ggxE.y, input.f0);

	// Get the inverse LTC matrix for GGX
	// Note we load the matrix transpose (avoid to have to transpose it in shader)
	float3x3 ltcTransformSpecular = 0.0;
	ltcTransformSpecular._m22 = 1.0;
	ltcTransformSpecular._m00_m02_m11_m20 = _LtcData.SampleLevel(_LinearClampSampler, ltcUv, 0.0);

	// Translate the light s.t. the shaded point is at the origin of the coordinate system.
	lightData.positionWS -= positionWS;

	if (isLine)
	{
		// TODO: some of this could be precomputed.
		// Rotate the endpoints into the local coordinate system.
		float3 P1 = mul(orthoBasisViewNormal, lightData.positionWS - lightData.right * lightData.size.x);
		float3 P2 = mul(orthoBasisViewNormal, lightData.positionWS + lightData.right * lightData.size.x);

		// Compute the binormal in the local coordinate system.
		float3 B = normalize(cross(P1, P2));

		float3 result = LTCEvaluate(P1, P2, B, Identity3x3) * input.albedo * input.opacity;
		
		#ifdef REFLECTION_PROBE_RENDERING
			return result;
		#endif
		
		return result + LTCEvaluate(P1, P2, B, ltcTransformSpecular) * specularFGD * input.f0;
	}
	else
	{
		// TODO: some of this could be precomputed.
		float4x3 lightVerts;
		lightVerts[0] = lightData.positionWS - lightData.right * lightData.size.x - lightData.up * lightData.size.y; // LL
		lightVerts[1] = lightData.positionWS - lightData.right * lightData.size.x + lightData.up * lightData.size.y; // UL
		lightVerts[2] = lightData.positionWS + lightData.right * lightData.size.x + lightData.up * lightData.size.y; // UR
		lightVerts[3] = lightData.positionWS + lightData.right * lightData.size.x - lightData.up * lightData.size.y; // LR

		// Rotate the endpoints into the local coordinate system.
		lightVerts = mul(lightVerts, transpose(orthoBasisViewNormal));

		// Evaluate the diffuse part
		// Polygon irradiance in the transformed configuration.
		float4x3 LD = mul(lightVerts, Identity3x3);

		float3 formFactorD = PolygonFormFactor(LD);
		float3 result = PolygonIrradianceFromVectorFormFactor(formFactorD) * input.albedo * input.opacity;
		
		#ifdef REFLECTION_PROBE_RENDERING
			return result;
		#endif

		// Evaluate the specular part
		// Polygon irradiance in the transformed configuration.
		float4x3 LS = mul(lightVerts, ltcTransformSpecular);
		float3 formFactorS = PolygonFormFactor(LS);
		return result + PolygonIrradianceFromVectorFormFactor(formFactorS) * specularFGD * input.f0;
	}
}

// Ref: "Moving Frostbite to PBR", p. 69.
float3 GetSpecularDominantDir(float3 N, float3 R, float perceptualRoughness, float NdotV)
{
    float p = perceptualRoughness;
    float a = 1.0 - p * p;
    float s = sqrt(a);

#ifdef USE_FB_DSD
    // This is the original formulation.
    float lerpFactor = (s + p * p) * a;
#else
    // TODO: tweak this further to achieve a closer match to the reference.
    float lerpFactor = (s + p * p) * saturate(a * a + lerp(0.0, a, NdotV * NdotV));
#endif

    // The result is not normalized as we fetch in a cubemap
    return lerp(N, R, lerpFactor);
}

float3 GetLighting(float4 positionCS, float3 N, float3 T, PbrInput input, out float3 illuminance, out float3 transmittance)
{
	float3 positionWS = PixelToWorld(positionCS.xyz);

	float3 V = normalize(-positionWS);
	float NdotV;
	input.bentNormal = GetViewReflectedNormal(input.bentNormal, V, NdotV);
	N = GetViewReflectedNormal(N, V, NdotV);
	
	T = Orthonormalize(T, N);

	// Geometry
	float3 B = normalize(cross(N, T));
	float perceptualRoughness = ConvertAnisotropicRoughnessToPerceptualRoughness(input.roughness);
	float3 R = reflect(-V, N);
	
	// Environment lighting
	// Ref https://jcgt.org/published/0008/01/03/
	float2 f_ab = GGXDirectionalAlbedo(NdotV, perceptualRoughness);
	float3 FssEss = lerp(f_ab.x, f_ab.y, input.f0);
	transmittance = FssEss;

	// Multiple scattering
	float Ess = f_ab.y;
	float Ems = 1.0 - Ess;
	float3 Favg = AverageFresnel(input.f0);
	float3 Fms = FssEss * Favg / (1.0 - (1.0 - Ess) * Favg);

	// Dielectrics
	float3 Edss = 1.0 - (FssEss + Fms * Ems);
	float3 kD = input.albedo * input.opacity * Edss;
	float3 bkD = input.translucency * input.opacity * Edss;
	
	float3 ambient = AmbientLight(input.bentNormal, input.occlusion, input.albedo * input.opacity);
	float3 backAmbient = AmbientLight(-input.bentNormal, input.occlusion, input.translucency * input.opacity);
	
	float3 irradiance, backIrradiance;
	
	float3 iblR = GetSpecularDominantDir(N, R, perceptualRoughness, NdotV);
	float iblMipLevel = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
	float4 probe = SampleReflectionProbe(positionWS, iblR, iblMipLevel, input.bentNormal, input.albedo * input.opacity, input.occlusion, irradiance);
	float3 radiance = probe.rgb;
	if (probe.a < 1.0)
	{
		float3 skyRadiance = _SkyReflection.SampleLevel(_TrilinearClampSampler, iblR, iblMipLevel);
		radiance = lerp(skyRadiance, probe.rgb, probe.a);
		irradiance = lerp(ambient, irradiance, probe.a);
		backIrradiance = lerp(backAmbient, irradiance, probe.a);
	}
	else
	{
		backIrradiance = irradiance;
	}

	float specularOcclusion = SpecularOcclusion(dot(N, R), perceptualRoughness, input.occlusion, dot(input.bentNormal, R));
	radiance *= specularOcclusion;
	
	#ifdef SCREENSPACE_REFLECTIONS_ON
		float4 ssr = _ReflectionBuffer[positionCS.xy];
		radiance = lerp(radiance, ssr.rgb, ssr.a);
	#endif
	
	// Ambient
	illuminance = irradiance;
	float3 luminance = FssEss * radiance + Fms * Ems * irradiance + (kD * irradiance + bkD * backIrradiance);
	
	#ifdef REFLECTION_PROBE_RENDERING
		luminance = kD * irradiance;
		luminance = 0.0;
	#endif
	
	float jitter = InterleavedGradientNoise(positionCS.xy, _FrameIndex);
	uint i;
	for (i = 0; i < _DirectionalLightCount; i++)
	{
		DirectionalLightData lightData = _DirectionalLightData[i];
		float3 lightColor = DirectionalLightColor(i, positionWS, true, jitter);
		if (all(lightColor) == 0.0)
			continue;
		
		float3 L = DiscLightApprox(lightData.AngularDiameter, R, lightData.Direction);
		float3 illum;
		luminance += EvaluateLight(input, T, B, N, L, V, input.bentNormal, illum, NdotV) * lightColor;
		illuminance += illum * lightColor;
	}
	
	#ifndef REFLECTION_PROBE_RENDERING
		uint3 clusterIndex;
		clusterIndex.xy = floor(positionCS.xy) / _TileSize;
		clusterIndex.z = log2(positionCS.w) * _ClusterScale + _ClusterBias;

		uint2 lightOffsetAndCount = _LightClusterIndices[clusterIndex];
		uint startOffset = lightOffsetAndCount.x;
		uint lightCount = lightOffsetAndCount.y;

		// Would it be better to combine this with the above, so we're only calling evaluate light once?
		for (i = 0; i < lightCount; i++)
		{
			int index = _LightClusterList[startOffset + i];
			LightData lightData = _LightData[index];
			LightCommon light = GetLightColor(lightData, positionWS, jitter, true);

			// Handle different lights
			float3 illum = 0;
			switch (lightData.lightType)
			{
				//case 0: // Directional
				case 1: // Spot
				case 2: // Point
				case 3: // Pyramid
				case 4: // Box
				{
						luminance += EvaluateLight(input, T, B, N, light.direction, V, input.bentNormal, illum, NdotV) * light.color;
						break;
					}
				case 5: // Tube
				case 6: // Rectangle
					luminance += LtcLight(input, positionWS, lightData, lightData.lightType == 5, N, NdotV) * light.color;
					break;
			}
		
			illuminance += illum * light.color;
		}
	#endif
	
	return luminance;
}

float3 GetLighting(float4 positionCS, float3 N, float3 T, inout PbrInput input)
{
	float3 illuminance, transmittance;
	return GetLighting(positionCS, N, T, input, illuminance, transmittance);
}

#endif