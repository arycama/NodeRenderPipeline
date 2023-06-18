#ifndef VOLUMETRIC_INCLUDED
#define VOLUMETRIC_INCLUDED

#include "Math.hlsl"

float IsotropicPhaseFunction()
{
	return RcpFourPi;
}

float RayleighPhaseFunction(float cosTheta)
{
	float k = 3 / (16 * Pi);
	return k * (1 + cosTheta * cosTheta);
}

float HenyeyGreensteinPhasePartConstant(float anisotropy)
{
	float g = anisotropy;

	return RcpFourPi * (1 - g * g);
}

float HenyeyGreensteinPhasePartVarying(float anisotropy, float cosTheta)
{
	float g = anisotropy;
	float x = 1 + g * g - 2 * g * cosTheta;
	float f = rsqrt(max(x, HalfEps)); // x^(-1/2)

	return f * f * f; // x^(-3/2)
}

float HenyeyGreensteinPhaseFunction(float anisotropy, float cosTheta)
{
	return HenyeyGreensteinPhasePartConstant(anisotropy) *
           HenyeyGreensteinPhasePartVarying(anisotropy, cosTheta);
}

float CornetteShanksPhasePartConstant(float anisotropy)
{
	float g = anisotropy;

	return (3 / (8 * Pi)) * (1 - g * g) / (2 + g * g);
}

// Similar to the RayleighPhaseFunction.
float CornetteShanksPhasePartSymmetrical(float cosTheta)
{
	float h = 1 + cosTheta * cosTheta;
	return h;
}

float CornetteShanksPhasePartAsymmetrical(float anisotropy, float cosTheta)
{
	float g = anisotropy;
	float x = 1 + g * g - 2 * g * cosTheta;
	float f = rsqrt(max(x, HalfEps)); // x^(-1/2)
	return f * f * f; // x^(-3/2)
}

float CornetteShanksPhasePartVarying(float anisotropy, float cosTheta)
{
	return CornetteShanksPhasePartSymmetrical(cosTheta) *
           CornetteShanksPhasePartAsymmetrical(anisotropy, cosTheta); // h * x^(-3/2)
}

// A better approximation of the Mie phase function.
// Ref: Henyey-Greenstein and Mie phase functions in Monte Carlo radiative transfer computations
float CornetteShanksPhaseFunction(float anisotropy, float cosTheta)
{
	return CornetteShanksPhasePartConstant(anisotropy) *
           CornetteShanksPhasePartVarying(anisotropy, cosTheta);
}

float3 TransmittanceFromOpacity(float3 opacity) { return 1.0 - opacity; }
float3 TransmittanceFromOpticalDepth(float3 opticalDepth) {	return exp(-opticalDepth); }

float3 OpacityFromOpticalDepth(float3 opticalDepth) { return 1.0 - TransmittanceFromOpticalDepth(opticalDepth); }
float3 OpacityFromTransmittance(float3 transmittance) { return 1.0 - transmittance; }

float3 OpticalDepthFromTransmittance(float3 transmittance) { return -log(transmittance); }
float3 OpticalDepthFromOpacity(float3 opacity) { return OpticalDepthFromTransmittance(TransmittanceFromOpacity(opacity)); }

#endif