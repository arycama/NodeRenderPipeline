#ifndef GGX_INCLUDED
#define GGX_INCLUDED

#include "Geometry.hlsl"
#include "Math.hlsl"
#include "Utility.hlsl"

void SampleGGXDir(float2 u, float3 V, float3x3 localToWorld, float roughness, out float3 L, out float NdotL, out float NdotH, out float VdotH, bool VeqN = false)
{
    // GGX NDF sampling
	float cosTheta = sqrt(SafeDiv(1.0 - u.x, 1.0 + (roughness * roughness - 1.0) * u.x));
	float phi = TwoPi * u.y;

	float3 localH = SphericalToCartesian(phi, cosTheta);

	NdotH = cosTheta;

	float3 localV;

	if (VeqN)
	{
        // localV == localN
		localV = float3(0.0, 0.0, 1.0);
		VdotH = NdotH;
	}
	else
	{
		localV = mul(V, transpose(localToWorld));
		VdotH = saturate(dot(localV, localH));
	}

    // Compute { localL = reflect(-localV, localH) }
	float3 localL = -localV + 2.0 * VdotH * localH;
	NdotL = localL.z;

	L = mul(localL, localToWorld);
}

float D_GGXNoPI(float NdotH, float roughness)
{
	float a2 = Sq(roughness);
	float s = (NdotH * a2 - NdotH) * NdotH + 1.0;

    // If roughness is 0, returns (NdotH == 1 ? 1 : 0).
    // That is, it returns 1 for perfect mirror reflection, and 0 otherwise.
	return SafeDiv(a2, s * s);
}

float D_GGX(float NdotH, float roughness)
{
	return RcpPi * D_GGXNoPI(NdotH, roughness);
}

// Note: V = G / (4 * NdotL * NdotV)
// Ref: http://jcgt.org/published/0003/02/03/paper.pdf
float V_SmithJointGGX(float NdotL, float NdotV, float roughness, float partLambdaV)
{
	float a2 = Sq(roughness);

    // Original formulation:
    // lambda_v = (-1 + sqrt(a2 * (1 - NdotL2) / NdotL2 + 1)) * 0.5
    // lambda_l = (-1 + sqrt(a2 * (1 - NdotV2) / NdotV2 + 1)) * 0.5
    // G        = 1 / (1 + lambda_v + lambda_l);

    // Reorder code to be more optimal:
	float lambdaV = NdotL * partLambdaV;
	float lambdaL = NdotV * sqrt((-NdotL * a2 + NdotL) * NdotL + a2);

    // Simplify visibility term: (2.0 * NdotL * NdotV) /  ((4.0 * NdotL * NdotV) * (lambda_v + lambda_l))
	return 0.5 / max(lambdaV + lambdaL, HalfMin);
}

// Precompute part of lambdaV
float GetSmithJointGGXPartLambdaV(float NdotV, float roughness)
{
	float a2 = Sq(roughness);
	return sqrt((-NdotV * a2 + NdotV) * NdotV + a2);
}

float V_SmithJointGGX(float NdotL, float NdotV, float roughness)
{
	float partLambdaV = GetSmithJointGGXPartLambdaV(NdotV, roughness);
	return V_SmithJointGGX(NdotL, NdotV, roughness, partLambdaV);
}

// weightOverPdf return the weight (without the Fresnel term) over pdf. Fresnel term must be apply by the caller.
void ImportanceSampleGGX(float2 u, float3 V, float3x3 localToWorld, float roughness, float NdotV, out float3 L, out float VdotH, out float NdotL, out float weightOverPdf)
{
	float NdotH;
	SampleGGXDir(u, V, localToWorld, roughness, L, NdotL, NdotH, VdotH);

    // Importance sampling weight for each sample
    // pdf = D(H) * (N.H) / (4 * (L.H))
    // weight = fr * (N.L) with fr = F(H) * G(V, L) * D(H) / (4 * (N.L) * (N.V))
    // weight over pdf is:
    // weightOverPdf = F(H) * G(V, L) * (L.H) / ((N.H) * (N.V))
    // weightOverPdf = F(H) * 4 * (N.L) * V(V, L) * (L.H) / (N.H) with V(V, L) = G(V, L) / (4 * (N.L) * (N.V))
    // Remind (L.H) == (V.H)
    // F is apply outside the function

	float Vis = V_SmithJointGGX(NdotL, NdotV, roughness);
	weightOverPdf = 4.0 * Vis * NdotL * VdotH / NdotH;
}

// Adapted from: "Sampling the GGX Distribution of Visible Normals", by E. Heitz
// http://jcgt.org/published/0007/04/01/paper.pdf
void SampleAnisoGGXVisibleNormal(float2 u, float3 V, float3x3 localToWorld, float roughnessX, float roughnessY, out float3 localV, out float3 localH, out float VdotH)
{
	localV = mul(V, transpose(localToWorld));

    // Construct an orthonormal basis around the stretched view direction
	float3x3 viewToLocal;
	viewToLocal[2] = normalize(float3(roughnessX * localV.x, roughnessY * localV.y, localV.z));
	viewToLocal[0] = (viewToLocal[2].z < 0.9999) ? normalize(cross(float3(0, 0, 1), viewToLocal[2])) : float3(1, 0, 0);
	viewToLocal[1] = cross(viewToLocal[2], viewToLocal[0]);

    // Compute a sample point with polar coordinates (r, phi)
	float r = sqrt(u.x);
	float phi = 2.0 * Pi * u.y;
	float t1 = r * cos(phi);
	float t2 = r * sin(phi);
	float s = 0.5 * (1.0 + viewToLocal[2].z);
	t2 = (1.0 - s) * sqrt(1.0 - t1 * t1) + s * t2;

    // Reproject onto hemisphere
	localH = t1 * viewToLocal[0] + t2 * viewToLocal[1] + sqrt(max(0.0, 1.0 - t1 * t1 - t2 * t2)) * viewToLocal[2];

    // Transform the normal back to the ellipsoid configuration
	localH = normalize(float3(roughnessX * localH.x, roughnessY * localH.y, max(0.0, localH.z)));

	VdotH = saturate(dot(localV, localH));
}

// GGX vsible normal sampling, isotropic variant
void SampleGGXVisibleNormal(float2 u, float3 V, float3x3 localToWorld, float roughness, out float3 localV, out float3 localH, out float VdotH)
{
	SampleAnisoGGXVisibleNormal(u, V, localToWorld, roughness, roughness, localV, localH, VdotH);
}

#endif