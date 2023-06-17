#ifndef GEOMETRY_INCLUDED
#define GEOMETRY_INCLUDED

#include "Math.hlsl"
#include "Utility.hlsl"

float3 SphericalToCartesian(float2 sinCosTheta, float2 sinCosPhi)
{
    return float3(sinCosTheta.xy * sinCosPhi.x, sinCosPhi.y);
}

// "Efficiently building a matrix to rotate one vector to another"
// http://cs.brown.edu/research/pubs/pdfs/1999/Moller-1999-EBA.pdf / https://dl.acm.org/doi/10.1080/10867651.1999.10487509
// (using https://github.com/assimp/assimp/blob/master/include/assimp/matrix3x3.inl#L275 as a code reference as it seems to be best)
float3x3 RotFromToMatrix(float3 from, float3 to)
{
	float e = dot(from, to);
	float f = abs(e); //(e < 0)? -e:e;

    // WARNING: This has not been tested/worked through, especially not for 16bit floats; seems to work in our special use case (from is always {0, 0, -1}) but wouldn't use it in general
	if (f > float(1.0 - 0.0003))
		return float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);

	float3 v = cross(from, to);
    /* ... use this hand optimized version (9 mults less) */
	float h = (1.0) / (1.0 + e); /* optimization by Gottfried Chen */
	float hvx = h * v.x;
	float hvz = h * v.z;
	float hvxy = hvx * v.y;
	float hvxz = hvx * v.z;
	float hvyz = hvz * v.y;

	float3x3 mtx;
	mtx[0][0] = e + hvx * v.x;
	mtx[0][1] = hvxy - v.z;
	mtx[0][2] = hvxz + v.y;

	mtx[1][0] = hvxy + v.z;
	mtx[1][1] = e + h * v.y * v.y;
	mtx[1][2] = hvyz - v.x;

	mtx[2][0] = hvxz - v.y;
	mtx[2][1] = hvyz + v.x;
	mtx[2][2] = e + hvz * v.z;

	return mtx;
}

float4 StdDevFromMoments(float4 m1, float4 m2)
{
	return sqrt(abs(m2 - m1 * m1));
}

bool IntersectSphereAABB(float3 position, float radius, float3 aabbMin, float3 aabbMax)
{
	float x = max(aabbMin.x, min(position.x, aabbMax.x));
	float y = max(aabbMin.y, min(position.y, aabbMax.y));
	float z = max(aabbMin.z, min(position.z, aabbMax.z));
	float distance2 = ((x - position.x) * (x - position.x) + (y - position.y) * (y - position.y) + (z - position.z) * (z - position.z));
	return distance2 < Sq(radius);
}

// This simplified version assume that we care about the result only when we are inside the box
float IntersectRayAABBSimple(float3 start, float3 dir, float3 boxMin, float3 boxMax)
{
	float3 invDir = rcp(dir);

    // Find the ray intersection with box plane
	float3 rbmin = (boxMin - start) * invDir;
	float3 rbmax = (boxMax - start) * invDir;
	float3 rbminmax = (dir > 0.0) ? rbmax : rbmin;
	return Min3(rbminmax);
}

// Plane equation: {(a, b, c) = N, d = -dot(N, P)}.
// Returns the distance from the plane to the point 'p' along the normal.
// Positive -> in front (above), negative -> behind (below).
float DistanceFromPlane(float3 p, float4 plane)
{
	return dot(float4(p, 1.0), plane);
}

// Solves the quadratic equation of the form: a*t^2 + b*t + c = 0.
// Returns 'false' if there are no float roots, 'true' otherwise.
// Ensures that roots.x <= roots.y.bool SolveQuadraticEquation(float a, float b, float c, out float2 roots)
bool SolveQuadraticEquation(float a, float b, float c, out float2 roots)
{
	float det = Sq(b) - 4.0 * a * c;

	float sqrtDet = sqrt(det);
	roots.x = (-b - sign(a) * sqrtDet) * rcp(2.0 * a);
	roots.y = (-b + sign(a) * sqrtDet) * rcp(2.0 * a);

	return det >= 0.0;
}

// Assume Sphere is at the origin (i.e start = position - spherePosition)
bool IntersectRaySphere(float3 start, float3 dir, float radius, out float2 intersections)
{
	float a = dot(dir, dir);
	float b = dot(dir, start) * 2.0;
	float c = dot(start, start) - radius * radius;

	return SolveQuadraticEquation(a, b, c, intersections);
}

// Generates an orthonormal (row-major) basis from a unit vector. TODO: make it column-major.
// The resulting rotation matrix has the determinant of +1.
// Ref: 'ortho_basis_pixar_r2' from http://marc-b-reynolds.github.io/quaternions/2016/07/06/Orthonormal.html
float3x3 GetLocalFrame(float3 localZ)
{
	float x = localZ.x;
	float y = localZ.y;
	float z = localZ.z;
	float sz = sign(z);
	float a = 1 / (sz + z);
	float ya = y * a;
	float b = x * ya;
	float c = x * sz;

	float3 localX = float3(c * x * a - 1, sz * b, c);
	float3 localY = float3(b, y * ya - sz, y);

    // Note: due to the quaternion formulation, the generated frame is rotated by 180 degrees,
    // s.t. if localZ = {0, 0, 1}, then localX = {-1, 0, 0} and localY = {0, -1, 0}.
	return float3x3(localX, localY, localZ);
}

// Construct a right-handed view-dependent orthogonal basis around the normal:
// b0-b2 is the view-normal aka reflection plane.
float3x3 GetOrthoBasisViewNormal(float3 V, float3 N, float unclampedNdotV, bool testSingularity = false)
{
	float3x3 orthoBasisViewNormal;
	if (testSingularity && (abs(1.0 - unclampedNdotV) <= FloatEps))
	{
        // In this case N == V, and azimuth orientation around N shouldn't matter for the caller,
        // we can use any quaternion-based method, like Frisvad or Reynold's (Pixar):
		orthoBasisViewNormal = GetLocalFrame(N);
	}
	else
	{
		orthoBasisViewNormal[0] = normalize(V - N * unclampedNdotV);
		orthoBasisViewNormal[2] = N;
		orthoBasisViewNormal[1] = cross(orthoBasisViewNormal[2], orthoBasisViewNormal[0]);
	}
	return orthoBasisViewNormal;
}

// This simplified version assume that we care about the result only when we are inside the sphere
// Assume Sphere is at the origin (i.e start = position - spherePosition) and dir is normalized
// Ref: http://http.developer.nvidia.com/GPUGems/gpugems_ch19.html
float IntersectRaySphereSimple(float3 start, float3 dir, float radius)
{
	float b = dot(dir, start) * 2.0;
	float c = dot(start, start) - radius * radius;
	float discriminant = b * b - 4.0 * c;

	return abs(sqrt(discriminant) - b) * 0.5;
}

float3 IntersectRayPlane(float3 rayOrigin, float3 rayDirection, float3 planeOrigin, float3 planeNormal)
{
	float dist = dot(planeNormal, planeOrigin - rayOrigin) / dot(planeNormal, rayDirection);
	return rayOrigin + rayDirection * dist;
}

// Same as above but return intersection distance and true / false if the ray hit/miss
bool IntersectRayPlane(float3 rayOrigin, float3 rayDirection, float3 planePosition, float3 planeNormal, out float t)
{
	bool res = false;
	t = -1.0;

	float denom = dot(planeNormal, rayDirection);
	if (abs(denom) > 1e-5)
	{
		float3 d = planePosition - rayOrigin;
		t = dot(d, planeNormal) / denom;
		res = (t >= 0);
	}

	return res;
}

#endif