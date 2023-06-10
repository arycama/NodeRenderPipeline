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
	
	float3 _RayleighScatter;
	float _RayleighHeight, _MieHeight, _MieScatter, _MieAbsorption;
};

float4 _AtmosphereTransmittanceRemap, _AtmosphereMultiScatterRemap;

Texture2D<float3> _AtmosphereTransmittance;
Texture2D<float3> _MultipleScatter;

float2 TransmittanceUv(float viewHeight, float viewZenithCosAngle)
{
	#if 1
		float H = sqrt(_TopRadius * _TopRadius - _PlanetRadius * _PlanetRadius);
		float rho = sqrt(viewHeight * viewHeight - _PlanetRadius * _PlanetRadius);

		float discriminant = viewHeight * viewHeight * (viewZenithCosAngle * viewZenithCosAngle - 1.0) + _TopRadius * _TopRadius;
		float d = -viewHeight * viewZenithCosAngle + sqrt(discriminant); // Distance to atmosphere boundary

		float d_min = _TopRadius - viewHeight;
		float d_max = rho + H;
		float x_mu = (d - d_min) / (d_max - d_min);
		float x_r = rho / H;

		return float2(x_mu, x_r);
	#else
		float2 uv;
		uv.x = 0.5 * (FastATanPos(max(viewZenithCosAngle, -0.45) * tan(1.26 * 0.75)) / 0.75 + (1.0 - 0.26));
		uv.y = sqrt((viewHeight - _PlanetRadius) / _AtmosphereHeight);
		return uv;
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
		float viewZenithCosAngle = d == 0.0 ? 1.0f : (H * H - rho * rho - d * d) / (2.0 * viewHeight * d);
	
		return float2(viewZenithCosAngle, viewHeight);
	#else
		float r = uv.y * uv.y * _AtmosphereHeight + _PlanetRadius;
		float mu = tan((2.0 * uv.x - 1.0 + 0.26) * 0.75) / tan(1.26 * 0.75);
		return float2(mu, r);
	#endif
}

float2 ChapmanHorizontal(float2 z)
{
	#if 1
		return sqrt(z);
	#else
		return (1.0 / (2.0 * z) + 1.0) * sqrt(PI * z / 2.0);
	#endif
}

// Computes (Exp[x^2] * Erfc[x]) for (x >= 0).
// Range of inputs:  [0, Inf].
// Range of outputs: [0, 1].
// Max Abs Error: 0.000000969658452.
// Max Rel Error: 0.000001091639525.
float Exp2Erfc(float x)
{
	float t, u, y;

	t = 3.9788608f * rcp(x + 3.9788608f); // Reduce the range
	u = t - 0.5f; // Center around 0

	y = -0.010297533124685f;
	y = mad(y, u, 0.288184314966202f);
	y = mad(y, u, 0.805188119411469f);
	y = mad(y, u, 1.203098773956299f);
	y = mad(y, u, 1.371236562728882f);
	y = mad(y, u, 1.312000870704651f);
	y = mad(y, u, 1.079175233840942f);
	y = mad(y, u, 0.774399876594543f);
	y = mad(y, u, 0.490166693925858f);
	y = mad(y, u, 0.275374621152878f);

	return y * t; // Expand the range
}

float ChapmanUpper(float z, float absCosTheta)
{
	float sinTheta = sqrt(saturate(1 - absCosTheta * absCosTheta));

	float zm12 = rsqrt(z); // z^(-1/2)
	float zp12 = z * zm12; // z^(+1/2)

	float tp = 1 + sinTheta; // 1 + Sin
	float rstp = rsqrt(tp); // 1 / Sqrt[1 + Sin]
	float rtp = rstp * rstp; // 1 / (1 + Sin)
	float stm = absCosTheta * rstp; // Sqrt[1 - Sin] = Abs[Cos] / Sqrt[1 + Sin]
	float arg = zp12 * stm; // Sqrt[z - z * Sin], argument of Erfc
	float e2ec = Exp2Erfc(arg); // Exp[x^2] * Erfc[x]

    // Term 1 of Equation 46.
	float mul1 = absCosTheta * rtp; // Sqrt[(1 - Sin) / (1 + Sin)] = Abs[Cos] / (1 + Sin)
	float trm1 = mul1 * (1 - 0.5 * rtp);

    // Term 2 of Equation 46.
	float mul2 = sqrt(PI) * rstp * e2ec; // Sqrt[Pi / (1 + Sin)] * Exp[x^2] * Erfc[x]
	float trm2 = mul2 * (zp12 * (-1.5 + tp + rtp) +
                         zm12 * 0.25 * (2 * tp - 1) * rtp);
	return trm1 + trm2;
}

float ChapmanHorizontal(float z)
{
	return sqrt(PI * z / 2.0) * (1.0 + 3.0 / (8.0 * z) - 15.0 / (128.0 * z * z));
}

// z = (r / H), Z = (R / H).
float RescaledChapman(float z, float Z, float cosTheta)
{
	float sinTheta = sqrt(saturate(1 - cosTheta * cosTheta));

    // Cos[Pi - theta] = -Cos[theta],
    // Sin[Pi - theta] =  Sin[theta],
    // so we can just use Abs[Cos[theta]].
	float ch = ChapmanUpper(z, abs(cosTheta)) * exp(Z - z); // Rescaling adds 'exp'

	if (cosTheta < 0)
	{
        // Ch[z, theta] = 2 * Exp[z - z_0] * Ch[z_0, Pi/2] - Ch[z, Pi - theta].
        // z_0 = r_0 / H = (r / H) * Sin[theta] = z * Sin[theta].
		float z_0 = z * sinTheta;
		float chP = ChapmanHorizontal(z_0) * exp(Z - z_0); // Rescaling adds 'exp'

        // Equation 48.
		ch = 2 * chP - ch;
	}

	return ch;
}

float2 Chapman(float r, float coschi)
{
	//return float2(RescaledChapman(r / _RayleighHeight, _PlanetRadius / _RayleighHeight, coschi), RescaledChapman(r / _MieHeight, _PlanetRadius / _MieHeight, coschi));
	
	// The approximate Chapman function
	// Ch (X+h , chi ) times exp2 ( -h)
	// X - altitude of unit density
	// h - observer altitude relative to X
	// coschi - cosine of incidence angle chi
	// X and h are given units of the 50% - height
	#if 1
		float2 h = max(0.0, r - _PlanetRadius) / float2(_RayleighHeight, _MieHeight);
		float2 X = _PlanetRadius / float2(_RayleighHeight, _MieHeight);
	
		float2 c = ChapmanHorizontal(X + h);
		if (coschi >= 0.0)
		{
			// chi above horizon
			return c / (c * coschi + 1.0) * exp(-h);
		}
		else
		{
			// chi below horizon , must use identity
			float2 x0 = sqrt(1.0 - coschi * coschi) * (X + h);
			float2 c0 = sqrt(x0);
			return 2.0 * c0 * exp(X - x0) - c / (1.0 - c * coschi) * exp(-h);
		}
	#else	
		float2 x = r / float2(_RayleighHeight, _MieHeight);
		float2 m = exp(-(r - _PlanetRadius) / float2(_RayleighHeight, _MieHeight));

		float2 c = (1.0 / (2.0 * x) + 1.0) * sqrt(PI * x / 2.0);
		if (coschi >= 0.0)
		{
			// chi above horizon
			return c / ((c - 1.0) * coschi + 1.0) * m;
		}
		else
		{
			// chi below horizon , must use identity
			float2 sinChi = SinFromCos(coschi);
			return (2.0 * exp(x - x * sinChi) * ChapmanHorizontal(x * sinChi) - c / ((c - 1.0) * -coschi + 1.0)) * m;
		}
#endif
}

float3 AtmosphereOpticalDepth(float height, float cosAngle)
{
	float2 lightOpticalDepths = Chapman(height, cosAngle);
	float3 lightOpticalDepth = _RayleighScatter * _RayleighHeight * lightOpticalDepths.x; // Rayleigh
	return lightOpticalDepth + (_MieScatter + _MieAbsorption) * _MieHeight * lightOpticalDepths.y; // Mie
}

bool RayIntersectsGround(float viewHeight, float mu)
{
	return (mu < 0.0) && ((viewHeight * viewHeight * (mu * mu - 1.0) + _PlanetRadius * _PlanetRadius) >= 0.0);
}

float3 TransmittanceToAtmosphere(float height, float cosAngle)
{
	//if(RayIntersectsGround(height, cosAngle))
	//	return 0.0;
	
	return exp(-AtmosphereOpticalDepth(height, cosAngle));
}

float3 TransmittanceToPoint(float height0, float cosAngle0, float height1, float cosAngle1)
{
	float2 viewOpticalDepths;
	if (height0 >= height1)
	{
		viewOpticalDepths = Chapman(height1, -cosAngle1) - Chapman(height0, -cosAngle0);
	}
	else
	{
		viewOpticalDepths = Chapman(height0, cosAngle0) - Chapman(height1, cosAngle1);
	}
		
	float3 viewOpticalDepth = _RayleighScatter * _RayleighHeight * viewOpticalDepths.x; // Rayleigh
	viewOpticalDepth += (_MieScatter + _MieAbsorption) * _MieHeight * viewOpticalDepth.y; // Mie
		
	return exp(-viewOpticalDepth);
}

float3 TransmittanceToAtmosphere(float viewHeight, float viewZenithCosAngle, SamplerState samplerState)
{
	//return TransmittanceToAtmosphere(viewHeight, viewZenithCosAngle);
	//if (RayIntersectsGround(viewHeight, viewZenithCosAngle))
	//	return 0.0;
	
	float2 uv = TransmittanceUv(viewHeight, viewZenithCosAngle);
	return _AtmosphereTransmittance.SampleLevel(samplerState, uv, 0.0);
}

// P must be relative to planet center
float3 TransmittanceToAtmosphere(float3 P, float3 V, SamplerState samplerState)
{
	float viewHeight = length(P);
	float3 N = P / viewHeight;
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