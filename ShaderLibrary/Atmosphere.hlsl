#ifndef ATMOSPHERE_INCLUDED
#define ATMOSPHERE_INCLUDED

#include "Geometry.hlsl"
#include "Math.hlsl"
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

float2 TransmittanceUv(float viewHeight, float viewZenithCosAngle)
{
	#if 1
		float H = sqrt(max(0.0, Sq(_TopRadius) - Sq(_PlanetRadius)));
		float rho = sqrt(max(0.0, Sq(viewHeight) - Sq(_PlanetRadius)));

		float discriminant = Sq(viewHeight) * (Sq(viewZenithCosAngle) - 1.0) + Sq(_TopRadius);
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

bool RayIntersectsGround(float viewHeight, float mu)
{
	return (mu < 0.0) && ((viewHeight * viewHeight * (mu * mu - 1.0) + _PlanetRadius * _PlanetRadius) >= 0.0);
}

float3 TransmittanceToPoint(float height0, float cosAngle0, float height1, float cosAngle1, SamplerState samplerState)
{
	//float2 uv0, uv1;
	
	//if (height0 >= height1)
	//{
	//	uv0 = float2(height0, -cosAngle0);
	//	uv1 = float2(height1, -cosAngle1);
	//}
	//else
	//{
	//	uv0 = float2(height1, -cosAngle1);
	//	uv1 = float2(height0, -cosAngle0);
	//}
	
	//float3 transmittance0 = _AtmosphereTransmittance.SampleLevel(samplerState, TransmittanceUv(uv0.x, uv0.y) * _AtmosphereTransmittanceRemap.xy + _AtmosphereTransmittanceRemap.zw, 0.0);
	//float3 transmittance1 = _AtmosphereTransmittance.SampleLevel(samplerState, TransmittanceUv(uv1.x, uv1.y) * _AtmosphereTransmittanceRemap.xy + _AtmosphereTransmittanceRemap.zw, 0.0);
	
	//return transmittance0 == 0.0 ? 0.0 : transmittance1 / transmittance0;
	
	float3 transmittance0, transmittance1;
	if (height0 >= height1)
	{
		transmittance0 = _AtmosphereTransmittance.SampleLevel(samplerState, TransmittanceUv(height0, -cosAngle0) * _AtmosphereTransmittanceRemap.xy + _AtmosphereTransmittanceRemap.zw, 0.0);
		transmittance1 = _AtmosphereTransmittance.SampleLevel(samplerState, TransmittanceUv(height1, -cosAngle1) * _AtmosphereTransmittanceRemap.xy + _AtmosphereTransmittanceRemap.zw, 0.0);
	}
	else
	{
		transmittance0 = _AtmosphereTransmittance.SampleLevel(samplerState, TransmittanceUv(height1, cosAngle1) * _AtmosphereTransmittanceRemap.xy + _AtmosphereTransmittanceRemap.zw, 0.0);
		transmittance1 = _AtmosphereTransmittance.SampleLevel(samplerState, TransmittanceUv(height0, cosAngle0) * _AtmosphereTransmittanceRemap.xy + _AtmosphereTransmittanceRemap.zw, 0.0);
	}
	
	return transmittance0 == 0.0 ? 0.0 : transmittance1 / transmittance0;
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
	float3 N = P / viewHeight;
	return TransmittanceToAtmosphere(viewHeight, dot(N, V), samplerState);
}

float3 AtmosphereMultiScatter(float viewHeight, float viewZenithCosAngle, SamplerState samplerState)
{
	float2 uv = TransmittanceUv(viewHeight, viewZenithCosAngle);
	return _MultipleScatter.SampleLevel(samplerState, uv, 0.0);
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


#define fmaf(a, b, c) ((a) * (b) + (c))

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
	y = fmaf(y, u, 0.288184314966202f);
	y = fmaf(y, u, 0.805188119411469f);
	y = fmaf(y, u, 1.203098773956299f);
	y = fmaf(y, u, 1.371236562728882f);
	y = fmaf(y, u, 1.312000870704651f);
	y = fmaf(y, u, 1.079175233840942f);
	y = fmaf(y, u, 0.774399876594543f);
	y = fmaf(y, u, 0.490166693925858f);
	y = fmaf(y, u, 0.275374621152878f);

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
	float mul2 = SqrtPi * rstp * e2ec; // Sqrt[Pi / (1 + Sin)] * Exp[x^2] * Erfc[x]
	float trm2 = mul2 * (zp12 * (-1.5 + tp + rtp) +
                         zm12 * 0.25 * (2 * tp - 1) * rtp);
	return trm1 + trm2;
}

float ChapmanHorizontal(float z)
{
	float zm12 = rsqrt(z); // z^(-1/2)
	float zm32 = zm12 * zm12 * zm12; // z^(-3/2)

	float p = -0.14687275046666018 + z * (0.4699928014933126 + z * 1.2533141373155001);

    // Equation 47.
	return p * zm32;
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

	//assert(ch >= 0);

	return ch;
}

#define spectrum float3

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
spectrum OptDepthSpherExpMedium(float r, float cosTheta, float R,
                                spectrum seaLvlAtt, float H, float n)
{
	float z = r * n;
	float Z = R * n;

	float ch = RescaledChapman(z, Z, cosTheta);

	return ch * H * seaLvlAtt;
}

// This variant of the function evaluates optical depth along a bounded path.
// 'r' is the radial distance from the center of the planet.
// rRcp = (1 / r).
// 'cosTheta' is the value of the dot product of the ray direction and the surface normal.
// 'dist' is the distance.
// seaLvlAtt = (sigma_t * k) is the sea-level (height = 0) attenuation coefficient.
// 'R' is the radius of the planet.
// n = (1 / H) is the falloff exponent, where 'H' is the scale height.
spectrum OptDepthSpherExpMedium(float r, float rRcp, float cosTheta, float dist, float R,
                                spectrum seaLvlAtt, float H, float n)
{
	float rX = r;
	float rRcpX = rRcp;
	float cosThetaX = cosTheta;
	float rY = RadAtDist(rX, rRcpX, cosThetaX, dist);
	float cosThetaY = CosAtDist(rX, rRcpX, cosThetaX, dist);

    // Potentially swap X and Y.
    // Convention: at the point Y, the ray points up.
	cosThetaX = (cosThetaY >= 0) ? cosThetaX : -cosThetaX;

	float zX = rX * n;
	float zY = rY * n;
	float Z = R * n;

	float chX = RescaledChapman(zX, Z, cosThetaX);
	float chY = ChapmanUpper(zY, abs(cosThetaY)) * exp(Z - zY); // Rescaling adds 'exp'

    // We may have swapped X and Y.
	float ch = abs(chX - chY);

	return ch * H * seaLvlAtt;
}

float ConvertCdfToOpticalDepth(float cdf, float viewOpacity)
{
    // Equation 24.
	return -log(1 - cdf * viewOpacity);
}

#define EPS_ABS  0.0001
#define EPS_REL  0.0001
#define MAX_ITER 4

// 'optDepth' is the value to solve for.
// 'maxOptDepth' is the maximum value along the ray, s.t. (maxOptDepth >= optDepth).
// 'maxDist' is the maximum distance along the ray.
float SampleSpherExpMedium(float optDepth, float r, float rRcp, float cosTheta, float R, float2 seaLvlAtt, float2 H, float2 n, float maxOptDepth, float maxDist)
{
	const float optDepthRcp = rcp(optDepth);
	const float2 Z = R * n;

    // Make an initial guess (homogeneous assumption).
	float t = maxDist * (optDepth * rcp(maxOptDepth));

    // Establish the ranges of valid distances ('tRange') and function values ('fRange').
	float tRange[2], fRange[2];
	tRange[0] = 0; /* -> */
	fRange[0] = 0 - optDepth;
	tRange[1] = maxDist; /* -> */
	fRange[1] = maxOptDepth - optDepth;

	uint iter = 0;
	float absDiff = optDepth, relDiff = 1;

	do // Perform a Newton–Raphson iteration.
	{
		float radAtDist = RadAtDist(r, rRcp, cosTheta, t);
		float cosAtDist = CosAtDist(r, rRcp, cosTheta, t);
        // Evaluate the function and its derivatives:
        // f  [t] = OptDepthAtDist[t] - GivenOptDepth = 0,
        // f' [t] = AttCoefAtDist[t],
        // f''[t] = AttCoefAtDist'[t] = -AttCoefAtDist[t] * CosAtDist[t] / H.
		float optDepthAtDist = 0, attAtDist = 0, attAtDistDeriv = 0;
		optDepthAtDist += OptDepthSpherExpMedium(r, rRcp, cosTheta, t, R, seaLvlAtt.x, H.x, n.x); 
		optDepthAtDist += OptDepthSpherExpMedium(r, rRcp, cosTheta, t, R, seaLvlAtt.y, H.y, n.y);
		
		optDepthAtDist = OpticalDepthFromTransmittance(TransmittanceToPoint(r, cosTheta, radAtDist, cosAtDist, _LinearClampSampler)).r;
		
		attAtDist += seaLvlAtt.x * exp(Z.x - radAtDist * n.x);
		attAtDist += seaLvlAtt.y * exp(Z.y - radAtDist * n.y);
		attAtDistDeriv -= seaLvlAtt.x * exp(Z.x - radAtDist * n.x) * n.x;
		attAtDistDeriv -= seaLvlAtt.y * exp(Z.y - radAtDist * n.y) * n.y;
		attAtDistDeriv *= cosAtDist;

		float f = optDepthAtDist - optDepth;
		float df = attAtDist;
		float ddf = attAtDistDeriv;
		float dg = df - 0.5 * f * (ddf * rcp(df));

		//assert(df > 0 && dg > 0);

#if 0
        // https://en.wikipedia.org/wiki/Newton%27s_method
        float slope = rcp(df);
#else
        // https://en.wikipedia.org/wiki/Halley%27s_method
		float slope = rcp(dg);
#endif

		float dt = -f * slope;

        // Find the boundary value we are stepping towards:
        // supremum for (f < 0) and infimum for (f > 0).
		uint sgn = asuint(f) >> 31;
		float tBound = tRange[sgn];
		float fBound = fRange[sgn];
		float tNewton = t + dt;

		bool isInRange = tRange[0] < tNewton && tNewton < tRange[1];

		if (!isInRange)
		{
            // The Newton's algorithm has effectively run out of digits of precision.
            // While it's possible to continue improving precision (to a certain degree)
            // via bisection, it is costly, and the convergence rate is low.
            // It's better to recall that, for short distances, optical depth is a
            // linear function of distance to an excellent degree of approximation.
			slope = (tBound - t) * rcp(fBound - f);
			dt = -f * slope;
			iter = MAX_ITER;
		}

		tRange[1 - sgn] = t; // Adjust the range using the
		fRange[1 - sgn] = f; // previous values of 't' and 'f'

		t = t + dt;

		absDiff = abs(optDepthAtDist - optDepth);
		relDiff = abs(optDepthAtDist * optDepthRcp - 1);

		iter++;

        // Stop when the accuracy goal has been reached.
        // Note that this uses the accuracy corresponding to the old value of 't'.
        // The new value of 't' we just computed should result in higher accuracy.
	} while ((absDiff > EPS_ABS) && (relDiff > EPS_REL) && (iter < MAX_ITER));

	return t;
}

#endif