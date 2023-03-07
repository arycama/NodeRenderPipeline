#ifndef ATMOSPHERE_INCLUDED
#define ATMOSPHERE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GeometricTools.hlsl"

cbuffer AtmosphereProperties
{
	float4 _AtmosphereExtinctionScale, _AtmosphereExtinctionOffset, _AtmosphereScatterOffset;
	float3 _AtmosphereOzoneScale;
	float _PlanetRadius;
	float3 _AtmosphereOzoneOffset;
	float _AtmosphereHeight;
	float3 _OzoneAbsorption;
	float _TopRadius;

	float _MiePhase;
	float _MiePhaseConstant;
	float _OzoneWidth;
	float _OzoneHeight;
	
	float3 _PlanetOffset;
	float _AtmospherePropertiesPad;
};

Texture2D<float3> _AtmosphereTransmittance;
Texture2D<float3> _MultipleScatter;

float2 TransmittanceUv(float viewHeight, float viewZenithCosAngle)
{
	float2 uv;
	uv.x = 0.5 * (FastATanPos(max(viewZenithCosAngle, -0.45) * tan(1.26 * 0.75)) / 0.75 + (1.0 - 0.26));
	uv.y = sqrt((viewHeight - _PlanetRadius) / _AtmosphereHeight);
	return uv;
}

float2 UvToSkyParams(float2 uv)
{
	float r = uv.y * uv.y * _AtmosphereHeight + _PlanetRadius;
	float mu = tan((2.0 * uv.x - 1.0 + 0.26) * 0.75) / tan(1.26 * 0.75);
	return float2(mu, r);
}

float3 TransmittanceToAtmosphere(float viewHeight, float viewZenithCosAngle, SamplerState samplerState)
{
	float2 uv = TransmittanceUv(viewHeight, viewZenithCosAngle);
	return _AtmosphereTransmittance.SampleLevel(samplerState, uv, 0.0);
}

// P must be relative to planet center
float3 TransmittanceToAtmosphere(float3 P, float3 V, SamplerState samplerState)
{
	float viewHeight = length(P);
	float3 N = P / max(1e-6, viewHeight);
	return TransmittanceToAtmosphere(viewHeight, dot(N, V), samplerState);
}

float3 AtmosphereMultiScatter(float viewHeight, float viewZenithCosAngle, SamplerState samplerState)
{
	float2 uv = TransmittanceUv(viewHeight, viewZenithCosAngle);
	return _MultipleScatter.SampleLevel(samplerState, uv, 0.0) * IsotropicPhaseFunction();
}

struct AtmosphereResult
{
	float3 transmittance, luminance, multiScatter;
	bool hasPlanetHit;
};

float4 AtmosphereTransmittance(float centerDistance)
{
	return saturate(exp2(centerDistance * _AtmosphereExtinctionScale + _AtmosphereExtinctionOffset));
}

// Returns rayleigh (rgb and mie (a) scatter coefficient
float4 AtmosphereScatter(float centerDistance)
{
	return saturate(exp2(centerDistance * _AtmosphereExtinctionScale + _AtmosphereScatterOffset));
}

float3 AtmosphereOpticalDepth(float centerDistance)
{
	float4 opticalDepthSumExtinction = AtmosphereTransmittance(centerDistance);
	float3 ozone = max(0.0, _OzoneAbsorption - abs(centerDistance * _AtmosphereOzoneScale + _AtmosphereOzoneOffset));
	return opticalDepthSumExtinction.xyz + opticalDepthSumExtinction.w + ozone;
}

float3 AtmosphereLight(float3 P, float3 V, float3 L, SamplerState samplerState)
{
	// Single scatter, with earth shadow 
	float2 intersections;
	if (IntersectRaySphere(P, L, _PlanetRadius, intersections) && intersections.x >= 0.0)
		return 0.0;
	
	float angle = dot(V, L);
	float4 atmosphereScatter = AtmosphereScatter(length(P));
	float3 scatterColor = atmosphereScatter.xyz * RayleighPhaseFunction(angle);
	scatterColor += atmosphereScatter.w * CornetteShanksPhasePartVarying(_MiePhase, angle) * _MiePhaseConstant;
		
	return scatterColor * TransmittanceToAtmosphere(P, L, samplerState);
}

float3 AtmosphereLightFull(float3 P, float3 V, float3 L, SamplerState samplerState, float attenuation)
{
	float3 lighting = AtmosphereLight(P, V, L, samplerState) * attenuation;
	
	float3 N = normalize(P);
	float3 ms = AtmosphereMultiScatter(length(P), dot(N, L), samplerState);
	float4 atmosphereScatter = AtmosphereScatter(length(P));
	lighting += ms * (atmosphereScatter.xyz + atmosphereScatter.w);
	
	return lighting;
}

#endif