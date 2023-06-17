#ifndef VOLUMETRIC_INCLUDED
#define VOLUMETRIC_INCLUDED

#include "Math.hlsl"

float IsotropicPhaseFunction()
{
	return FourPi;
}

float RayleighPhaseFunction(float cosAngle)
{
	return 3.0 / 4.0 * (1 + Sq(cosAngle)) * RcpFourPi;
}

float HenyeyGreensteinPhasePartConstant(float g)
{
	return (1 - g * g) * RcpFourPi;
}

float HenyeyGreensteinPhasePartVarying(float g, float cosAngle)
{
	float x = 1 + g * g - 2 * g * cosAngle;
	float f = rsqrt(max(x, HalfEps)); // x^(-1/2)

	return f * f * f; // x^(-3/2)
}

float HenyeyGreensteinPhaseFunction(float g, float cosAngle)
{
	return HenyeyGreensteinPhasePartConstant(g) *
           HenyeyGreensteinPhasePartVarying(g, cosAngle);
}

float CornetteShanksPhasePartConstant(float g)
{
	return (3.0 / (8.0 * Pi)) * (1.0 - g * g) / (2.0 + g * g);
}

// Similar to the RayleighPhaseFunction.
float CornetteShanksPhasePartSymmetrical(float cosAngle)
{
	return 1.0 + Sq(cosAngle);
}

float CornetteShanksPhasePartAsymmetrical(float g, float cosAngle)
{
	float x = 1 + g * g - 2 * g * cosAngle;
	float f = rsqrt(max(x, HalfEps)); // x^(-1/2)
	return f * f * f; // x^(-3/2)
}

float CornetteShanksPhasePartVarying(float g, float cosAngle)
{
	return CornetteShanksPhasePartSymmetrical(cosAngle) * CornetteShanksPhasePartAsymmetrical(g, cosAngle); // h * x^(-3/2)
}

// A better approximation of the Mie phase function.
// Ref: Henyey-Greenstein and Mie phase functions in Monte Carlo radiative transfer computations
float CornetteShanksPhaseFunction(float g, float cosAngle)
{
	return CornetteShanksPhasePartConstant(g) * CornetteShanksPhasePartVarying(g, cosAngle);
}

float3 OpticalDepthFromOpacity(float3 opacity)
{
	return -log(1 - opacity);
}

float3 OpacityFromOpticalDepth(float3 opticalDepth)
{
	return 1.0 - exp(-opticalDepth);
}

#endif