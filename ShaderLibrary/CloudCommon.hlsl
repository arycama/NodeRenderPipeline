#ifndef CLOUD_COMMON_INCLUDED
#define CLOUD_COMMON_INCLUDED

#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Lighting.hlsl"

Texture3D<float> _CloudNoise, _CloudDetail;
Texture2D<float3> _WeatherTexture;

float3 _LightColor0, _LightDirection0, _LightColor1, _LightDirection1;
float _Thickness, _Height, _WeatherScale;
float _FrontScatter, _BackScatter, _ScatterBlend;
float _ScatterOctaves, _ScatterContribution, _ScatterAttenuation, _ScatterEccentricity;
float _MinSamples, _MaxSamples, _SampleDistance, _CloudDepthScale, _ShadowSamples, _TransmittanceThreshold;
float _TemporalBlend, _StdDevFactor, _SampleFactor, _MaxDistance, _LightingSamples, _LightingDistance;
float _FogEnabled;
float _Density, _DetailScale, _NoiseScale, _DetailStrength;
float2 _WindSpeed;

#ifdef __INTELLISENSE__
	#define LIGHT_COUNT_TWO
#endif

float SampleCloudDensity(float3 positionWS)
{
	float altitude = distance(-_PlanetOffset, positionWS) - _PlanetRadius;
	float fraction = saturate((altitude - _Height) / _Thickness);
	float gradient = 4.0 * fraction * (1 - fraction);

	positionWS += _WorldSpaceCameraPos;
	float baseNoise = _CloudNoise.SampleLevel(_LinearRepeatSampler, positionWS * _NoiseScale, 0);
	float3 weatherData = _WeatherTexture.SampleLevel(_LinearRepeatSampler, positionWS.xz * _WeatherScale + _WindSpeed * _Time.y, 0);
    
	float cloudCoverage = gradient * weatherData.r;
	float baseCloud = RangeRemap(1.0 - cloudCoverage, 1.0, baseNoise) * cloudCoverage;
	
	float detailNoise = _CloudDetail.SampleLevel(_LinearRepeatSampler, positionWS * _DetailScale, 0);
	return RangeRemap(detailNoise * _DetailStrength, 1.0, baseCloud) * _Density;
}

float4 SampleCloud(float3 P, float3 V, float startDistance, float stepLength, float sampleCount, out float averageDepth)
{
	float lightingDs = _LightingDistance / _LightingSamples;
	
	float transmittanceSum = 0.0, weightedTransmittanceSum = 0.0;
	float4 color = float4(0.0, 0.0, 0.0, 1.0);
	for (float i = 0.0; i < sampleCount; i++)
	{
		float3 positionWS = (i * stepLength + startDistance) * V + P;
		float density = SampleCloudDensity(positionWS);
		float transmittance = exp(-density * stepLength);

		if (density > 0.0)
		{
			#if defined(LIGHT_COUNT_ONE) || defined(LIGHT_COUNT_TWO)
				float2 intersections;
				if (!IntersectRaySphere(positionWS + _PlanetOffset, _LightDirection0, _PlanetRadius, intersections) || intersections.x < 0.0)
				{
					float3 color0 = TransmittanceToAtmosphere(positionWS + _PlanetOffset, _LightDirection0, _LinearClampSampler) * ApplyExposure(_LightColor0);
					if (any(color0 > 0.0))
					{
						float lightTransmittance = 1.0;
						for (float k = 0.5 * lightingDs; k < _LightingDistance; k += lightingDs)
						{
							float3 samplePos = positionWS + _LightDirection0 * k;
							lightTransmittance *= exp(-SampleCloudDensity(samplePos) * lightingDs);
						}
				
						float LdotV = dot(_LightDirection0, V);
						float asymmetry = lightTransmittance * transmittance;
				
						float phase = lerp(CornetteShanksPhaseFunction(-_BackScatter, LdotV) * 2.16, CornetteShanksPhaseFunction(_FrontScatter, LdotV), asymmetry);
						color.rgb += color0 * phase * lightTransmittance * (1.0 - transmittance) * color.a;
					}
				}
			#endif
		
			#ifdef LIGHT_COUNT_TWO
				float3 color1 = TransmittanceToAtmosphere(positionWS + _PlanetOffset, _LightDirection1, _LinearClampSampler) * ApplyExposure(_LightColor1);
				if (any(color1 > 0.0))
				{
					float lightTransmittance = 1.0;
					for (float k = 0.5 * lightingDs; k < _LightingDistance; k += lightingDs)
					{
						float3 samplePos = positionWS + _LightDirection1 * k;
						lightTransmittance *= exp(-SampleCloudDensity(samplePos) * lightingDs);
					}
				
					float LdotV = dot(_LightDirection1, V);
					float asymmetry = lightTransmittance * transmittance;
				
					float phase = lerp(CornetteShanksPhaseFunction(-_BackScatter, LdotV) * 2.16, CornetteShanksPhaseFunction(_FrontScatter, LdotV), asymmetry);
					color.rgb += color1 * phase * lightTransmittance * (1.0 - transmittance) * color.a;
				}
			#endif
		}

		color.a *= transmittance;
		transmittanceSum += color.a;
		weightedTransmittanceSum += (i * stepLength + startDistance) * color.a;

		// Causes shadow issues, possibly because once it breaks we're no longer ccumulating depth+weighted 
		if (color.a < _TransmittanceThreshold)
			break;
	}
	
	averageDepth = weightedTransmittanceSum / max(1e-6, transmittanceSum);// / (0.5 + 0.5 / sampleCount);
	
	// Apply Ambient 
	color.rgb += AmbientLightCornetteShanks(V, 0.0, 0.0, 0.5) * (1.0 - color.a);

	return color;
}

float4 RenderCloud(float3 P, float3 rayDir, uint2 id, float depth, float sceneDepth, out float cloudDepth, bool noise = true)
{
    // Check for intersection with planet
	float2 innerIntersections, outerIntersections;;
	bool hasInnerIntersection = IntersectRaySphere(P + _PlanetOffset, rayDir, _PlanetRadius + _Height, innerIntersections) && innerIntersections.x >= 0;
	bool hasOuterIntersection = IntersectRaySphere(P + _PlanetOffset, rayDir, _PlanetRadius + _Height + _Thickness, outerIntersections) && outerIntersections.x >= 0;

	bool isMaxDepth = depth == _FarClipValue;

	cloudDepth = _MaxDistance * 2;
	float startDistance = 0, endDistance = 0;

	bool belowClouds = length(P + _PlanetOffset) < _PlanetRadius + _Height;
	if (belowClouds)
	{
		float2 planetIntersections;
		bool hasPlanetIntersection = IntersectRaySphere(P + _PlanetOffset, rayDir, _PlanetRadius, planetIntersections) && planetIntersections.x >= 0.0;
		if (hasPlanetIntersection)
		{
			return float4(0.0, 0.0, 0.0, 1.0);
		}

		startDistance = innerIntersections.y;

	    // Early exit if a scene object is blocking the cloud entirely
		if (startDistance > sceneDepth && !isMaxDepth)
		{
			return float4(0.0, 0.0, 0.0, 1.0);
		}

		endDistance = outerIntersections.y;
	}
	else
	{
		bool aboveClouds = length(P + _PlanetOffset) > _PlanetRadius + _Height + _Thickness;
		if (aboveClouds)
		{
	        // Early exit if we miss the planet
			if (!hasOuterIntersection || (!isMaxDepth && outerIntersections.x > sceneDepth))
			{
				return float4(0.0, 0.0, 0.0, 1.0);
			}

			startDistance = outerIntersections.x;
		}

		endDistance = hasInnerIntersection ? innerIntersections.x : outerIntersections.y;
	}

	if (!isMaxDepth)
		endDistance = min(sceneDepth, endDistance);

    // Adaptive sample count based on angle
	float totalDistance = abs(endDistance - startDistance);
	float sampleCount = lerp(_MinSamples, _MaxSamples, pow(saturate(totalDistance / _SampleDistance), _SampleFactor));

	float offset = noise ? BlueNoise1D(id) : 0.5;
	
	float stepLength = (endDistance - startDistance) / sampleCount;
	float start = startDistance + stepLength * offset;
	return SampleCloud(P, rayDir, start, stepLength, sampleCount, cloudDepth);
}

#endif