#ifndef DEFERRED_WATER_INCLUDED
#define DEFERRED_WATER_INCLUDED

#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/GGXExtensions.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/MaterialUtils.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Brdf.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Deferred.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"

#ifdef __INTELLISENSE__
	#define LIGHT_COUNT_ONE 
	#define LIGHT_COUNT_TWO
#endif

Texture2D<float4> _WaterNormalFoam, _WaterRoughnessMask;
Texture2D<float3> _WaterEmission, _UnderwaterResult;
Texture2D<float> _Depth, _UnderwaterDepth;

Texture2D<float> _UnityFBInput0;

float3 _Extinction, _Color, _LightColor0, _LightDirection0, _LightColor1, _LightDirection1;
float _RefractOffset, _Steps;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	return GetFullScreenTriangleVertexPosition(id);
}

GBufferOut Fragment(float4 positionCS : SV_Position)
{
	float waterDepth = _UnityFBInput0[positionCS.xy];
	float4 waterNormalFoam = _WaterNormalFoam[positionCS.xy];
	
	float3 positionWS = PixelToWorld(positionCS.xy, waterDepth);
	float linearWaterDepth = LinearEyeDepth(waterDepth, _ZBufferParams);
	float distortion = _RefractOffset * _ScreenSize.y * abs(CameraAspect) * 0.25 / linearWaterDepth;
	
	float3 N = UnpackNormalOctQuadEncode(2.0 * Unpack888ToFloat2(waterNormalFoam.xyz) - 1.0);
	float2 uvOffset = N.xz * distortion;
	float2 refractionUv = uvOffset * _ScreenSize.xy + positionCS.xy;
	float2 refractedPositionSS = clamp(refractionUv, 0, _ScreenSize.xy - 1);
	float underwaterDepth = _UnderwaterDepth[refractedPositionSS];
	float underwaterDistance = LinearEyeDepth(underwaterDepth, _ZBufferParams) - linearWaterDepth;

	// Clamp underwater depth if sampling a non-underwater pixel
	if (underwaterDistance < 0.0)
	{
		underwaterDepth = _UnderwaterDepth[positionCS.xy];
		underwaterDistance = max(0.0, LinearEyeDepth(underwaterDepth, _ZBufferParams) - linearWaterDepth);
		refractionUv = positionCS.xy;
	}
	
	float3 V = normalize(positionWS);
	underwaterDistance /= dot(V, _InvViewMatrix._m02_m12_m22);
	
	#if defined(LIGHT_COUNT_ONE) || defined(LIGHT_COUNT_TWO)
		// Slight optimisation, only calculate atmospheric transmittance at surface
		float3 lightColor0 = INV_FOUR_PI * ApplyExposure(_LightColor0) * TransmittanceToAtmosphere(positionWS + _PlanetOffset, _LightDirection0, _LinearClampSampler);
	#endif
	
	#ifdef LIGHT_COUNT_TWO
		// Slight optimisation, only calculate atmospheric transmittance at surface
		float3 lightColor1 = INV_FOUR_PI * ApplyExposure(_LightColor1) * TransmittanceToAtmosphere(positionWS + _PlanetOffset, _LightDirection1, _LinearClampSampler);
	#endif

	float3 scatter = 0.0;
	
	float offset = BlueNoise1D(positionCS.xy);
	for (float i = offset; i < _Steps; i++)
	{
		float xi = i / _Steps;
		float c = Min3(_Extinction);
		float b = underwaterDistance;
		float t = -log(1.0 - xi * (1.0 - exp(-c * b))) / c;
		float weight = exp(c * t) / c - rcp(c * exp(c * (b - t)));
		float3 P = positionWS + V * t;

		#if defined(LIGHT_COUNT_ONE) || defined(LIGHT_COUNT_TWO)
			float attenuation = DirectionalLightShadow(P, 0, 0.5, false);
			if(attenuation > 0.0)
			{
				attenuation *= CloudTransmittanceLevelZero(P);
				if(attenuation > 0.0)
				{
					float shadowDistance0 = max(0.0, positionWS.y - P.y) / max(1e-6, saturate(_LightDirection0.y));
					float3 shadowPosition = MultiplyPoint3x4(_WaterShadowMatrix, P);
					if (all(saturate(shadowPosition) == shadowPosition))
					{
						float shadowDepth = _WaterShadows.SampleLevel(_LinearClampSampler, shadowPosition.xy, 0.0).r;
						shadowDistance0 = saturate(shadowDepth - shadowPosition.z) * _WaterShadowFar;
					}
				
					float3 lightTransmittance = exp(-_Extinction * shadowDistance0);
					float3 viewTransmittance = exp(-_Extinction * t);
				
					float LdotV = dot(_LightDirection0, V);
					float asymmetry = lightTransmittance * viewTransmittance;
				
					float _BackScatter = 0.25;
					float _FrontScatter = 0.65;

					float phase = lerp(CornetteShanksPhaseFunction(-_BackScatter, LdotV) * 2.16, CornetteShanksPhaseFunction(_FrontScatter, LdotV), asymmetry);
				
					phase = 1;
					scatter += phase * lightColor0 * attenuation * exp(-_Extinction * (shadowDistance0 + t)) * weight;
				}
			}
		#endif
		
		#ifdef LIGHT_COUNT_TWO
			float shadowDistance1 = max(0.0, positionWS.y - P.y) / max(1e-6, saturate(_LightDirection1.y));
		scatter += lightColor1 * exp(-_Extinction * (shadowDistance1 + t)) * weight;
		#endif
	}
	
	scatter = scatter / _Steps * _Extinction;
	
	// Ambient 
	float3 finalTransmittance = exp(-underwaterDistance * _Extinction);
	scatter += _AmbientSh[0].xyz * rcp(2.0 * sqrt(PI)) * (1.0 - finalTransmittance);

	float3 waterEmission = scatter * _Color;
	if(underwaterDepth != UNITY_RAW_FAR_CLIP_VALUE)
		waterEmission += _UnderwaterResult[refractionUv] * exp(-_Extinction * underwaterDistance);
	
	// Apply roughness to transmission
	float4 waterRoughnessMask = _WaterRoughnessMask[positionCS.xy];
	float perceptualRoughness = ConvertAnisotropicPerceptualRoughnessToPerceptualRoughness(waterRoughnessMask.gb);
	waterEmission *= (1.0 - waterNormalFoam.a) * GGXDiffuse(1.0, dot(N, -V), perceptualRoughness, 0.04) * PI;

	GBufferOut output;
	output.gBuffer0 = PackGBufferAlbedoTranslucency(waterNormalFoam.a, 0.0, positionCS.xy);
	output.gBuffer1 = float4(waterNormalFoam.xyz, 0.0);
	output.gBuffer2 = waterRoughnessMask;
	output.gBuffer3 = float4(waterNormalFoam.xyz, 1.0);
	output.emission = waterEmission;
	return output;
}

#endif