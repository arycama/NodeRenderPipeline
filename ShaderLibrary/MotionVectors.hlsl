#ifndef MOTION_VECTORS_INCLUDED
#define MOTION_VECTORS_INCLUDED

#include "SpaceTransforms.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

// Designed to compress (-256.0, +256.0) with a signed 6e3 float
uint PackXY(float x)
{
	uint signbit = asuint(x) >> 31;
	x = clamp(abs(x / 32768.0), 0, asfloat(0x3BFFE000));
	return (f32tof16(x) + 8) >> 4 | signbit << 9;
}

float UnpackXY(uint x)
{
	return f16tof32((x & 0x1FF) << 4 | (x >> 9) << 15) * 32768.0;
}

// Designed to compress (-1.0, 1.0) with a signed 8e3 float
uint PackZ(float x)
{
	uint signbit = asuint(x) >> 31;
	x = clamp(abs(x / 128.0), 0, asfloat(0x3BFFE000));
	return (f32tof16(x) + 2) >> 2 | signbit << 11;
}

float UnpackZ(uint x)
{
	return f16tof32((x & 0x7FF) << 2 | (x >> 11) << 15) * 128.0;
}

// Pack the velocity to write to R10G10B10A2_UNORM
uint PackVelocity(float3 velocity)
{
	velocity.xy *= _ScreenSize.xy;
	return PackXY(velocity.x) | PackXY(velocity.y) << 10 | PackZ(velocity.z) << 20;
}

// Unpack the velocity from R10G10B10A2_UNORM
float3 UnpackVelocity(uint velocity)
{
	float3 result = float3(UnpackXY(velocity & 0x3FF), UnpackXY((velocity >> 10) & 0x3FF), UnpackZ(velocity >> 20));
	result.xy *= _ScreenSize.zw;
	return result;
}

float3 MotionVectorFragment(float4 nonJitteredPositionCS, float4 previousPositionCS)
{
	float3 positionSS = PerspectiveDivide(nonJitteredPositionCS).xyz;
	float3 previousPositionSS = PerspectiveDivide(previousPositionCS).xyz;
	
	positionSS.xy = positionSS.xy * 0.5 + 0.5;
	previousPositionSS.xy = previousPositionSS.xy * 0.5 + 0.5;
	
	// Subtract z in linear space
	positionSS.z = Linear01Depth(positionSS.z, _ZBufferParams);
	previousPositionSS.z = Linear01Depth(previousPositionSS.z, _ZBufferParams);
	
	return positionSS - previousPositionSS;
}

float2 UnjitterTextureUV(float2 uv, float2 currentJitterInPixels)
{
    // Note: We negate the y because UV and screen space run in opposite directions
	return uv - ddx_fine(uv) * currentJitterInPixels.x + ddy_fine(uv) * currentJitterInPixels.y;
}

#endif