#ifndef ATMOSPHERE_INCLUDED
#define ATMOSPHERE_INCLUDED

#include "Geometry.hlsl"
#include "Math.hlsl"
#include "Utility.hlsl"
#include "Volumetric.hlsl"

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
	
	float3 _RayleighScatter;
	float _RayleighHeight, _MieHeight, _MieScatter, _MieAbsorption;
};

float4 _AtmosphereTransmittanceRemap, _AtmosphereMultiScatterRemap;

Texture2D<float3> _AtmosphereTransmittance;
Texture2D<float3> _MultipleScatter;

float2 TransmittanceUv(float viewHeight, float cosAngle)
{
	#if 1
		float H = sqrt(Sq(_TopRadius) - Sq(_PlanetRadius));
		float rho = sqrt(max(0.0, Sq(viewHeight) - Sq(_PlanetRadius)));

		float discriminant = Sq(viewHeight) * (Sq(cosAngle) - 1.0) + Sq(_TopRadius);
		float d = -viewHeight * cosAngle + sqrt(discriminant); // Distance to atmosphere boundary

		float dMin = _TopRadius - viewHeight;
		float dMax = rho + H;
		float x_mu = Remap(d, dMin, dMax);
		float x_r = rho / H;

		return float2(x_mu, x_r) * _AtmosphereTransmittanceRemap.xy + _AtmosphereTransmittanceRemap.zw;
	#else
		float2 uv;
		uv.x = 0.5 * (FastATanPos(max(cosAngle, -0.45) * tan(1.26 * 0.75)) / 0.75 + (1.0 - 0.26));
		uv.y = sqrt((viewHeight - _PlanetRadius) / _AtmosphereHeight);
		return uv * _AtmosphereTransmittanceRemap.xy + _AtmosphereTransmittanceRemap.zw;
	#endif
}

float2 UvToSkyParams(float2 uv)
{
	#if 1
		float x_mu = uv.x;
		float x_r = uv.y;

		float H = sqrt(_TopRadius * _TopRadius - _PlanetRadius * _PlanetRadius);
		float rho = H * x_r;
		float viewHeight = sqrt(rho * rho + _PlanetRadius * _PlanetRadius);

		float d_min = _TopRadius - viewHeight;
		float d_max = rho + H;
		float d = d_min + x_mu * (d_max - d_min);
		float cosAngle = d == 0.0 ? 1.0f : (H * H - rho * rho - d * d) / (2.0 * viewHeight * d);
	
		return float2(cosAngle, viewHeight);
	#else
		float r = uv.y * uv.y * _AtmosphereHeight + _PlanetRadius;
		float mu = tan((2.0 * uv.x - 1.0 + 0.26) * 0.75) / tan(1.26 * 0.75);
		return float2(mu, r);
	#endif
}

bool RayIntersectsGround(float viewHeight, float mu)
{
	return (mu < 0.0) && ((viewHeight * viewHeight * (mu * mu - 1.0) + _PlanetRadius * _PlanetRadius) >= 0.0);
}

float2 RescaledChapman(float2 x, float cosAngle)
{
	x = x / float2(_RayleighHeight, _MieHeight);
	float2 X = _PlanetRadius / float2(_RayleighHeight, _MieHeight);
	
	float2 c = sqrt(x);
	if (cosAngle >= 0.0)
	{
		// cosAngle above horizon
		return c / (c * cosAngle + 1.0) * exp(X - x);
	}
	else
	{
		// cosAngle below horizon, must use identity
		float2 x0 = SinFromCos(cosAngle) * x;
		float2 c0 = sqrt(x0);
		return 2.0 * c0 * exp(X - x0) - c / (1.0 - c * cosAngle) * exp(X - x);
	}
}

float RadAtDist(float r, float rRcp, float cosTheta, float s)
{
	float x2 = 1 + (s * rRcp) * ((s * rRcp) + 2 * cosTheta);

    // Equation 38.
	return r * sqrt(x2);
}

float CosAtDist(float r, float rRcp, float cosTheta, float s)
{
	float x2 = 1 + (s * rRcp) * ((s * rRcp) + 2 * cosTheta);

    // Equation 39.
	return ((s * rRcp) + cosTheta) * rsqrt(x2);
}

// This variant of the function evaluates optical depth along an infinite path.
// 'r' is the radial distance from the center of the planet.
// 'cosTheta' is the value of the dot product of the ray direction and the surface normal.
// seaLvlAtt = (sigma_t * k) is the sea-level (height = 0) attenuation coefficient.
// 'R' is the radius of the planet.
// n = (1 / H) is the falloff exponent, where 'H' is the scale height.
float3 OptDepthSpherExpMedium(float r, float cosTheta)
{
	float2 ch = RescaledChapman(r, cosTheta);
	return ch.x * _RayleighHeight * _RayleighScatter + ch.y * _MieHeight * (_MieScatter + _MieAbsorption);
}

float3 OptDepthSpherExpMedium(float height0, float cosTheta0, float height1, float cosTheta1)
{
    // Potentially swap X and Y.
    // Convention: at the point Y, the ray points up.
	cosTheta0 = (cosTheta1 >= 0) ? cosTheta0 : -cosTheta0;

	float2 zX = height0 / float2(_RayleighHeight, _MieHeight);
	float2 zY = height1 / float2(_RayleighHeight, _MieHeight);
	float2 Z = _PlanetRadius / float2(_RayleighHeight, _MieHeight);

	float2 chX = RescaledChapman(height0, cosTheta0);
	
	float2 c = sqrt(zY);
	float2 chY = c / (c * abs(cosTheta1) + 1.0) * exp(Z - zY); // Rescaling adds 'exp'

    // We may have swapped X and Y.
	float2 ch = abs(chX - chY);

	return ch.x * _RayleighHeight * _RayleighScatter + ch.y * _MieHeight * (_MieScatter + _MieAbsorption);
}

// This variant of the function evaluates optical depth along a bounded path.
float3 OptDepthSpherExpMedium(float r, float cosTheta, float dist)
{
	float rX = r;
	float cosThetaX = cosTheta;
	float rY = RadAtDist(rX, rcp(r), cosThetaX, dist);
	float cosThetaY = CosAtDist(rX, rcp(r), cosThetaX, dist);
	
	return OptDepthSpherExpMedium(r, cosTheta, rY, cosThetaY);
}

float3 TransmittanceToAtmosphere(float viewHeight, float cosAngle, SamplerState samplerState)
{
	return exp(-OptDepthSpherExpMedium(viewHeight, cosAngle));
	
	float2 uv = TransmittanceUv(viewHeight, cosAngle);
	return _AtmosphereTransmittance.SampleLevel(samplerState, uv, 0.0);
}

// P must be relative to planet center
float3 TransmittanceToAtmosphere(float3 P, float3 V, SamplerState samplerState)
{
	float viewHeight = length(P);
	float3 N = P / viewHeight;
	return TransmittanceToAtmosphere(viewHeight, dot(N, V), samplerState);
}

float3 AtmosphereExtinctionNoOzone(float centerDistance)
{
	float4 rayleighMieExtinctionCombined = saturate(exp2(centerDistance * _AtmosphereExtinctionScale + _AtmosphereExtinctionOffset));
	return rayleighMieExtinctionCombined.xyz + rayleighMieExtinctionCombined.w;
}

float3 AtmosphereExtinction(float centerDistance)
{
	float3 ozoneExtinction = max(0.0, _OzoneAbsorption - abs(centerDistance * _AtmosphereOzoneScale + _AtmosphereOzoneOffset));
	return AtmosphereExtinctionNoOzone(centerDistance) + ozoneExtinction;
}

// Returns rayleigh (rgb and mie (a) scatter coefficient
float4 AtmosphereScatter(float centerDistance)
{
	return saturate(exp2(centerDistance * _AtmosphereExtinctionScale + _AtmosphereScatterOffset));
}

// Single scatter albedo for a point on the atmosphere
void AtmosphereAlbedo(float centerDistance, float3 extinction, out float3 rayleighAlbedo, out float3 mieAlbedo)
{
	float4 scatterCombined = saturate(exp2(centerDistance * _AtmosphereExtinctionScale + _AtmosphereScatterOffset));
	rayleighAlbedo = scatterCombined.xyz / extinction;
	mieAlbedo = scatterCombined.w / extinction;
}

// Single scatter albedo for a point on the atmosphere
void AtmosphereAlbedo(float centerDistance, out float3 rayleighAlbedo, out float3 mieAlbedo)
{
	AtmosphereAlbedo(centerDistance, AtmosphereExtinction(centerDistance), rayleighAlbedo, mieAlbedo);
}

float3 TransmittanceToPoint(float height1, float cosAngle1, float height0, float cosAngle0, float dist, SamplerState samplerState)
{
	return exp(-OptDepthSpherExpMedium(height1, cosAngle1, height0, cosAngle0));
	
	if (height0 < height1)
	{
		Swap(height0, height1);
		Swap(cosAngle0, cosAngle1);
		cosAngle0 = -cosAngle0;
		cosAngle1 = -cosAngle1;
	}
	
	float3 transmittance0 = TransmittanceToAtmosphere(height0, cosAngle0, samplerState);
	if (all(transmittance0 == 0.0))
		return 0.0;
	
	float3 transmittance1 = TransmittanceToAtmosphere(height1, cosAngle1, samplerState);
	return transmittance1 / transmittance0;
}

float3 AtmosphereMultiScatter(float viewHeight, float cosAngle, SamplerState samplerState)
{
	float2 uv = TransmittanceUv(viewHeight, cosAngle);
	return _MultipleScatter.SampleLevel(samplerState, uv, 0.0);
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