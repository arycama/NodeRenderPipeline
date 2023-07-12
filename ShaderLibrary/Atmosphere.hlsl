#ifndef ATMOSPHERE_INCLUDED
#define ATMOSPHERE_INCLUDED

#include "Geometry.hlsl"
#include "Math.hlsl"
#include "Utility.hlsl"
#include "Volumetric.hlsl"

float _PlanetRadius;
float _AtmosphereHeight;
float3 _OzoneAbsorption;
float _TopRadius;

float _MiePhase;
float _OzoneWidth;
float _OzoneHeight;
	
float3 _PlanetOffset;
float _AtmospherePropertiesPad;
	
float3 _RayleighScatter;
float _RayleighHeight, _MieHeight, _MieScatter, _MieAbsorption;
float4 _AtmosphereTransmittanceRemap, _AtmosphereMultiScatterRemap, _AtmosphereAmbientRemap;

Texture2D<float3> _AtmosphereTransmittance;
Texture2D<float3> _MultipleScatter;

float DistanceToTopAtmosphereBoundary(float radius, float cosAngle)
{
	float discriminant = Sq(radius) * (Sq(cosAngle) - 1.0) + Sq(_TopRadius);
	return max(0.0, -radius * cosAngle + sqrt(max(0.0, discriminant)));
}

float DistanceToBottomAtmosphereBoundary(float radius, float cosAngle)
{
	float discriminant = Sq(radius) * (Sq(cosAngle) - 1.0) + Sq(_PlanetRadius);
	return max(0.0, (-radius * cosAngle - sqrt(max(0.0, discriminant))));
}

float DistanceToNearestAtmosphereBoundary(float radius, float cosAngle, bool ray_r_mu_intersects_ground)
{
	if (ray_r_mu_intersects_ground)
	{
		return DistanceToBottomAtmosphereBoundary(radius, cosAngle);
	}
	else
	{
		return DistanceToTopAtmosphereBoundary(radius, cosAngle);
	}
}

bool RayIntersectsGround(float viewHeight, float cosAngle)
{
	return (cosAngle < 0.0) && (Sq(viewHeight) * (Sq(cosAngle) - 1.0) + Sq(_PlanetRadius)) >= 0.0;
}

float2 TransmittanceUv(float viewHeight, float cosAngle)
{
	// Distance to top atmosphere boundary for a horizontal ray at ground level.
	float H = sqrt(Sq(_TopRadius) - Sq(_PlanetRadius));
	
	// Distance to the horizon.
	float rho = sqrt(max(0.0, Sq(viewHeight) - Sq(_PlanetRadius)));
	
	// Distance to the top atmosphere boundary for the ray (r,mu), and its minimum
	// and maximum values over all mu - obtained for (r,1) and (r,mu_horizon).
	float d = DistanceToTopAtmosphereBoundary(viewHeight, cosAngle);
	float dMin = max(0.0, _TopRadius - viewHeight);
	float dMax = rho + H;
	float x_mu = Remap(d, dMin, dMax);
	float x_r = rho / H;
	return float2(x_mu, x_r) * _AtmosphereTransmittanceRemap.xy + _AtmosphereTransmittanceRemap.zw;
}

void UvToSkyParams(float2 uv, out float height, out float cosAngle, out float d)
{
	// Distance to top atmosphere boundary for a horizontal ray at ground level.
	float H = sqrt(Sq(_TopRadius) - Sq(_PlanetRadius));
	
	// Distance to the horizon, from which we can compute r:
	float rho = H * uv.y;
	height = sqrt(Sq(rho) + Sq(_PlanetRadius));
	
	// Distance to the top atmosphere boundary for the ray (r,mu), and its minimum
	// and maximum values over all mu - obtained for (r,1) and (r,mu_horizon) -
	// from which we can recover mu:
	float d_min = _TopRadius - height;
	float d_max = rho + H;
	d = Remap(uv.x, 0.0, 1.0, d_min, d_max);
	cosAngle = d == 0.0 ? 1.0 : (Sq(H) - Sq(rho) - Sq(d)) / (2.0 * height * d);
	cosAngle = clamp(cosAngle, -1.0, 1.0);
}

// Calculates the height above the atmosphere based on the current view height, angle and distance
float HeightAtDistance(float viewHeight, float cosAngle, float distance)
{
	return sqrt(Sq(distance) + 2.0 * viewHeight * cosAngle * distance + Sq(viewHeight));
}

float CosAngleAtDistance(float viewHeight, float cosAngle, float distance, float heightAtDistance)
{
	return (viewHeight * cosAngle + distance) / heightAtDistance;
}

float CosAngleAtDistance(float viewHeight, float cosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(viewHeight, cosAngle, distance);
	return CosAngleAtDistance(viewHeight, cosAngle, distance, heightAtDistance);
}

float3 AtmosphereExtinction(float centerDistance)
{
	float height = max(0.0, centerDistance - _PlanetRadius);
	
	float3 extinction = exp(-height / _RayleighHeight) * _RayleighScatter;
	extinction += exp(-height / _MieHeight) * (_MieAbsorption + _MieScatter);
	extinction += max(0.0, 1.0 - abs(height - _OzoneHeight) / _OzoneWidth) * _OzoneAbsorption;
	return extinction;
}

// Returns rayleigh (rgb and mie (a) scatter coefficient
float4 AtmosphereScatter(float centerDistance)
{
	float height = max(0.0, centerDistance - _PlanetRadius);
	
	float4 result;
	result.xyz = exp(-height / _RayleighHeight) * _RayleighScatter;
	result.w = exp(-height / _MieHeight) * _MieScatter;
	return result;
}

float3 TransmittanceToAtmosphere(float viewHeight, float cosAngle)
{
	float2 uv = TransmittanceUv(viewHeight, cosAngle);
	return _AtmosphereTransmittance.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float3 TransmittanceToAtmosphere(float3 P, float3 V)
{
	float viewHeight = length(P);
	float3 N = P / viewHeight;
	return TransmittanceToAtmosphere(viewHeight, dot(N, V));
}

float3 TransmittanceToPoint(float radius0, float cosAngle0, float radius1, float cosAngle1)
{
	float3 lowTransmittance, highTransmittance;
	if (radius0 > radius1)
	{
		lowTransmittance = TransmittanceToAtmosphere(radius1, -cosAngle1);
		highTransmittance = TransmittanceToAtmosphere(radius0, -cosAngle0);
	}
	else
	{
		lowTransmittance = TransmittanceToAtmosphere(radius0, cosAngle0);
		highTransmittance = TransmittanceToAtmosphere(radius1, cosAngle1);
	}
		
	return highTransmittance == 0.0 ? 0.0 : lowTransmittance * rcp(highTransmittance);
}

float3 AtmosphereMultiScatter(float viewHeight, float cosAngle)
{
	float2 uv = float2(0.5 * cosAngle + 0.5, (viewHeight - _PlanetRadius) / _AtmosphereHeight);
	return _MultipleScatter.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float3 AtmosphereLight(float3 P, float3 V, float3 L)
{
	// Single scatter, with earth shadow 
	float2 intersections;
	if (IntersectRaySphere(P, L, _PlanetRadius, intersections) && intersections.x >= 0.0)
		return 0.0;
	
	float angle = dot(V, L);
	float4 atmosphereScatter = AtmosphereScatter(length(P));
	float3 scatterColor = atmosphereScatter.xyz * RayleighPhaseFunction(angle);
	scatterColor += atmosphereScatter.w * HenyeyGreensteinPhaseFunction(_MiePhase, angle);
		
	return scatterColor * TransmittanceToAtmosphere(P, L);
}

float3 AtmosphereLightFull(float3 P, float3 V, float3 L, float attenuation)
{
	float3 lighting = AtmosphereLight(P, V, L) * attenuation;
	
	float3 N = normalize(P);
	float3 ms = AtmosphereMultiScatter(length(P), dot(N, L));
	float4 atmosphereScatter = AtmosphereScatter(length(P));
	lighting += ms * (atmosphereScatter.xyz + atmosphereScatter.w);
	
	return lighting;
}

#endif