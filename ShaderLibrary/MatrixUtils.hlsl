#ifndef MATRIX_UTILS_INCLUDED
#define MATRIX_UTILS_INCLUDED

#include "Utility.hlsl"

const static float3x3 Identity3x3 = float3x3(1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0);

// Fast matrix muls (3 mads)
float4 MultiplyPoint(float3 p, float4x4 mat) { return p.x * mat[0] + (p.y * mat[1] + (p.z * mat[2] + mat[3])); }
float4 MultiplyPoint(float4x4 mat, float3 p) { return p.x * mat._m00_m10_m20_m30 + (p.y * mat._m01_m11_m21_m31 + (p.z * mat._m02_m12_m22_m32 + mat._m03_m13_m23_m33)); }
float4 MultiplyPointProj(float4x4 mat, float3 p) { return PerspectiveDivide(MultiplyPoint(mat, p)); }

// 3x4, for non-projection matrices
float3 MultiplyPoint3x4(float3 p, float4x3 mat) { return p.x * mat[0] + (p.y * mat[1] + (p.z * mat[2] + mat[3])); }
float3 MultiplyPoint3x4(float4x4 mat, float3 p) { return p.x * mat._m00_m10_m20 + (p.y * mat._m01_m11_m21 + (p.z * mat._m02_m12_m22 + mat._m03_m13_m23)); }
float3 MultiplyPoint3x4(float3x4 mat, float3 p) { return MultiplyPoint3x4(p, transpose(mat)); }

float3 MultiplyVector(float3 v, float3x3 mat, bool doNormalize) { return ConditionalNormalize(v.x * mat[0] + v.y * mat[1] + v.z * mat[2], doNormalize); }
float3 MultiplyVector(float3 v, float4x4 mat, bool doNormalize) { return MultiplyVector(v, (float3x3)mat, doNormalize); }
float3 MultiplyVector(float3x3 mat, float3 v, bool doNormalize) { return ConditionalNormalize(v.x * mat._m00_m10_m20 + (v.y * mat._m01_m11_m21 + (v.z * mat._m02_m12_m22)), doNormalize); }
float3 MultiplyVector(float4x4 mat, float3 v, bool doNormalize) { return MultiplyVector((float3x3) mat, v, doNormalize); }
float3 MultiplyVector(float3x4 mat, float3 v, bool doNormalize) { return MultiplyVector((float3x3) mat, v, doNormalize); }

#endif