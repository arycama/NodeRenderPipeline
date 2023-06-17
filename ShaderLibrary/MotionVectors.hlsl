#ifndef MOTION_VECTORS_INCLUDED
#define MOTION_VECTORS_INCLUDED

#include "Utility.hlsl"

float2 MotionVectorFragment(float4 nonJitteredPositionCS, float4 previousPositionCS)
{
	return (PerspectiveDivide(nonJitteredPositionCS).xy * 0.5 + 0.5) - (PerspectiveDivide(previousPositionCS).xy * 0.5 + 0.5);
}

float2 UnjitterTextureUV(float2 uv)
{
	#ifdef UNITY_PASS_SHADOWCASTER
		return uv;
	#else
		// Note: We negate the y because UV and screen space run in opposite directions
		float2 currentJitterInPixels = _Jitter * _ScreenSize.xy;
		return uv - ddx_fine(uv) * currentJitterInPixels.x + ddy_fine(uv) * currentJitterInPixels.y;
	#endif
}

#endif