// Contains some extra tessellation functions, mostly versions of Unity's built-in utils but modified to take world-space parameters 

#ifndef TESSELLATION_UTILS_INCLUDED
#define TESSELLATION_UTILS_INCLUDED

#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Core.hlsl"

// Returns true if triangle with given 3 world positions is outside of camera's view frustum.
// cullEps is distance outside of frustum that is still considered to be inside (i.e. max displacement)
bool PatchFrustumCull(float3 p0, float3 p1, float3 p2, float threshold)
{
	for (uint i = 0; i < 6; i++)
	{
		float4 plane = _CullingPlanes[i];
		
		if (any(DistanceFromPlane(p0, plane) > -threshold ||
			DistanceFromPlane(p1, plane) > -threshold ||
			DistanceFromPlane(p2, plane) > -threshold))
			return false;
	}
	
	return true;
}

// Quad variant
bool QuadFrustumCull(float3 p0, float3 p1, float3 p2, float3 p3, float threshold)
{
	for (uint i = 0; i < 6; i++)
	{
		float4 plane = _CullingPlanes[i];
		
		if (any(DistanceFromPlane(p0, plane) > -threshold ||
			DistanceFromPlane(p1, plane) > -threshold ||
			DistanceFromPlane(p2, plane) > -threshold ||
			DistanceFromPlane(p3, plane) > -threshold))
			return false;
	}
	
	return true;
}

float CalculateSphereEdgeFactor(float radius, float3 edgeCenter, float targetEdgeLength)
{
	return max(1.0, ProjectedSphereRadius(radius, edgeCenter) * _ScreenSize.x * 0.5 / targetEdgeLength);
}

float CalculateSphereEdgeFactor(float3 corner0, float3 corner1, float targetEdgeLength)
{
	float3 edgeCenter = 0.5 * (corner0 + corner1);
	float r = 0.5 * distance(corner0, corner1);
	return min(64.0, CalculateSphereEdgeFactor(r, edgeCenter, targetEdgeLength));
}

float4 BarycentricInterpolate(float4 point0, float4 point1, float4 point2, float3 weights)
{
	return point0 * weights.x + point1 * weights.y + point2 * weights.z;
}

float3 BarycentricInterpolate(float3 point0, float3 point1, float3 point2, float3 weights)
{
	return point0 * weights.x + point1 * weights.y + point2 * weights.z;
}

float2 BarycentricInterpolate(float2 point0, float2 point1, float2 point2, float3 weights)
{
	return point0 * weights.x + point1 * weights.y + point2 * weights.z;
}

float BarycentricInterpolate(float point0, float point1, float point2, float3 weights)
{
	return point0 * weights.x + point1 * weights.y + point2 * weights.z;
}

#endif