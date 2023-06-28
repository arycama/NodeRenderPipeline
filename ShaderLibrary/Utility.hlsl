#ifndef UTILITY_INCLUDED
#define UTILITY_INCLUDED

#include "Core.hlsl"
#include "Math.hlsl"

Texture2D<float2> _UnitBlueNoise2D, _BlueNoise2D;
Texture2D<float> _BlueNoise1D;

float BlueNoise1D(uint2 pixelCoord)
{
	return _BlueNoise1D[pixelCoord % 128];
}

float2 BlueNoise2D(uint2 pixelCoord)
{
	return _BlueNoise2D[pixelCoord % 128];
}

float2 UnitBlueNoise2D(uint2 pixelCoord)
{
	return normalize(2.0 * _UnitBlueNoise2D[pixelCoord % 128] - 1.0);
}

float4 ComputeScreenPos(float4 positionCS)
{
	return float4((positionCS.xy * float2(1, _ProjectionParams.x) + positionCS.w) * 0.5, positionCS.zw);
}

float3 ApplyExposure(float3 color)
{
	#ifdef REFLECTION_PROBE_RENDERING
		return color * _ExposureValue;
	#else
		return color * _Exposure[uint2(0, 0)];
	#endif
}

// Remaps a value from one range to another
float1 Remap(float1 v, float1 pMin, float1 pMax = 1.0, float1 nMin = 0.0, float1 nMax = 1.0) { return nMin + (v - pMin) * rcp(pMax - pMin) * (nMax - nMin); }
float2 Remap(float2 v, float2 pMin, float2 pMax = 1.0, float2 nMin = 0.0, float2 nMax = 1.0) { return nMin + (v - pMin) * rcp(pMax - pMin) * (nMax - nMin); }
float3 Remap(float3 v, float3 pMin, float3 pMax = 1.0, float3 nMin = 0.0, float3 nMax = 1.0) { return nMin + (v - pMin) * rcp(pMax - pMin) * (nMax - nMin); }
float4 Remap(float4 v, float4 pMin, float4 pMax = 1.0, float4 nMin = 0.0, float4 nMax = 1.0) { return nMin + (v - pMin) * rcp(pMax - pMin) * (nMax - nMin); }

float Remap01(float x, float rcpLength, float startTimesRcpLength) { return saturate(x * rcpLength - startTimesRcpLength); }

float1 Mod(float1 x, float1 y) { return x - y * floor(x / y); }
float2 Mod(float2 x, float2 y) { return x - y * floor(x / y); }
float3 Mod(float3 x, float3 y) { return x - y * floor(x / y); }
float4 Mod(float4 x, float4 y) { return x - y * floor(x / y); }

void Swap(inout float1 a, inout float1 b) { float1 t = a; a = b; b = t; }
void Swap(inout float2 a, inout float2 b) { float2 t = a; a = b; b = t; }
void Swap(inout float3 a, inout float3 b) { float3 t = a; a = b; b = t; }
void Swap(inout float4 a, inout float4 b) { float4 t = a; a = b; b = t; }

float SqrLength(float1 x) { return dot(x, x); }
float SqrLength(float2 x) { return dot(x, x); }
float SqrLength(float3 x) { return dot(x, x); }
float SqrLength(float4 x) { return dot(x, x); }

float3 NLerp(float3 A, float3 B, float t)
{
	return normalize(lerp(A, B, t));
}

// Projects a vector onto another vector (Assumes vectors are normalized)
float3 Project(float3 V, float3 N)
{
	return N * dot(V, N);
}

// Projects a vector onto a plane defined by a normal orthongal to the plane (Assumes vectors are normalized)
float3 ProjectOnPlane(float3 V, float3 N)
{
	return V - Project(V, N);
}

float Angle(float3 from, float3 to)
{
	return FastACos(dot(from, to));
}

float SignedAngle(float3 from, float3 to, float3 axis)
{
	float angle = Angle(from, to);
	float3 axis1 = cross(from, to);
	float sgn = sign(dot(axis1, axis));
	return angle * sgn;
}

float GaussianWeight(float x, float radius)
{
	float twoSqrRadius = 2.0 * Sq(radius);
	return exp(-Sq(x) * rcp(twoSqrRadius)) * rcp(sqrt(Pi * twoSqrRadius));
}

float2 VogelDiskSample(int sampleIndex, int samplesCount, float phi, float power = 1.0)
{
	float GoldenAngle = 2.4f;

	float r = pow(sqrt(sampleIndex + 0.5f) / sqrt(samplesCount), power);
	float theta = sampleIndex * GoldenAngle + phi;

	float sine, cosine;
	sincos(theta, sine, cosine);

	return float2(r * cosine, r * sine);
}

float3 MatrixScaleColumnMajor(float4x3 mat)
{
	return float3(length(mat[0]), length(mat[1]), length(mat[2]));
}

float3 MatrixScaleColumnMajor(float4x4 mat)
{
	return MatrixScaleColumnMajor((float4x3) mat);
}

float3 MatrixScaleRowMajor(float4x4 mat)
{
	return MatrixScaleColumnMajor(transpose(mat));
}

float3 MatrixScaleRowMajor(float3x4 mat)
{
	return MatrixScaleColumnMajor(transpose(mat));
}

float Max2(float2 x) { return max(x.x, x.y); }
float Max3(float3 x) { return max(x.x, max(x.y, x.z)); }
float Max4(float4 x) { return Max2(max(x.xy, x.zw)); }

float Min2(float2 x) { return min(x.x, x.y); }
float Min3(float3 x) { return min(x.x, min(x.y, x.z)); }
float Min4(float4 x) { return Min2(min(x.xy, x.zw)); }

// Normalize if bool is set to true
float3 ConditionalNormalize(float3 input, bool doNormalize)
{
	return doNormalize ? normalize(input) : input;
}

// Divides a 4-component vector by it's w component
float4 PerspectiveDivide(float4 input)
{
	return float4(input.xyz * rcp(input.w), input.w);
}

float4 GetFullScreenTriangleVertexPosition(uint vertexID, float z = _NearClipValue)
{
    // note: the triangle vertex position coordinates are x2 so the returned UV coordinates are in range -1, 1 on the screen.
	float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
	return float4(uv * 2.0 - 1.0, z, 1.0);
}

// Generates a triangle in homogeneous clip space, s.t.
// v0 = (-1, -1, 1), v1 = (3, -1, 1), v2 = (-1, 3, 1).
float2 GetFullScreenTriangleTexCoord(uint vertexID)
{
    return float2((vertexID << 1) & 2, 1.0 - (vertexID & 2));
}

FragmentInputImage VertexImage(uint vertexID : SV_VertexID)
{
	FragmentInputImage output;
	output.positionCS = GetFullScreenTriangleVertexPosition(vertexID, _FarClipValue);
	output.uv = GetFullScreenTriangleTexCoord(vertexID);
	return output;
}

float3 ClipToAABB(float3 history, float3 center, float3 extents, out bool wasClipped)
{
    // This is actually `distance`, however the keyword is reserved
	float3 offset = history - center;
	float3 v_unit = offset.xyz / extents.xyz;
	float3 absUnit = abs(v_unit);
	float maxUnit = Max3(absUnit);
	wasClipped = maxUnit > 1.0;
	
	if (maxUnit > 1.0)
		return center + (offset / maxUnit);
	else
		return history;
}

float3 ClipToAABB(float3 history, float3 center, float3 extents)
{
    // This is actually `distance`, however the keyword is reserved
	float3 offset = history - center;
	float3 v_unit = offset.xyz / extents.xyz;
	float3 absUnit = abs(v_unit);
	float maxUnit = Max3(absUnit);

	if (maxUnit > 1.0)
		return center + (offset / maxUnit);
	else
		return history;
}

// Seems to work better than above
float3 ClipToAABB(float3 history, float3 current, float3 center, float3 extents)
{
	float3 direction = current - history;

    // calculate intersection for the closest slabs from the center of the AABB in HistoryColour direction
	float3 intersection = ((center - sign(direction) * extents) - history) / direction;

    // clip unexpected T values
	float3 possibleT = intersection >= 0.0 ? intersection : 100.0 + 1.0;
	float t = min(100, Min3(possibleT));

    // final history colour
	return float3(t < 100 ? history + direction * t : history);
}

float4 ClipToAABB(float4 history, float4 center, float4 extents)
{
    // This is actually `distance`, however the keyword is reserved
	float4 offset = history - center;
	float3 v_unit = offset.xyz / extents.xyz;
	float3 absUnit = abs(v_unit);
	float maxUnit = Max3(absUnit);

	if (maxUnit > 1.0)
		return center + (offset / maxUnit);
	else
		return history;
}

float3 ClipHistory(float3 history, float3 color, float3 center, float3 extents, out bool wasClipped)
{
	float3 rayDirection = (color - history);
	float3 rcpDir = rcp(rayDirection);
	float tmin = Min3((center + (rcpDir >= 0.0 ? -extents : extents) - history) * rcpDir);
	wasClipped = tmin > 0.0;
	return clamp(history + rayDirection * max(0.0, tmin), center - extents, center + extents);
}

float3 ClipHistory(float3 history, float3 color, float3 center, float3 extents)
{
	bool wasClipped;
	return ClipHistory(history, color, center, extents, wasClipped);
}

float4 BilinearInterpolate(float4 texels[4], float2 center)
{
	float4 result0 = lerp(texels[3], texels[2], center.x);
	float4 result1 = lerp(texels[0], texels[1], center.x);
	return lerp(result0, result1, center.y);
}

float BilinearInterpolate(float4 texels, float2 center)
{
	float result0 = lerp(texels[3], texels[2], center.x);
	float result1 = lerp(texels[0], texels[1], center.x);
	return lerp(result0, result1, center.y);
}

float3 BilinearInterpolate(float3 texels[4], float2 center)
{
	float3 result0 = lerp(texels[3], texels[2], center.x);
	float3 result1 = lerp(texels[0], texels[1], center.x);
	return lerp(result0, result1, center.y);
}

float BilinearInterpolate(float texels[4], float2 center)
{
	float result0 = lerp(texels[3], texels[2], center.x);
	float result1 = lerp(texels[0], texels[1], center.x);
	return lerp(result0, result1, center.y);
}

float4 BilinearWeights(float2 uv)
{
	float4 weights = uv.xxyy * float4(-1, 1, 1, -1) + float4(1, 0, 0, 1);
	return weights.zzww * weights.xyyx;
}

// Gives weights for four texels from a 0-1 input position to match a gather result
float4 BilinearWeights(float2 uv, float2 textureSize)
{
	const float2 offset = 1.0 / 512.0;
	float2 localUv = frac(uv * textureSize + (-0.5 + offset));
	return BilinearWeights(localUv);
}

// Linear eye depth for four texels packed into a float4
float4 LinearEyeDepth(float4 z)
{
	return 1.0 / (_ZBufferParams.z * z + _ZBufferParams.w);
}

float4 Linear01Depth(float4 z)
{
	return 1.0 / (_ZBufferParams.x * z + _ZBufferParams.y);
}

float2 hash2(float2 p)
{
	return frac(sin(mul(float2x2(127.1, 311.7, 269.5, 183.3), p)) * 43758.5453);
}

void DoWetProcess(inout float3 Diffuse, inout float roughness, inout float3 normal, float3 worldNormal, float WetLevel, float Porosity)
{
	//float Porosity = tex2D(GreyTextures, uv).g;
	// Calc diffuse factor
	float factor = lerp(1, 0.2, Porosity);

	// Water influence on material BRDF
	Diffuse *= lerp(1.0, factor, WetLevel); // Attenuate diffuse
	roughness = lerp(0.001, roughness, lerp(1, factor, 0.5 * WetLevel));
	normal = lerp(normal, worldNormal, WetLevel); // Smooth normal map
}

float FresnelSchlickTIR(float nt, float ni, float3 n, float3 i)
{
	float R0 = (nt - ni) / (nt + ni);
	R0 *= R0;
	float CosX = dot(n, i);
	if (ni > nt)
	{
		float inv_eta = ni / nt;
		float SinT2 = inv_eta * inv_eta * (1.0f - CosX * CosX);
		if (SinT2 > 1.0f)
		{
			return 1.0f; // TIR
		}
		CosX = sqrt(1.0f - SinT2);
	}

	return R0 + (1.0f - R0) * pow(1.0 - CosX, 5.0);
}

// Converts clip-space depth to linear depth, handling orthographic and perspective
float LinearDepth(float depth)
{
	float persp = LinearEyeDepth(depth).r;
	float ortho = (_ProjectionParams.z - _ProjectionParams.y) * (1 - depth) + _ProjectionParams.y;
	return lerp(persp, ortho, unity_OrthoParams.w);
}

Texture2D<float> _NormalLength;

float atanh(float x)
{
	return 0.5 * log((1.0 + x) / (1.0 - x));
}

// Variant with float3 for f90
float3 F_Schlick(float3 f0, float u)
{
	float x = 1.0 - u;
	float x2 = x * x;
	float x5 = x * x2 * x2;
	return f0 * (1.0 - x5) + (1.0 * x5); // sub mul mul mul sub mul mad*3
}

// Projects edge bounding-sphere into clip space
float ProjectedSphereRadius(float worldRadius, float3 worldPosition)
{
	float d2 = dot(worldPosition, worldPosition);
	return worldRadius * abs(CameraAspect) * rsqrt(d2 - worldRadius * worldRadius);
}

// Total number of pixels in a texture
uint PixelCount(uint resolution)
{
	return (4 * resolution * resolution - 1) / 3;
}

// Resolution of a mip
uint MipResolution(uint mip, uint resolution)
{
	return resolution >> mip;
}

// Total number of mip levels in a texture
uint MipCount(uint resolution)
{
	return log2(resolution) + 1;
}

uint MipCount(uint2 resolution)
{
	return MipCount(max(resolution.x, resolution.y));
}

// Index at which the mip starts if the texture was laid out in 1D
uint MipOffset(uint mip, uint resolution)
{
	uint pixelCount = PixelCount(resolution);
	uint mipCount = MipCount(resolution);
	uint endMipOffset = ((1u << (2u * (mipCount - mip))) - 1u) / 3u;
	return pixelCount - endMipOffset;
}

uint MipOffset(uint mip, uint2 resolution)
{
	return MipOffset(mip, max(resolution.x, resolution.y));
}

// Converts a 1D index to a mip level
uint IndexToMip(uint index, uint resolution)
{
	uint pixelCount = PixelCount(resolution);
	uint mipCount = MipCount(resolution);
	return (uint) (mipCount - (log2(3.0 * (pixelCount - index) + 1.0) / 2.0));
}

// Converts a texture byte offset to an XYZ coordinate. (Where Z is the mip level)
uint3 TextureIndexToCoord(uint index, uint resolution)
{
	uint mip = IndexToMip(index, resolution);
	uint localCoord = index - MipOffset(mip, resolution);
	uint mipSize = MipResolution(mip, resolution);
	return uint3(localCoord % mipSize, localCoord / mipSize, mip);
}

uint TextureCoordToOffset(uint3 position, uint resolution)
{
	uint mipSize = MipResolution(position.z, resolution);
	uint coord = position.x + position.y * mipSize;
	uint mipOffset = MipOffset(position.z, resolution);
	return mipOffset + coord;
}

Texture2D<float> _LengthToRoughness;

float1 Remap01ToHalfTexelCoord(float1 coord, float1 size)
{
	const float1 start = 0.5 * rcp(size);
	const float1 len = 1.0 - rcp(size);
	return coord * len + start;
}

float2 Remap01ToHalfTexelCoord(float2 coord, float2 size)
{
	const float2 start = 0.5 * rcp(size);
	const float2 len = 1.0 - rcp(size);
	return coord * len + start;
}

float3 Remap01ToHalfTexelCoord(float3 coord, float3 size)
{
	const float3 start = 0.5 * rcp(size);
	const float3 len = 1.0 - rcp(size);
	return coord * len + start;
}

float3 UnpackNormalSNorm(float2 data)
{
	float3 normal;
	normal.xy = data.xy;
	normal.z = sqrt(saturate(1.0 - dot(normal.xy, normal.xy)));
	return normal;
}

float EyeToDeviceDepth(float eyeDepth)
{
	return (1.0 - eyeDepth * _ZBufferParams.w) * rcp(eyeDepth * _ZBufferParams.z);
}

float Linear01ToDeviceDepth(float eyeDepth)
{
	return (1.0 - eyeDepth * _ZBufferParams.y) * rcp(eyeDepth * _ZBufferParams.x);
}

// From Filmic SMAA presentation[Jimenez 2016]
// A bit more verbose that it needs to be, but makes it a bit better at latency hiding
float3 Bicubic5Tap(Texture2D<float3> input, float2 texcoord, float sharpening, float4 rtMetrics)
{
	float2 position = rtMetrics.xy * texcoord;
	float2 centerPosition = floor(position - 0.5) + 0.5;
	float2 f = position - centerPosition;
	float2 f2 = f * f;
	float2 f3 = f * f2;

	float c = sharpening;
	float2 w0 = -c * f3 + 2.0 * c * f2 - c * f;
	float2 w1 = (2.0 - c) * f3 - (3.0 - c) * f2 + 1.0;
	float2 w2 = -(2.0 - c) * f3 + (3.0 - 2.0 * c) * f2 + c * f;
	float2 w3 = c * f3 - c * f2;

	float2 w12 = w1 + w2;
	float2 tc12 = rtMetrics.zw * (centerPosition + w2 / w12);
	
	float2 tc0 = rtMetrics.zw * (centerPosition - 1.0);
	float2 tc3 = rtMetrics.zw * (centerPosition + 2.0);
	float4 color = float4(input.SampleLevel(_LinearClampSampler, float2(tc12.x, tc0.y), 0.0), 1.0) * (w12.x * w0.y) +
					float4(input.SampleLevel(_LinearClampSampler, float2(tc0.x, tc12.y), 0.0), 1.0) * (w0.x * w12.y) +
					float4(input.SampleLevel(_LinearClampSampler, float2(tc12.x, tc12.y), 0.0), 1.0) * (w12.x * w12.y) +
					float4(input.SampleLevel(_LinearClampSampler, float2(tc3.x, tc0.y), 0.0), 1.0) * (w3.x * w12.y) +
					float4(input.SampleLevel(_LinearClampSampler, float2(tc12.x, tc3.y), 0.0), 1.0) * (w12.x * w3.y);
	return color.rgb * rcp(color.a);
}

// Return view direction in tangent space, make sure tangentWS.w is already multiplied by GetOddNegativeScale()
float3 GetViewDirectionTangentSpace(float4 tangentWS, float3 normalWS, float3 viewDirWS)
{
    // must use interpolated tangent, bitangent and normal before they are normalized in the pixel shader.
    float3 unnormalizedNormalWS = normalWS;
    const float renormFactor = 1.0 / length(unnormalizedNormalWS);

    // use bitangent on the fly like in hdrp
    // IMPORTANT! If we ever support Flip on double sided materials ensure bitangent and tangent are NOT flipped.
    float crossSign = (tangentWS.w > 0.0 ? 1.0 : -1.0); // we do not need to multiple GetOddNegativeScale() here, as it is done in vertex shader
    float3 bitang = crossSign * cross(normalWS.xyz, tangentWS.xyz);

    float3 WorldSpaceNormal = renormFactor * normalWS.xyz;       // we want a unit length Normal Vector node in shader graph

    // to preserve mikktspace compliance we use same scale renormFactor as was used on the normal.
    // This is explained in section 2.2 in "surface gradient based bump mapping framework"
    float3 WorldSpaceTangent = renormFactor * tangentWS.xyz;
    float3 WorldSpaceBiTangent = renormFactor * bitang;

    float3x3 tangentSpaceTransform = float3x3(WorldSpaceTangent, WorldSpaceBiTangent, WorldSpaceNormal);
    float3 viewDirTS = mul(tangentSpaceTransform, viewDirWS);

    return viewDirTS;
}

float2 ApplyScaleOffset(float2 x, float4 scaleOffset)
{
	return x * scaleOffset.xy + scaleOffset.zw;
}

float2 ParallaxOffset1Step(float height, float amplitude, float3 viewDirTS)
{
	height = height * amplitude - amplitude / 2.0;
	float3 v = normalize(viewDirTS);
	v.z += 0.42;
	return height * (v.xy / v.z);
}

// ref http://blog.selfshadow.com/publications/blending-in-detail/
// ref https://gist.github.com/selfshadow/8048308
// Reoriented Normal Mapping
// Blending when n1 and n2 are already 'unpacked' and normalised
// assume compositing in tangent space
float3 BlendNormalRNM(float3 n1, float3 n2)
{
	float3 t = n1.xyz + float3(0.0, 0.0, 1.0);
	float3 u = n2.xyz * float3(-1.0, -1.0, 1.0);
	float3 r = (t / t.z) * dot(t, u) - u;
	return r;
} 

// Division which returns 1 for (inf/inf) and (0/0).
// If any of the input parameters are NaNs, the result is a NaN.
float SafeDiv(float numer, float denom)
{
	return (numer != denom) ? numer / denom : 1;
}

// Inserts the bits indicated by 'mask' from 'src' into 'dst'.
uint BitFieldInsert(uint mask, uint src, uint dst)
{
	return (src & mask) | (dst & ~mask);
}

// Composes a floating point value with the magnitude of 'x' and the sign of 's'.
// See the comment about FastSign() below.
float CopySign(float x, float s, bool ignoreNegZero = true)
{
	if (ignoreNegZero)
	{
		return (s >= 0) ? abs(x) : -abs(x);
	}
	else
	{
		uint negZero = 0x80000000u;
		uint signBit = negZero & asuint(s);
		return asfloat(BitFieldInsert(negZero, signBit, asuint(x)));
	}
}

// Returns -1 for negative numbers and 1 for positive numbers.
// 0 can be handled in 2 different ways.
// The IEEE floating point standard defines 0 as signed: +0 and -0.
// However, mathematics typically treats 0 as unsigned.
// Therefore, we treat -0 as +0 by default: FastSign(+0) = FastSign(-0) = 1.
// If (ignoreNegZero = false), FastSign(-0, false) = -1.
// Note that the sign() function in HLSL implements signum, which returns 0 for 0.
float FastSign(float s, bool ignoreNegZero = true)
{
	return CopySign(1.0, s, ignoreNegZero);
}

#endif