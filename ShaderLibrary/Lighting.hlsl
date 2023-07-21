#ifndef LIGHTING_INCLUDED
#define LIGHTING_INCLUDED

#ifdef __INTELLISENSE__
    #define VOXEL_GI_ON
#endif

#include "Atmosphere.hlsl"
#include "Geometry.hlsl"
#include "LightingCommon.hlsl"
#include "MatrixUtils.hlsl"
#include "SpaceTransforms.hlsl"
#include "Utility.hlsl"

Texture2D<float4> _CloudTransmittance, _ReflectionBuffer;
Texture2D<float3> _CloudShadow;

float _FlatAmbient;

// Volumetric Lighting
float4x4 _WorldToCloudShadow;
float4 _CloudShadow_TexelSize;
float _CloudDepthInvScale;

float3 _GroundColor;
float _CloudMaxAvgExtinction, _CloudMaxOpticalDepth;

Texture2D<float> _WaterShadows;
float4x4 _WaterShadowMatrix;

StructuredBuffer<float3x4> _DirectionalShadowMatrices;
StructuredBuffer<float4x4> _SpotlightShadowMatrices, _AreaShadowMatrices;
Buffer<float4> _CullingSpheres;
TextureCubeArray<float> _PointShadows;
Texture2DArray<float> _SpotlightShadows, _AreaShadows;
float _ShadowPcfRadius;
float _CascadeCount;

Texture2DArray<float> _DirectionalShadows;
Texture2D<float> _OtherShadowAtlas;
float4 _ShadowMapTexture_TexelSize;
float _ShadowmapExponent;
float3 _WaterExtinction;
float _WaterShadowFar;

uint _BlockerSamples;
uint _PcfSamples;

struct LightCommon
{
    float3 color;
    float3 direction;
};

// VXGI
Texture3D<float> _VoxelGIX, _VoxelGIY, _VoxelGIZ, _VoxelOcclusion;
float4x4 _WorldToVoxel;
float3 _VoxelCenter, _VoxelOffset;
float _VoxelSize;
float _VoxelResolution;
float _VoxelBias;

float _VolumeWidth, _VolumeHeight, _VolumeSlices, _VolumeDepth;
Texture3D<float3> _VolumetricLighting;

float _EVSMExponent, _LightLeakBias, _VarianceBias;
float _DirectionalShadowDistance, _DirectionalShadowCascadeScale, _DirectionalShadowCascadeBias, _DirectionalShadowCascadeFade, _DirectionalShadowCascadeFadeScale, _DirectionalShadowCascadeFadeBias;

float3 _OriginalCameraPosition, _ReflectionCameraPosition;
float4x4 _CameraViewProjectionMatrix;

// ref: Practical Realtime Strategies for Accurate Indirect Occlusion
// Update ambient occlusion to colored ambient occlusion based on statitics of how light is bouncing in an object and with the albedo of the object
float3 GTAOMultiBounce(float visibility, float3 albedo)
{
	float3 a = 2.0404 * albedo - 0.3324;
	float3 b = -4.7951 * albedo + 0.6417;
	float3 c = 2.7552 * albedo + 0.6903;

	float x = visibility;
	return max(x, ((x * a + b) * x + c) * x);
}

// Ref: Steve McAuley - Energy-Conserving Wrapped Diffuse
float ComputeWrappedDiffuseLighting(float NdotL, float w)
{
	return saturate((NdotL + w) / ((1.0 + w) * (1.0 + w)));
}

float VoxelGI(float3 positionWS, float3 normalWS)
{
    #ifdef VOXEL_GI_ON
		float3 voxelPos = MultiplyPoint(_WorldToVoxel, positionWS).xyz;

		if(all(voxelPos > 0.0 && voxelPos < 1.0))
		{
			float3 uv = voxelPos;

			// Convert to integer, wrap, convert back to normalized
			uv = Mod(uv * _VoxelResolution + _VoxelOffset, _VoxelResolution);
        
            // Offset based on normal
		    uv = floor(uv + normalWS * IntersectRayAABBSimple(frac(uv), normalWS, 0, 1)) + 0.5;
            uv /= _VoxelResolution;

			float3 nSquared = normalWS * normalWS, isNegative = normalWS < 0.0 ? 0.5 : 0.0;;
			uv.z *= 0.5;
			float3 tcz = uv.zzz + isNegative;

			float result = nSquared.x * _VoxelGIX.SampleLevel(_LinearRepeatSampler, float3(uv.xy, tcz.x), 0.0);
			result += nSquared.y * _VoxelGIY.SampleLevel(_LinearRepeatSampler, float3(uv.xy, tcz.y), 0.0);
			return result + nSquared.z * _VoxelGIZ.SampleLevel(_LinearRepeatSampler, float3(uv.xy, tcz.z), 0.0);
		}
    #endif

    return 1;
}

float VoxelOcclusion(float3 positionWS)
{
    #ifdef VOXEL_GI_ON
		float3 voxelPos = MultiplyPoint(_WorldToVoxel, positionWS).xyz;

		if(all(voxelPos > 0.0 && voxelPos < 1.0))
		{
			float3 uv = voxelPos;

			// Convert to integer, wrap, convert back to normalized
			uv = Mod(uv * _VoxelResolution + _VoxelOffset, _VoxelResolution) / _VoxelResolution;
    
            return _VoxelOcclusion.SampleLevel(_LinearRepeatSampler, uv, 0.0);
		}
    #endif

    return 1;
}

// Outputs
#if defined(UNITY_PASS_DEFERRED) || defined(MOTION_VECTORS_ON)
	#define FRAGMENT_OUTPUT GBufferOut
	#define FRAGMENT_OUTPUT_TYPE
#elif defined(UNITY_PASS_SHADOWCASTER)
	#define FRAGMENT_OUTPUT void
	#define FRAGMENT_OUTPUT_TYPE
#else
#define FRAGMENT_OUTPUT float4
#define FRAGMENT_OUTPUT_TYPE : SV_Target
#endif

Texture2D<float4> _CloudCoverage;

cbuffer AmbientSh
{
	float4 _AmbientSh[7];
};

float3 EvaluateSH(float3 N, float3 albedo, float occlusion, float4 sh[7])
{
	// Calculate the zonal harmonics expansion for V(x, ωi)*(n.l)
	float3 t = FastACosPos(sqrt(saturate(1.0 - GTAOMultiBounce(occlusion, albedo))));
	float3 a = sin(t);
	float3 b = cos(t);
	
	// Calculate the zonal harmonics expansion for V(x, ωi)*(n.l)
	float3 A0 = a * a;
	float3 A1 = 1.0 - b * b * b;
	float3 A2 = a * a * (1.0 + 3.0 * b * b);
	 
	float4 shAr = sh[0];
	float4 shAg = sh[1];
	float4 shAb = sh[2];
	float4 shBr = sh[3];
	float4 shBg = sh[4];
	float4 shBb = sh[5];
	float4 shC = sh[6];
	
	float3 irradiance = 0.0;
	irradiance.r = dot(shAr.xyz * A1.r, N) + shAr.w * A0.r;
	irradiance.g = dot(shAg.xyz * A1.g, N) + shAg.w * A0.g;
	irradiance.b = dot(shAb.xyz * A1.b, N) + shAb.w * A0.b;
	
    // 4 of the quadratic (L2) polynomials
	float4 vB = N.xyzz * N.yzzx;
	irradiance.r += dot(shBr * A2.r, vB) + shBr.z / 3.0 * (A0.r - A2.r);
	irradiance.g += dot(shBg * A2.g, vB) + shBg.z / 3.0 * (A0.g - A2.g);
	irradiance.b += dot(shBb * A2.b, vB) + shBb.z / 3.0 * (A0.b - A2.b);

    // Final (5th) quadratic (L2) polynomial
	float vC = N.x * N.x - N.y * N.y;
	irradiance += shC.rgb * A2 * vC;
	
	return irradiance;
}

float3 AmbientLight(float3 N, float3 albedo, float occlusion)
{
	return EvaluateSH(N, albedo, occlusion, _AmbientSh);
}

float CloudTransmittanceLevelZero(float3 positionWS)
{
	float3 coords = MultiplyPoint3x4(_WorldToCloudShadow, positionWS);
	if (any(coords.xy <= 0) || any(coords.xy >= 1))
		return 1.0;

	float3 shadowData = _CloudShadow.SampleLevel(_LinearClampSampler, coords.xy, 0.0);
	float depth = max(0.0, coords.z - shadowData.r) * _CloudDepthInvScale;
	float opticalDepth = depth * shadowData.g;
	float transmittance = exp(-opticalDepth);
	return max(transmittance, shadowData.b);
}

float3 DiscLightApprox(float angularDiameter, float3 R, float3 L)
{
    // Disk light approximation based on angular diameter
    float r = sin(radians(angularDiameter * 0.5)); // Disk radius
    float d = cos(radians(angularDiameter * 0.5)); // Distance to disk

    // Closest point to a disk (since the radius is small, this is a good approximation
    float DdotR = dot(L, R);
    float3 S = R - DdotR * L;
    return DdotR < d ? normalize(d * L + normalize(S) * r) : R;
}

float GetNoHSquared(float radiusTan, float NoL, float NoV, float VoL)
{
    // radiuscos can be precalculated if radiusTan is a directional light
    float radiusCos = rsqrt(1.0 + radiusTan * radiusTan);

    // Early out if R falls within the disc
    float RoL = 2.0 * NoL * NoV - VoL;
    if(RoL >= radiusCos)
        return 1.0;

    float rOverLengthT = radiusCos * radiusTan * rsqrt(1.0 - RoL * RoL);
    float NoTr = rOverLengthT * (NoV - RoL * NoL);
    float VoTr = rOverLengthT * (2.0 * NoV * NoV - 1.0 - RoL * VoL);

    // Calculate (N.H)^2 based on the bent light vector
    float newNoL = NoL * radiusCos + NoTr;
    float newVoL = VoL * radiusCos + VoTr;
    float NoH = NoV + newNoL;
    float HoH = 2.0 * newVoL + 2.0;
    return max(0.0, NoH * NoH / HoH);
}

float DirectionalLightShadow(float3 positionWS, float shadowIndex, float jitter = 0.5, bool softShadows = false)
{
	bool useReflectionProbes = false;
	#ifdef REFLECTION_PROBE_RENDERING
        useReflectionProbes = true;
    #endif
    
	float viewZ = WorldToClip(positionWS).w;
    
    // For reflection probes, we need to convert to the camera-relative position
	if (useReflectionProbes)
	{
		positionWS = positionWS + _ReflectionCameraPosition - _OriginalCameraPosition;
		viewZ = MultiplyPoint(_CameraViewProjectionMatrix, positionWS).w;
	}
    
    // Calculate the cascade from near plane distance. Max(0) required because shadow near plane for cascades may be further than camera
    // near plane, causing nearby pixels to be below 0
	float cascade = max(0.0, log2(max(1.0, viewZ)) * _DirectionalShadowCascadeScale + _DirectionalShadowCascadeBias);

    // Cascade blending
	if (!useReflectionProbes)
	{
		float fraction = frac(cascade) * _DirectionalShadowCascadeFadeScale + _DirectionalShadowCascadeFadeBias;
		if (fraction > jitter)
			cascade++;
        
		if (cascade >= _CascadeCount)
			return 1.0;
	}
    else
	{
		cascade = _CascadeCount - 1.0;
	}
    
	float slice = shadowIndex * _CascadeCount + cascade;
	float3x4 cascadeData = _DirectionalShadowMatrices[slice];
	float3 positionLS = MultiplyPoint3x4(cascadeData, positionWS);
	return _DirectionalShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(positionLS.xy, floor(slice)), positionLS.z);
    
    //float2 offset = VogelDiskSample(j, _PcfSamples, jitter * TWO_PI) * _ShadowPcfRadius;
    
	//float sum = 0.0;
	//for (uint j = 0; j < _PcfSamples; j++)
	//{
	//	float GoldenAngle = 2.4f;

	//	float r = sqrt(j + 0.5) / sqrt(_PcfSamples);
	//	float theta = j * GoldenAngle + jitter * TWO_PI;

	//	float sine, cosine;
	//	sincos(theta, sine, cosine);

	//	float2 offset = float2(r * cosine, r * sine);
	//	sum += _DirectionalShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(positionLS.xy + offset, floor(slice)), positionLS.z);
	//}
    
	//return sum / _PcfSamples;
}

float3 DirectionalLightColor(uint index, float3 positionWS, bool softShadows = false, float jitter = 0.5, bool applyShadow = true, bool exponentialShadows = false, bool atmosphereTransmittance = true)
{
    DirectionalLightData  lightData = _DirectionalLightData[index];
    
    // Earth shadow 
	float2 intersections;
	if (IntersectRaySphere(positionWS + _PlanetOffset, lightData.Direction, _PlanetRadius, intersections) && intersections.x >= 0.0)
		return 0.0;
    
	float attenuation = 1.0;
    if (applyShadow)
    {
		if (lightData.ShadowIndex != UintMax)
		{
			attenuation *= DirectionalLightShadow(positionWS, lightData.ShadowIndex, jitter, softShadows);
			if (attenuation == 0.0)
				return 0.0;
		}
        
		if (index == 0)
		{
			attenuation *= CloudTransmittanceLevelZero(positionWS);
			if (attenuation == 0.0)
				return 0.0;
		}
	}
    
	float3 color = attenuation;
    
#ifdef WATER_SHADOW_ON
	    float shadowDistance = max(0.0, -_WorldSpaceCameraPos.y - positionWS.y) / max(1e-6, saturate(lightData.Direction.y));
	    float3 shadowPosition = MultiplyPoint3x4(_WaterShadowMatrix, positionWS);
	    if (index == 0 && all(saturate(shadowPosition) == shadowPosition))
	    {
		    float shadowDepth = _WaterShadows.SampleLevel(_LinearClampSampler, shadowPosition.xy, 0.0).r;
		    shadowDistance = saturate(shadowDepth - shadowPosition.z) * _WaterShadowFar;
	    }
    
	    color *= exp(-shadowDistance * _WaterExtinction);
        if(all(color == 0.0))
			return 0.0;
    #endif
    
	if (atmosphereTransmittance)
		color *= TransmittanceToAtmosphere(positionWS + _PlanetOffset, lightData.Direction);

	return color * ApplyExposure(lightData.Color);
}

// Ref: Moving Frostbite to PBR.

// Non physically based hack to limit light influence to attenuationRadius.
// Square the result to smoothen the function.
float DistanceWindowing(float distSquare, float rangeAttenuationScale, float rangeAttenuationBias)
{
    // If (range attenuation is enabled)
    //   rangeAttenuationScale = 1 / r^2
    //   rangeAttenuationBias  = 1
    // Else
    //   rangeAttenuationScale = 2^12 / r^2
    //   rangeAttenuationBias  = 2^24
	return saturate(rangeAttenuationBias - Sq(distSquare * rangeAttenuationScale));
}

float SmoothDistanceWindowing(float distSquare, float rangeAttenuationScale, float rangeAttenuationBias)
{
	float factor = DistanceWindowing(distSquare, rangeAttenuationScale, rangeAttenuationBias);
	return Sq(factor);
}

// Applies SmoothDistanceWindowing() after transforming the attenuation ellipsoid into a sphere.
// If r = rsqrt(invSqRadius), then the ellipsoid is defined s.t. r1 = r / invAspectRatio, r2 = r3 = r.
// The transformation is performed along the major axis of the ellipsoid (corresponding to 'r1').
// Both the ellipsoid (e.i. 'axis') and 'unL' should be in the same coordinate system.
// 'unL' should be computed from the center of the ellipsoid.
float EllipsoidalDistanceAttenuation(float3 unL, float3 axis, float invAspectRatio,
                                    float rangeAttenuationScale, float rangeAttenuationBias)
{
    // Project the unnormalized light vector onto the axis.
	float projL = dot(unL, axis);

    // Transform the light vector so that we can work with
    // with the ellipsoid as if it was a sphere with the radius of light's range.
	float diff = projL - projL * invAspectRatio;
	unL -= diff * axis;

	float sqDist = dot(unL, unL);
	return SmoothDistanceWindowing(sqDist, rangeAttenuationScale, rangeAttenuationBias);
}

// Applies SmoothDistanceWindowing() using the axis-aligned ellipsoid of the given dimensions.
// Both the ellipsoid and 'unL' should be in the same coordinate system.
// 'unL' should be computed from the center of the ellipsoid.
float EllipsoidalDistanceAttenuation(float3 unL, float3 invHalfDim,
                                    float rangeAttenuationScale, float rangeAttenuationBias)
{
    // Transform the light vector so that we can work with
    // with the ellipsoid as if it was a unit sphere.
	unL *= invHalfDim;

	float sqDist = dot(unL, unL);
	return SmoothDistanceWindowing(sqDist, rangeAttenuationScale, rangeAttenuationBias);
}

// Computes the squared magnitude of the vector computed by MapCubeToSphere().
float ComputeCubeToSphereMapSqMagnitude(float3 v)
{
	float3 v2 = v * v;
    // Note: dot(v, v) is often computed before this function is called,
    // so the compiler should optimize and use the precomputed result here.
	return dot(v, v) - v2.x * v2.y - v2.y * v2.z - v2.z * v2.x + v2.x * v2.y * v2.z;
}

// Applies SmoothDistanceWindowing() after mapping the axis-aligned box to a sphere.
// If the diagonal of the box is 'd', invHalfDim = rcp(0.5 * d).
// Both the box and 'unL' should be in the same coordinate system.
// 'unL' should be computed from the center of the box.
float BoxDistanceAttenuation(float3 unL, float3 invHalfDim,
                            float rangeAttenuationScale, float rangeAttenuationBias)
{
	float attenuation = 0.0;

    // Transform the light vector so that we can work with
    // with the box as if it was a [-1, 1]^2 cube.
	unL *= invHalfDim;

    // Our algorithm expects the input vector to be within the cube.
	if ((Max3(abs(unL)) <= 1.0))
	{
		float sqDist = ComputeCubeToSphereMapSqMagnitude(unL);
		attenuation = SmoothDistanceWindowing(sqDist, rangeAttenuationScale, rangeAttenuationBias);
	}
	return attenuation;
}

// Square the result to smoothen the function.
float AngleAttenuation(float cosFwd, float lightAngleScale, float lightAngleOffset)
{
	return saturate(cosFwd * lightAngleScale + lightAngleOffset);
}

float SmoothAngleAttenuation(float cosFwd, float lightAngleScale, float lightAngleOffset)
{
	float attenuation = AngleAttenuation(cosFwd, lightAngleScale, lightAngleOffset);
	return Sq(attenuation);
}

#define PUNCTUAL_LIGHT_THRESHOLD 0.01 // 1cm (in Unity 1 is 1m)

// Combines SmoothWindowedDistanceAttenuation() and SmoothAngleAttenuation() in an efficient manner.
// distances = {d, d^2, 1/d, d_proj}, where d_proj = dot(lightToSample, lightData.forward).
float PunctualLightAttenuation(float4 distances, float rangeAttenuationScale, float rangeAttenuationBias,
                              float lightAngleScale, float lightAngleOffset)
{
	float distSq = distances.y;
	float distRcp = distances.z;
	float distProj = distances.w;
	float cosFwd = distProj * distRcp;

	float attenuation = min(distRcp, 1.0 / PUNCTUAL_LIGHT_THRESHOLD);
	attenuation *= DistanceWindowing(distSq, rangeAttenuationScale, rangeAttenuationBias);
	attenuation *= AngleAttenuation(cosFwd, lightAngleScale, lightAngleOffset);

    // Effectively results in SmoothWindowedDistanceAttenuation(...) * SmoothAngleAttenuation(...).
	return Sq(attenuation);
}

LightCommon GetLightColor(LightData lightData, float3 positionWS, float dither, bool softShadows)
{
    LightCommon light;
    light.color = ApplyExposure(lightData.color);

    float rangeAttenuationScale = rcp(Sq(lightData.range));
    float3 lightVector = lightData.positionWS - positionWS;
    light.direction = normalize(lightVector);

    // Rotate the light direction into the light space.
    float3x3 lightToWorld = float3x3(lightData.right, lightData.up, lightData.forward);
    float3 positionLS = mul(lightToWorld, -lightVector);

    // Apply the sphere light hack to soften the core of the punctual light.
    // It is not physically plausible (using max() is more correct, but looks worse).
    // See https://www.desmos.com/calculator/otqhxunqhl
    float dist = max(lightData.size.x, length(lightVector));
	float distSq = dist * dist;
    float distRcp = rsqrt(distSq);
    
	float3 invHalfDim = rcp(float3(lightData.range + lightData.size.x * 0.5, lightData.range + lightData.size.y * 0.5, lightData.range));

    // Line Light
    if (lightData.lightType == 5)
    {
        light.color *= EllipsoidalDistanceAttenuation(lightVector, invHalfDim, rangeAttenuationScale, 1.0);
    }

    // Rectangle/area light
    if (lightData.lightType == 6)
    {
        if (dot(lightData.forward, lightVector) >= FloatEps)
            light.color = 0.0;
        
        light.color *= BoxDistanceAttenuation(positionLS, invHalfDim, 1, 1);
    }
    else
	{
        // Inverse square + radial distance falloff
        // {d, d^2, 1/d, d_proj}
		float4 distances = float4(dist, distSq, distRcp, dot(-lightVector, lightData.forward));
		light.color *= PunctualLightAttenuation(distances, rangeAttenuationScale, 1.0, lightData.angleScale, lightData.angleOffset);

        // Manually clip box light X/Y (Z is handled by above)
		if (lightData.lightType == 3 || lightData.lightType == 4)
		{
            // Perform perspective projection for frustum light
			float2 positionCS = positionLS.xy;
			if (lightData.lightType == 3)
				positionCS /= positionLS.z;

         // Box lights have no range attenuation, so we must clip manually.
			if (Max3(float3(abs(positionCS), abs(positionLS.z - 0.5 * lightData.range) - 0.5 * lightData.range + 1)) > 1.0)
				light.color = 0.0;
		}
	}
    
    // Shadows (If enabled, disabled in reflection probes for now)
    #ifndef NO_SHADOWS
    if (lightData.shadowIndex != UintMax)
    {
        // Point light
		if (lightData.lightType == 1)
        {
            float3 toLight = lightVector * float3(-1, 1, -1);
            float dominantAxis = Max3(abs(toLight));
            float depth = rcp(dominantAxis) * lightData.shadowProjectionY + lightData.shadowProjectionX;
            light.color *= _PointShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float4(toLight, lightData.shadowIndex), depth);
        }

        // Spot light
        if (lightData.lightType == 2 || lightData.lightType == 3 || lightData.lightType == 4)
        {
            float3 positionLS = MultiplyPointProj(_SpotlightShadowMatrices[lightData.shadowIndex], positionWS).xyz;
			if (all(saturate(positionLS.xy) == positionLS.xy))            
                light.color *= _SpotlightShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(positionLS.xy, lightData.shadowIndex), positionLS.z);
        }
        
        // Area light
		if (lightData.lightType == 6)
		{
            float4 positionLS = MultiplyPoint(_AreaShadowMatrices[lightData.shadowIndex], positionWS);
            
            // Vogel disk randomised PCF
			float sum = 0.0;
			for (uint j = 0; j < _PcfSamples; j++)
			{
				float2 offset = VogelDiskSample(j, _PcfSamples, dither * TwoPi) * _ShadowPcfRadius;
				float3 uv = float3(positionLS.xy + offset, positionLS.z) / positionLS.w;
				sum += _AreaShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(uv.xy, lightData.shadowIndex), uv.z);
			}
                
			light.color *= sum / _PcfSamples;
		}
	}
    #endif

    return light;
}

// Combines two CS phase functions, allows for configurable front and back scatter
float TwoTermCornetteShanksPhaseFunction(float angle, float backScatter, float frontScatter, float scatterBlend)
{
    return lerp(CornetteShanksPhaseFunction(backScatter, angle), CornetteShanksPhaseFunction(frontScatter, angle),
                scatterBlend);
}

float3 CombineShadowcoordComponents(float2 baseUV, float2 deltaUV, float depth, float3 receiverPlaneDepthBias)
{
    float3 uv = float3(baseUV + deltaUV, depth + receiverPlaneDepthBias.z);
    uv.z += dot(deltaUV, receiverPlaneDepthBias.xy);
    return uv;
}

float PenumbraSize(float zReceiver, float zBlocker) //Parallel plane estimation
{
    return zBlocker - zReceiver; // / zBlocker;
}

void FindBlocker(out float avgBlockerDepth, out uint numBlockers, float2 uv, float zReceiver, float searchWidth,
                 float dither)
{
    // This uses similar triangles to compute what
    // area of the shadow map we should search
    //searchWidth = LIGHT_SIZE_UV * (zReceiver - NEAR_PLANE) / zReceiver;

    float blockerSum = 0;
    numBlockers = 0;

    for (uint i = 0; i < _BlockerSamples; ++i)
    {
        float2 offset = VogelDiskSample(i, _BlockerSamples, dither * TwoPi) * searchWidth;
        float shadowMapDepth = _OtherShadowAtlas.SampleLevel(_PointClampSampler, uv + offset, 0);
        if (shadowMapDepth < zReceiver)
        {
            blockerSum += shadowMapDepth;
            numBlockers++;
        }
    }

    avgBlockerDepth = blockerSum / numBlockers;
}

float PCF_Filter(float2 uv, float zReceiver, float filterRadiusUV, float dither, float3 biasUVZ = 0)
{
    float sum = 0.0f;
    for (uint i = 0; i < _PcfSamples; ++i)
    {
		float2 offset = VogelDiskSample(i, _PcfSamples, dither * TwoPi) * filterRadiusUV;
        sum += _OtherShadowAtlas.SampleCmpLevelZero(_LinearClampCompareSampler, uv + offset, 1.0 - zReceiver);
    }
    return sum / _PcfSamples;
}

float PCSS(float4 coords, float searchWidth, float dither)
{
    float2 uv = coords.xy;
    float zReceiver = 1 - coords.z; // Assumed to be eye-space z in this code

    // STEP 1: blocker search
    float avgBlockerDepth = 0;
    float numBlockers = 0;
    FindBlocker(avgBlockerDepth, numBlockers, uv, zReceiver, searchWidth, dither);

    if (numBlockers < 1)
        //There are no occluders so early out (this saves filtering)
        return 1.0f;

    // STEP 2: penumbra size
    float penumbraRatio = PenumbraSize(zReceiver, avgBlockerDepth);
    float filterRadiusUV = penumbraRatio * searchWidth;
    //filterRadiusUV = penumbraRatio * LIGHT_SIZE_UV * NEAR_PLANE / (1 - coords.z);

    // STEP 3: filtering
    return PCF_Filter(uv, zReceiver, filterRadiusUV, dither);
}

void FindBlockerArray(out float avgBlockerDepth, out uint numBlockers, float3 position, float index, float searchWidth,
                      float dither, float3 biasUVZ = 0)
{
    // This uses similar triangles to compute what
    // area of the shadow map we should search
    //searchWidth = LIGHT_SIZE_UV * (zReceiver - NEAR_PLANE) / zReceiver;

    float blockerSum = 0;
    numBlockers = 0;

    for (uint i = 0; i < _BlockerSamples; ++i)
    {
		float2 offset = VogelDiskSample(i, _BlockerSamples, dither * TwoPi) * searchWidth;
        float3 uv = CombineShadowcoordComponents(position.xy, offset, position.z, biasUVZ);
        float shadowMapDepth = _DirectionalShadows.SampleLevel(_PointClampSampler, float3(uv.xy, index), 0);
        if (shadowMapDepth > uv.z)
        {
            blockerSum += shadowMapDepth;
            numBlockers++;
        }
    }

    avgBlockerDepth = blockerSum / numBlockers;
}

float PCF_FilterArray(float3 position, float filterRadiusUV, float index, float dither, float3 biasUVZ = 0)
{
    float sum = 0.0f;
    for (uint i = 0; i < _PcfSamples; ++i)
    {
		float2 offset = VogelDiskSample(i, _PcfSamples, dither * TwoPi) * filterRadiusUV;
        float3 uv = CombineShadowcoordComponents(position.xy, offset, position.z, biasUVZ);
        sum += _DirectionalShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(uv.xy + offset, index), uv.z);
    }
    return sum / _PcfSamples;
}

float PCSSArray(float3 position, float cascadeIndex, float searchWidth, float dither, float3 biasUVZ = 0)
{
    // STEP 1: blocker search
    float avgBlockerDepth = 0;
    float numBlockers = 0;
    FindBlockerArray(avgBlockerDepth, numBlockers, position, cascadeIndex, searchWidth, dither, biasUVZ);

    if (numBlockers < 1)
        //There are no occluders so early out (this saves filtering)
        return 1.0f;

    // STEP 2: penumbra size
    float penumbraRatio = PenumbraSize(position.z, avgBlockerDepth);
    float filterRadiusUV = penumbraRatio * searchWidth;
    //filterRadiusUV = penumbraRatio * LIGHT_SIZE_UV * NEAR_PLANE / (1 - coords.z);

    // STEP 3: filtering
    return PCF_FilterArray(position, filterRadiusUV, cascadeIndex, dither, biasUVZ);
}

// Based on Oat and Sander's 2008 technique
// Area/solidAngle of intersection of two cone
float4 BlendVisibiltyCones(float4 coneA, float4 coneB)
{
	float cosC1 = sqrt(saturate(1.0 - coneA.a));
	float cosC2 = sqrt(saturate(1.0 - coneB.a));
	float cosB = dot(coneA.xyz, coneB.xyz);

	float r0 = FastACosPos(cosC1);
	float r1 = FastACosPos(cosC2);
	float d = FastACosPos(cosB);

	float3 normal;
	float area;
	if (min(r1, r0) <= max(r1, r0) - d)
	{
        // One cap is completely inside the other
		area = 1.0 - max(cosC1, cosC2);
		normal = r0 > r1 ? coneB.xyz : coneA.xyz;
	}
	else if (r0 + r1 <= d)
	{
        // No intersection exists
		area = 0.0;
		normal = NLerp(coneA.xyz, coneB.xyz, 0.5);
	}
	else
	{
		float diff = abs(r0 - r1);
		float den = r0 + r1 - diff;
		float x = 1.0 - saturate((d - diff) / max(den, 1e-4));
		area = (1.0 - max(cosC1, cosC2)) * smoothstep(0.0, 1.0, x);
		float angle = 0.5 * (d - abs(r0 - r1));
		normal = NLerp(coneA.xyz, coneB.xyz, angle / d);
	}

	return float4(normal, 1.0 - Sq(1.0 - area));
}

float3 PlanetCurve(float3 positionRWS)
{
	float dst = length(positionRWS.xz) / _PlanetRadius;
	positionRWS.y += _PlanetRadius * (sqrt(1 - dst * dst) - 1.0);
	return positionRWS;
}

float3 PlanetCurvePrevious(float3 positionRWS)
{
	float dst = length(positionRWS.xz) / _PlanetRadius;
	positionRWS.y += _PlanetRadius * (sqrt(1 - dst * dst) - 1.0);
	return positionRWS;
}

#endif