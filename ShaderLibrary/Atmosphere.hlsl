#ifndef ATMOSPHERE_INCLUDED
#define ATMOSPHERE_INCLUDED

#include "Geometry.hlsl"
#include "Math.hlsl"
#include "Utility.hlsl"
#include "Volumetric.hlsl"

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
float4 _AtmosphereTransmittanceRemap, _AtmosphereMultiScatterRemap;

Texture2D<float3> _AtmosphereTransmittance;
Texture2D<float3> _MultipleScatter;

float DistanceToTopAtmosphereBoundary(float viewHeight, float cosAngle)
{
	float discriminant = Sq(viewHeight) * (Sq(cosAngle) - 1.0) + Sq(_TopRadius);
	return max(0.0, -viewHeight * cosAngle + sqrt(max(0.0, discriminant)));
}

float DistanceToBottomAtmosphereBoundary(float r, float mu)
{
	float discriminant = r * r * (mu * mu - 1.0) + Sq(_PlanetRadius);
	return max(0.0, (-r * mu - sqrt(max(0.0, discriminant))));
}

float DistanceToNearestAtmosphereBoundary(float r, float mu, bool ray_r_mu_intersects_ground)
{
	if (ray_r_mu_intersects_ground)
	{
		return DistanceToBottomAtmosphereBoundary(r, mu);
	}
	else
	{
		return DistanceToTopAtmosphereBoundary(r, mu);
	}
}

bool RayIntersectsGround(float viewHeight, float cosAngle)
{
	return (cosAngle < 0.0) && (Sq(viewHeight) * (Sq(cosAngle) - 1.0) + Sq(_PlanetRadius)) >= 0.0;
}

float DistanceToNearestAtmosphereBoundary(float r, float mu)
{
	bool hasGroundHit = RayIntersectsGround(r, mu);
	return DistanceToNearestAtmosphereBoundary(r, mu, hasGroundHit);
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
	float dMin = _TopRadius - viewHeight;
	float dMax = rho + H;
	float x_mu = Remap(d, dMin, dMax);
	float x_r = rho / H;
	return float2(x_mu, x_r) * _AtmosphereTransmittanceRemap.xy + _AtmosphereTransmittanceRemap.zw;
}

float2 UvToSkyParams(float2 uv)
{
	// Distance to top atmosphere boundary for a horizontal ray at ground level.
	float H = sqrt(Sq(_TopRadius) - Sq(_PlanetRadius));
	
	// Distance to the horizon, from which we can compute r:
	float rho = H * uv.y;
	float r = sqrt(rho * rho + Sq(_PlanetRadius));
	
	// Distance to the top atmosphere boundary for the ray (r,mu), and its minimum
	// and maximum values over all mu - obtained for (r,1) and (r,mu_horizon) -
	// from which we can recover mu:
	float d_min = _TopRadius - r;
	float d_max = rho + H;
	float d = d_min + uv.x * (d_max - d_min);
	float mu = d == 0.0 ? 1.0 : (H * H - rho * rho - d * d) / (2.0 * r * d);
	mu = clamp(mu, -1.0, 1.0);
	
	return float2(mu, r);
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

float3 AtmosphereExtinctionNoOzone(float centerDistance)
{
	float4 rayleighMieExtinctionCombined = exp2(centerDistance * _AtmosphereExtinctionScale + _AtmosphereExtinctionOffset);
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
	return exp2(centerDistance * _AtmosphereExtinctionScale + _AtmosphereScatterOffset);
}

float3 TransmittanceToAtmosphere(float viewHeight, float cosAngle, SamplerState samplerState)
{
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

float3 TransmittanceToPoint(float height1, float cosAngle1, float height0, float cosAngle0, SamplerState samplerState)
{
	//return exp(-OptDepthSpherExpMedium(height1, cosAngle1, height0, cosAngle0));
	
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
	return transmittance0 == 0.0 ? 0.0 : transmittance1 / transmittance0;
}

float3 TransmittanceToPoint(float height, float cosAngle, float distance, SamplerState samplerState)
{
	float height1 = HeightAtDistance(height, cosAngle, distance);
	float cosAngle1 = CosAngleAtDistance(height, cosAngle, distance, height1);
	return TransmittanceToPoint(height, cosAngle, height1, cosAngle1, samplerState);
}

float3 OpticalDepthToPoint(float height1, float cosAngle1, float height0, float cosAngle0, SamplerState samplerState)
{
	return OpticalDepthFromTransmittance(TransmittanceToPoint(height1, cosAngle1, height0, cosAngle0, samplerState));
}

float3 AtmosphereMultiScatter(float viewHeight, float cosAngle, SamplerState samplerState)
{
	float2 uv = float2(0.5 * cosAngle + 0.5, (viewHeight - _PlanetRadius) / _AtmosphereHeight);
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

float3 ComputeOpticalLengthToTopAtmosphereBoundary(float r, float mu, uint sampleCount, bool rayIntersectsGround)
{
	// The integration step, i.e. the length of each integration interval.
	float dx = DistanceToNearestAtmosphereBoundary(r, mu, rayIntersectsGround) / float(sampleCount);
	
	// Integration loop.
	float3 result = 0.0;
	for (uint i = 0; i <= sampleCount; i++)
	{
		float distance = float(i) * dx;
		
		// Distance between the current sample point and the planet center.
		float radius = clamp(HeightAtDistance(r, mu, distance), _PlanetRadius, _TopRadius);
		
		// Number density at the current sample point (divided by the number density
		// at the bottom of the atmosphere, yielding a dimensionless number).
		float3 extinction = AtmosphereExtinction(radius); // GetProfileDensity(profile, r_i - _PlanetRadius);
		
		// Sample weight (from the trapezoidal rule).
		float weight = i == 0 || i == sampleCount ? 0.5 : 1.0;
		result += extinction * weight * dx;
	}
	
	return result;
}

float3 ComputeTransmittanceToTopAtmosphereBoundary(float r, float mu, uint sampleCount, bool rayIntersectsGround)
{
	return exp(-ComputeOpticalLengthToTopAtmosphereBoundary(r, mu, sampleCount, rayIntersectsGround));
}

#endif