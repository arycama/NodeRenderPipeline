#ifndef SPACE_TRANSFORMS_INCLUDED
#define SPACE_TRANSFORMS_INCLUDED

#include "Core.hlsl"
#include "Utility.hlsl"

// Fast matrix muls (3 mads)
float4 MultiplyPoint(float3 p, float4x4 mat) { return p.x * mat[0] + (p.y * mat[1] + (p.z * mat[2] + mat[3])); }
float4 MultiplyPoint(float4x4 mat, float3 p) { return MultiplyPoint(p, transpose(mat)); }
float4 MultiplyPointProj(float4x4 mat, float3 p) { return PerspectiveDivide(MultiplyPoint(p, transpose(mat))); }

// 3x4, for non-projection matrices
float3 MultiplyPoint3x4(float3 p, float4x3 mat) { return p.x * mat[0] + (p.y * mat[1] + (p.z * mat[2] + mat[3])); }
float3 MultiplyPoint3x4(float4x4 mat, float3 p) { return MultiplyPoint3x4(p, transpose((float3x4) mat)); }
float3 MultiplyPoint3x4(float3x4 mat, float3 p) { return MultiplyPoint3x4(p, transpose(mat)); }

float3 MultiplyVector(float3 v, float3x3 mat, bool doNormalize) { return ConditionalNormalize(v.x * mat[0] + v.y * mat[1] + v.z * mat[2], doNormalize); }
float3 MultiplyVector(float3 v, float4x4 mat, bool doNormalize) { return MultiplyVector(v, (float3x3)mat, doNormalize); }
float3 MultiplyVector(float3x3 mat, float3 v, bool doNormalize) { return MultiplyVector(v, transpose(mat), doNormalize); }
float3 MultiplyVector(float4x4 mat, float3 v, bool doNormalize) { return MultiplyVector((float3x3) mat, v, doNormalize); }
float3 MultiplyVector(float3x4 mat, float3 v, bool doNormalize) { return MultiplyVector((float3x3) mat, v, doNormalize); }

// World Space Transforms
float3 PixelToWorld(float3 position) { return MultiplyPointProj(_InvViewProjMatrix, float3(position.xy * _ScreenSize.zw * 2 - 1, position.z)).xyz; }
float3 PixelToWorld(float2 position, float depth) { return PixelToWorld(float3(position, depth)); }
float3 ClipToWorld(float3 position) { return MultiplyPointProj(_InvViewProjMatrix, position).xyz; }
float3 ViewToWorld(float3 position) {	return MultiplyPoint3x4(_InvViewMatrix, position); }
float3 ObjectToWorld(float3 position, uint instanceID) { return MultiplyPoint3x4(GetObjectToWorld(instanceID), position); }

float3 ViewToWorldDir(float3 direction, bool doNormalize) { return ConditionalNormalize(MultiplyVector(_InvViewMatrix, direction, false), doNormalize); }
float3 ObjectToWorldDir(float3 direction, uint instanceID, bool doNormalize) { return ConditionalNormalize(MultiplyVector(GetObjectToWorld(instanceID), direction, false), doNormalize); }

// Object Space Transforms
float3 WorldToObject(float3 position, uint instanceID) { return MultiplyPoint3x4(GetWorldToObject(instanceID), position); }
float3 ViewToObject(float3 position, uint instanceID) { return WorldToObject(MultiplyPoint3x4(_InvViewMatrix, position), instanceID); }
float3 ClipToObject(float3 position, uint instanceID) { return MultiplyPoint3x4(GetWorldToObject(instanceID), ClipToWorld(position)); }
float3 WorldToObjectDir(float3 direction, uint instanceID, bool doNormalize = false) { return ConditionalNormalize(MultiplyVector(GetWorldToObject(instanceID), direction, false), doNormalize); }
float3 ViewToObjectDir(float3 direction, uint instanceID, bool doNormalize = false) { return ConditionalNormalize(WorldToObjectDir(ViewToWorldDir(direction, false), false), doNormalize); }

// View Space Transforms
float3 WorldToView(float3 position) {	return MultiplyPoint3x4(_ViewMatrix, position); }
float3 ObjectToView(float3 position, uint instanceID) { return WorldToView(ObjectToWorld(position, instanceID)); }
float3 WorldToViewDir(float3 position, bool doNormalize = false) { return ConditionalNormalize(MultiplyVector(_ViewMatrix, position, false), doNormalize); }

// Projection Space Transforms
float4 WorldToClip(float3 position) { return MultiplyPoint(_ViewProjMatrix, position); }
float4 ObjectToClip(float3 position, uint instanceID) { return WorldToClip(ObjectToWorld(position, instanceID)); }

// Transforms normal from object to world space
float3 ObjectToWorldNormal(float3 normalOS, uint instanceID, bool doNormalize)
{
#ifdef UNITY_ASSUME_UNIFORM_SCALING
    return ObjectToWorldDir(normalOS, instanceID, doNormalize);
#else
	return ConditionalNormalize(MultiplyVector(normalOS, (float3x3) GetWorldToObject(instanceID), false), doNormalize);
#endif
}

// Pixel space transforms
float4 WorldToPixel(float3 position, bool flip = true) 
{ 
	float4 clipPos = MultiplyPointProj(_ViewProjMatrix, position);
    
	// Transform to NDC (0:1)
	clipPos.xy = clipPos.xy * 0.5 + 0.5;
	
	if(!flip)
		clipPos.y = 1.0 - clipPos.y;
	
	// Multiply by screen dimensions to get final pixel coords
	clipPos.xy *= _ScreenSize.xy;
    
	return clipPos;
}

float4 ViewToPixel(float3 position, float2 resolution)
{
	float4 clipPos = MultiplyPointProj(_ProjMatrix, position);
	
	// Transform to NDC (0:1)
	clipPos.xy = clipPos.xy * 0.5 + 0.5;
	
	// Multiply by screen dimensions to get final pixel coords
	clipPos.xy *= resolution;
	return clipPos;
}

// Motion vectors
float4 WorldToClipNonJittered(float3 position) { return MultiplyPoint(_NonJitteredViewProjMatrix, position); }
float4 WorldToClipPrevious(float3 position) { return MultiplyPoint(_PrevViewProjMatrix, position); }
float4 ViewToClipPrevious(float3 position) { return MultiplyPoint(_PrevViewMatrix, position); }
float3 ObjectToWorldPrevious(float3 position, uint instanceID) { return MultiplyPoint3x4(GetPreviousObjectToWorld(instanceID), position); }

// NDC (0-1) Space Transforms
float4 WorldToNdc(float3 position) { return PerspectiveDivide(WorldToClip(position)) * float2(0.5, 1.0).xxyy + float2(0.5, 0.0).xxyy; }
float4 WorldToNdcPrevious(float3 position) { return PerspectiveDivide(WorldToClipPrevious(position)) * float2(0.5, 1.0).xxyy + float2(0.5, 0.0).xxyy; }
float4 ViewToNdcPrevious(float3 position) { return PerspectiveDivide(ViewToClipPrevious(position)) * float2(0.5, 1.0).xxyy + float2(0.5, 0.0).xxyy; }

float GetOddNegativeScale() { return unity_WorldTransformParams.w; }

float3x3 CreateTangentToWorld(float3 normal, float3 tangent, float flipSign)
{
    float sgn = flipSign * GetOddNegativeScale();
    float3 bitangent = cross(normal, tangent) * sgn;

	return float3x3(tangent, bitangent, normal);
}

#endif