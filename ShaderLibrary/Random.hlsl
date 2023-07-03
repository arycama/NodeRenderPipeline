#pragma once

#include "Math.hlsl"

uint PcgHash(uint state)
{
	uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

uint PcgHash(uint2 v)
{
	return PcgHash(v.x ^ PcgHash(v.y));
}

uint PcgHash(uint3 v)
{
	return PcgHash(v.x ^ PcgHash(v.yz));
}

uint PcgHash(uint4 v)
{
	return PcgHash(v.x ^ PcgHash(v.yzw));
}

uint PermuteState(uint state) { return state * 747796405u + 2891336453u; }

float1 ConstructFloat(uint1 m) { return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1; }
float2 ConstructFloat(uint2 m) { return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1; }
float3 ConstructFloat(uint3 m) { return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1; }
float4 ConstructFloat(uint4 m) { return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1; }

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

float GaussianFloat(uint seed)
{
	float2 u = RandomFloat2(seed);
	return sqrt(-2.0 * log(u.x)) * cos(TwoPi * u.y);
}

float2 GaussianFloat2(uint seed)
{
	float2 u = RandomFloat2(seed);
	float r = sqrt(-2.0 * log(u.x));
	float theta = TwoPi * u.y;
	return float2(r * sin(theta), r * cos(theta));
}

float4 GaussianFloat4(uint seed)
{
	float4 u = RandomFloat4(seed);
	
	float2 r = sqrt(-2.0 * log(u.xz));
	float2 theta = TwoPi * u.yw;
	return float4(r.x * sin(theta.x), r.x * cos(theta.x), r.y * sin(theta.y), r.y * cos(theta.y));
}

//From  Next Generation Post Processing in Call of Duty: Advanced Warfare [Jimenez 2014]
// http://advances.floattimerendering.com/s2014/index.html
float InterleavedGradientNoise(float2 pixCoord, int frameCount)
{
	const float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
	float2 frameMagicScale = float2(2.083, 4.867);
	pixCoord += frameCount * frameMagicScale;
	return frac(magic.z * frac(dot(pixCoord, magic.xy)));
}

// Ref: http://holger.dammertz.org/stuff/notes_HammersleyOnHemisphere.html
float VanDerCorputBase2(uint i)
{
	return reversebits(i) * rcp(4294967296.0); // 2^-32
}

float2 Hammersley2dSeq(uint i, uint sequenceLength)
{
	return float2(float(i) / float(sequenceLength), VanDerCorputBase2(i));
}