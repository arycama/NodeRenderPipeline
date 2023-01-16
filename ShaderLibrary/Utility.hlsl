#ifndef UTILITY_INCLUDED
#define UTILITY_INCLUDED

#include "Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"

Texture2D<float2> _UnitBlueNoise2D, _BlueNoise2D;
Texture2D<float> _BlueNoise1D;

TEMPLATE_1_FLT(RandomPerFrameSeed, u, return u)
//TEMPLATE_1_FLT(RandomPerFrameSeed, u, return frac((u + (_FrameIndex / 64) * GOLDEN_RATIO)))

TEMPLATE_1_FLT(Sum, x, return dot(x, 1.0))

float BlueNoise1D(uint2 pixelCoord)
{
	return RandomPerFrameSeed(_BlueNoise1D[pixelCoord % 128]);
}

float2 BlueNoise2D(uint2 pixelCoord)
{
	return RandomPerFrameSeed(_BlueNoise2D[pixelCoord % 128]);
}

float2 UnitBlueNoise2D(uint2 pixelCoord)
{
	return normalize(2.0 * RandomPerFrameSeed(_UnitBlueNoise2D[pixelCoord % 128]) - 1.0);
}

uint4 PcgHash(uint4 state)
{
	uint4 word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

uint3 PcgHash(uint3 state)
{
	uint3 word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

uint2 PcgHash(uint2 state)
{
	uint2 word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

uint PcgHash(uint state)
{
	uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

float4 ComputeScreenPos(float4 positionCS)
{
	positionCS.xy = (positionCS.xy * float2(1, _ProjectionParams.x) + positionCS.w) * 0.5;
	return positionCS;
}

float3 ApplyExposure(float3 color)
{
	#ifdef REFLECTION_PROBE_RENDERING
		return color * _ExposureValue;
	#else
		return color * _Exposure[uint2(0, 0)];
	#endif
}

float Remap(float v, float pMin, float pMax, float nMin, float nMax) { return nMin + (v - pMin) / (pMax - pMin) * (nMax - nMin); }
float2 Remap(float2 v, float2 pMin, float2 pMax, float2 nMin, float2 nMax) { return nMin + (v - pMin) / (pMax - pMin) * (nMax - nMin); }
float3 Remap(float3 v, float3 pMin, float3 pMax, float3 nMin, float3 nMax) { return nMin + (v - pMin) / (pMax - pMin) * (nMax - nMin); }
float4 Remap(float4 v, float4 pMin, float4 pMax, float4 nMin, float4 nMax) { return nMin + (v - pMin) / (pMax - pMin) * (nMax - nMin); }

// Z buffer to linear 0..1 depth (0 at near plane, 1 at far plane).
// zBufferParam = { (f-n)/n, 1, (f-n)/n*f, 1/f }
float Linear01DepthFromNearPlane(float depth, float4 zBufferParam)
{
	float near = _ProjectionParams.y;
	float far = _ProjectionParams.z;

	float eye = LinearEyeDepth(depth, zBufferParam);
	return (eye - near) / (far - near);

	zBufferParam.x = -1 + far / near;
	zBufferParam.y = 1;
	zBufferParam.z = (-1 + far / near) / far;
	zBufferParam.w = 1 / far;

	// from camera
	return 1.0 / (zBufferParam.x * depth + zBufferParam.y);

	//return 1.0 / (zBufferParam.x + zBufferParam.y / depth);
}

// Common Utilities, should probably be a seperate file
TEMPLATE_1_FLT(Square, x, return (x) * (x))
TEMPLATE_1_FLT(SqrLength, x, return dot(x, x))

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
	float twoSqrRadius = 2.0 * Square(radius);
	return exp(-Square(x) * rcp(twoSqrRadius)) * rcp(sqrt(PI * twoSqrRadius));
}

float2 VogelDiskSample(int sampleIndex, int samplesCount, float phi)
{
	float GoldenAngle = 2.4f;

	float r = sqrt(sampleIndex + 0.5f) / sqrt(samplesCount);
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

float Max4(float4 x)
{
	float2 temp = max(x.xy, x.zw);
	return max(temp.x, temp.y);
}

float Min4(float4 x)
{
	float2 temp = min(x.xy, x.zw);
	return min(temp.x, temp.y);
}

float Max3(float3 x)
{
	return Max3(x.x, x.y, x.z);
}

float Min3(float3 x)
{
	return Min3(x.x, x.y, x.z);
}

float Max2(float2 x)
{
	return max(x.x, x.y);
}

float Min2(float2 x)
{
	return min(x.x, x.y);
}

// Normalize if bool is set to true
float3 ConditionalNormalize(float3 input, bool doNormalize)
{
	return doNormalize ? SafeNormalize(input) : input;
}

// Divides a 4-component vector by it's w component
float4 PerspectiveDivide(float4 input)
{
	return float4(input.xyz * rcp(input.w), input.w);
}

FragmentInputImage VertexImage(uint vertexID : SV_VertexID)
{
	FragmentInputImage output;
	output.positionCS = GetFullScreenTriangleVertexPosition(vertexID, UNITY_RAW_FAR_CLIP_VALUE);
	output.uv = GetFullScreenTriangleTexCoord(vertexID);
	return output;
}

float3 SampleTexture2DBicubic(Texture2D<float3> tex, SamplerState smp, float2 coord, float4 texSize)
{
	float2 xy = coord * texSize.xy + 0.5;
	float2 ic = floor(xy);
	float2 fc = frac(xy);

	float2 weights[2], offsets[2];
	BicubicFilter(fc, weights, offsets);

	return weights[0].y * (weights[0].x * tex.SampleLevel(smp, (ic + float2(offsets[0].x, offsets[0].y) - 0.5) * texSize.zw, 0.0) +
                           weights[1].x * tex.SampleLevel(smp, (ic + float2(offsets[1].x, offsets[0].y) - 0.5) * texSize.zw, 0.0)) +
           weights[1].y * (weights[0].x * tex.SampleLevel(smp, (ic + float2(offsets[0].x, offsets[1].y) - 0.5) * texSize.zw, 0.0) +
                           weights[1].x * tex.SampleLevel(smp, (ic + float2(offsets[1].x, offsets[1].y) - 0.5) * texSize.zw, 0.0));
}

float3 Reinhard(float3 color)
{
	return color * rcp(1.0 + Luminance(color));
}

float3 InverseReinhard(float3 color)
{
	return color * rcp(1.0 - Luminance(color));
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
	float2 localUv = frac(uv * textureSize - 0.5);
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

uint PermuteState(uint state)
{
	return state * 747796405u + 2891336453u;
}

float2 ConstructFloat(uint2 m)
{
	return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1;
}

float3 ConstructFloat(uint3 m)
{
	return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1;
}

float4 ConstructFloat(uint4 m)
{
	return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1;
}

uint RandomUint(uint value, uint seed = 0)
{
	uint state = PermuteState(value);
	return PcgHash(state + seed);
}

float RandomFloat(uint value, uint seed = 0)
{
	uint start = PermuteState(value) + seed;
	uint state = PermuteState(start);
	return ConstructFloat(PcgHash(state));
}

float2 RandomFloat2(uint value, uint seed = 0)
{
	uint start = PermuteState(value) + seed;

	uint2 state;
	state.x = PermuteState(start);
	state.y = PermuteState(state.x);
	return ConstructFloat(PcgHash(state));
}

float3 RandomFloat3(uint value, uint seed = 0)
{
	uint start = PermuteState(value) + seed;

	uint3 state;
	state.x = PermuteState(start);
	state.y = PermuteState(state.x);
	state.z = PermuteState(state.y);
	return ConstructFloat(PcgHash(state));
}

float4 RandomFloat4(uint value, uint seed, out uint outState)
{
	uint start = PermuteState(value) + seed;

	uint4 state;
	state.x = PermuteState(start);
	state.y = PermuteState(state.x);
	state.z = PermuteState(state.y);
	state.w = PermuteState(state.z);
	outState = state.w;
	return ConstructFloat(PcgHash(state));
}

float4 RandomFloat4(uint value, uint seed = 0)
{
	uint state;
	return RandomFloat4(value, seed, state);
}

// Variant with float3 for f90
float3 F_Schlick(float3 f0, float3 f90, float u)
{
	float x = 1.0 - u;
	float x2 = x * x;
	float x5 = x * x2 * x2;
	return f0 * (1.0 - x5) + (f90 * x5); // sub mul mul mul sub mul mad*3
}

float GaussianFloat(uint seed)
{
	float2 u = RandomFloat2(seed);
	return sqrt(-2.0 * log(u.x)) * cos(TWO_PI * u.y);
}

float2 GaussianFloat2(uint seed)
{
	float2 u = RandomFloat2(seed);
	float r = sqrt(-2.0 * log(u.x));
	float theta = 2.0 * PI * u.y;
	return float2(r * sin(theta), r * cos(theta));
}

float4 GaussianFloat4(uint seed)
{
	float4 u = RandomFloat4(seed);
	
	float2 r = sqrt(-2.0 * log(u.xz));
	float2 theta = 2.0 * PI * u.yw;
	return float4(r.x * sin(theta.x), r.x * cos(theta.x), r.y * sin(theta.y), r.y * cos(theta.y));
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

float LengthToRoughness(float len)
{
	len = 3.0 * len - 2.0; // Remap from 2/3:1 to 0:1
	float2 uv = Remap01ToHalfTexelCoord(float2(len, 0.5), float2(256.0, 1));
	return _LengthToRoughness.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float LengthToPerceptualRoughness(float len)
{
	return RoughnessToPerceptualRoughness(LengthToRoughness(len));
}

float LengthToSmoothness(float len)
{
	return RoughnessToPerceptualSmoothness(LengthToRoughness(len));
}

float RoughnessToNormalLength(float roughness)
{
	if(roughness < 1e-3)
		return 1.0;
	if (roughness >= 1.0)
		return 2.0 / 3.0;

	float a = sqrt(saturate(1.0 - pow(roughness, 2.0)));
	return (a - (1.0 - a * a) * atanh(a)) / (a * a * a);
}

float PerceptualRoughnessToNormalLength(float perceptualRoughness)
{
	return RoughnessToNormalLength(PerceptualRoughnessToRoughness(perceptualRoughness));
}

float SmoothnessToNormalLength(float smoothness)
{
	return RoughnessToNormalLength(PerceptualSmoothnessToRoughness(smoothness));
}

float3 UnpackNormalSNorm(float2 data)
{
	float3 normal;
	normal.xy = data.xy;
	normal.z = sqrt(saturate(1.0 - dot(normal.xy, normal.xy)));
	return normal;
}

float EyeToDeviceDepth(float eyeDepth, float4 zBufferParam)
{
	return (1.0 - eyeDepth * zBufferParam.w) * rcp(eyeDepth * zBufferParam.z);
}

float Linear01ToDeviceDepth(float eyeDepth, float4 zBufferParam)
{
	return (1.0 - eyeDepth * zBufferParam.y) * rcp(eyeDepth * zBufferParam.x);
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
	float3 centerColor = input.SampleLevel(_LinearClampSampler, float2(tc12.x, tc12.y), 0.0);
	
	float2 tc0 = rtMetrics.zw * (centerPosition - 1.0);
	float2 tc3 = rtMetrics.zw * (centerPosition + 2.0);
	float4 color = float4(input.SampleLevel(_LinearClampSampler, float2(tc12.x, tc0.y), 0.0), 1.0) * (w12.x * w0.y) +
					float4(input.SampleLevel(_LinearClampSampler, float2(tc0.x, tc12.y), 0.0), 1.0) * (w0.x * w12.y) +
					float4(centerColor, 1.0) * (w12.x * w12.y) +
					float4(input.SampleLevel(_LinearClampSampler, float2(tc3.x, tc0.y), 0.0), 1.0) * (w3.x * w12.y) +
					float4(input.SampleLevel(_LinearClampSampler, float2(tc12.x, tc3.y), 0.0), 1.0) * (w12.x * w3.y);
	return color.rgb * rcp(color.a);
}

#endif