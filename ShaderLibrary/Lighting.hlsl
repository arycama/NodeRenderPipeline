#ifndef LIGHTING_INCLUDED
#define LIGHTING_INCLUDED

#ifdef __INTELLISENSE__
    #define VOXEL_GI_ON
#endif

#include "Core.hlsl"
#include "LightingCommon.hlsl"
#include "Atmosphere.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AreaLighting.hlsl"

Texture2D<float4> _CloudTransmittance, _ReflectionBuffer;
Texture2D<float3> _CloudShadow;

float _FlatAmbient;

// Volumetric Lighting
float4x4 _WorldToCloudShadow;
float4 _CloudShadow_TexelSize;
float _CloudDepthInvScale;

float3 _GroundColor;
float _CloudMaxAvgExtinction, _CloudMaxOpticalDepth;

Texture2D<float2> _WaterShadows;
float4x4 _WaterShadowMatrix;

StructuredBuffer<float3x4> _DirectionalShadowMatrices;
StructuredBuffer<float4x4> _SpotlightShadowMatrices, _AreaShadowMatrices;
Buffer<float4> _CullingSpheres;
TextureCubeArray<float> _PointShadows;
Texture2DArray<float> _SpotlightShadows, _AreaShadows;
float _ShadowPcfRadius;
uint _CascadeCount;

Texture2DArray<float> _DirectionalShadows, _ExponentialShadowmap;
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

float VoxelGI(float3 positionWS, float3 normalWS)
{
    #ifdef VOXEL_GI_ON
		float3 voxelPos = MultiplyPoint(_WorldToVoxel, positionWS).xyz;

		if(all(voxelPos > 0.0 && voxelPos < 1.0))
		{
			float3 uv = voxelPos;

			// Convert to integer, wrap, convert back to normalized
			uv = mod(uv * _VoxelResolution + _VoxelOffset, _VoxelResolution);
        
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
			uv = mod(uv * _VoxelResolution + _VoxelOffset, _VoxelResolution) / _VoxelResolution;
    
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
Buffer<float4> _AmbientSh;

float3 AmbientLight(float3 n, float3 albedo, float occlusion)
{
    if (_FlatAmbient)
        return 12.5;

    // Calculate the zonal harmonics expansion for V(x, ωi)*(n.l)
	float t = FastACosPos(sqrt(saturate(1.0 - occlusion)));
    float a = sin(t);
    float b = cos(t);

	float A0 = sqrt(4.0 * PI / 1.0) * (sqrt(1.0 * PI) / 2.0) * a * a;
	float A1 = sqrt(4.0 * PI / 3.0) * (sqrt(3.0 * PI) / 3.0) * (1.0 - b * b * b);
	float A2 = sqrt(4.0 * PI / 5.0) * (sqrt(5.0 * PI) / 16.0) * a * a * (2.0 + 6.0 * b * b);

    float3 irradiance =
        _AmbientSh[0].xyz * A0  +
        _AmbientSh[1].xyz * A1 * n.y +
        _AmbientSh[2].xyz * A1 * n.z +
        _AmbientSh[3].xyz * A1 * n.x +
        _AmbientSh[4].xyz * A2 * (n.y * n.x) +
        _AmbientSh[5].xyz * A2 * (n.y * n.z) +
        _AmbientSh[6].xyz * A2 * (3.0 * n.z * n.z - 1.0) +
        _AmbientSh[7].xyz * A2 * (n.z * n.x) +
        _AmbientSh[8].xyz * A2 * (n.x * n.x - n.y * n.y);

    return max(irradiance, 0) * INV_PI;
}

float3 CornetteShanksZonalHarmonics(float g)
{
    float A0 = 0.282095f;
    float A1 = 0.293162f * g * (4.0f + (g * g)) / (2.0f + (g * g));
    float A2 = (0.126157f + 1.44179f * (g * g) + 0.324403f * (g * g) * (g * g)) / (2.0f + (g * g));
    return float3(A0, A1, A2);
}

float3 AmbientLightCornetteShanks(float3 n, float gBack, float gFront, float gBlend)
{
    if (_FlatAmbient)
        return 12.5;

    float3 zh0 = CornetteShanksZonalHarmonics(gBack);
    float3 zh1 = CornetteShanksZonalHarmonics(gFront);
    float3 A = lerp(zh0, zh1, gBlend);

    float3 irradiance =
        _AmbientSh[0].xyz * A.x +
        _AmbientSh[1].xyz * A.y * n.y +
        _AmbientSh[2].xyz * A.y * n.z +
        _AmbientSh[3].xyz * A.y * n.x +
        _AmbientSh[4].xyz * A.z * (n.y * n.x) +
        _AmbientSh[5].xyz * A.z * (n.y * n.z) +
        _AmbientSh[6].xyz * A.z * (3.0 * n.z * n.z - 1.0) +
        _AmbientSh[7].xyz * A.z * (n.z * n.x) +
        _AmbientSh[8].xyz * A.z * (n.x * n.x - n.y * n.y);

    return max(irradiance, 0);
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

float DirectionalLightShadow(float3 positionWS, uint shadowIndex, float jitter = 0.5, bool softShadows = false, bool exponentialShadows = false)
{
	for (uint i = 0; i < _CascadeCount; i++)
	{
		uint slice = shadowIndex * _CascadeCount + i;

		float3x4 cascadeData = _DirectionalShadowMatrices[slice];
		float3 positionLS = MultiplyPoint3x4(cascadeData, positionWS);

		if (any(positionLS < 0.0 || positionLS > 1.0))
			continue;

		if (exponentialShadows)
		{
			float occluder = _ExponentialShadowmap.SampleLevel(_LinearClampSampler, float3(positionLS.xy, slice), 0.0);
			float receiver = exp2(-(1.0 - positionLS.z) * _ShadowmapExponent);
			return saturate(occluder * receiver);
		}
		else if (softShadows)
		{
            // Vogel disk randomised PCF
			float sum = 0.0;
			for (uint j = 0; j < _PcfSamples; j++)
			{
				float2 offset = VogelDiskSample(j, _PcfSamples, jitter * TWO_PI) * _ShadowPcfRadius;
				sum += _DirectionalShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(positionLS.xy + offset, slice), positionLS.z);
			}

			return sum / _PcfSamples; //return PCSSArray(positionLS, slice, _Softness, dither);
		}
		else
		{
			return _DirectionalShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(positionLS.xy, slice), positionLS.z);
		}
	}
    
	return 1.0;
}

float3 DirectionalLightColor(uint index, float3 positionWS, bool softShadows = false, float jitter = 0.5, bool applyShadow = true, bool exponentialShadows = false, bool atmosphereTransmittance = true)
{
    DirectionalLightData  lightData = _DirectionalLightData[index];
    
    // Earth shadow 
	float2 intersections;
	if (IntersectRaySphere(positionWS + _PlanetOffset, lightData.Direction, _PlanetRadius, intersections) && intersections.x >= 0.0)
		return 0.0;
    
	float attenuation = 1.0;
    
    #ifdef WATER_SHADOW_ON
	    float shadowDistance = max(0.0, -_WorldSpaceCameraPos.y - positionWS.y) / max(1e-6, saturate(lightData.Direction.y));
	    float3 shadowPosition = MultiplyPoint3x4(_WaterShadowMatrix, positionWS);
	    if (index == 0 && all(shadowPosition > 0 && shadowPosition < 1))
	    {
		    float shadowDepth = _WaterShadows.SampleLevel(_LinearClampSampler, shadowPosition.xy, 0.0).r;
		    shadowDistance = saturate(shadowDepth - shadowPosition.z) * _WaterShadowFar;
	    }
    
	    attenuation *= exp(-shadowDistance * _WaterExtinction);
        if(attenuation == 0.0)
			return 0.0;
    #endif
    
    if(applyShadow && index == 0)
    {
		attenuation *= CloudTransmittanceLevelZero(positionWS);
		if (attenuation == 0.0)
			return 0.0;
	}

    if (applyShadow && lightData.ShadowIndex != UINT_MAX)
    {
		attenuation *= DirectionalLightShadow(positionWS, lightData.ShadowIndex, jitter, softShadows, exponentialShadows);
		if (attenuation == 0.0)
			return 0.0;
	}
    
	float3 color = attenuation;
	if (atmosphereTransmittance)
		color *= TransmittanceToAtmosphere(positionWS + _PlanetOffset, lightData.Direction, _LinearClampSampler);

	return color * ApplyExposure(lightData.Color);
}

LightCommon GetLightColor(LightData lightData, float3 positionWS, float dither, bool softShadows)
{
    LightCommon light;
    light.color = ApplyExposure(lightData.color);

    float rangeAttenuationScale = rcp(Square(lightData.range));
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
        if (dot(lightData.forward, lightVector) >= FLT_EPS)
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
    if (lightData.shadowIndex != UINT_MAX)
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
				float2 offset = VogelDiskSample(j, _PcfSamples, dither * TWO_PI) * _ShadowPcfRadius;
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
        float2 offset = VogelDiskSample(i, _BlockerSamples, dither * TWO_PI) * searchWidth;
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
        float2 offset = VogelDiskSample(i, _PcfSamples, dither * TWO_PI) * filterRadiusUV;
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
        float2 offset = VogelDiskSample(i, _BlockerSamples, dither * TWO_PI) * searchWidth;
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
        float2 offset = VogelDiskSample(i, _PcfSamples, dither * TWO_PI) * filterRadiusUV;
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