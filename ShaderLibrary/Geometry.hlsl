#ifndef GEOMETRY_INCLUDED
#define GEOMETRY_INCLUDED

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


#endif
