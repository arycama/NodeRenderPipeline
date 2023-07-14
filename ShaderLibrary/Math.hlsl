#ifndef MATH_INCLUDED
#define MATH_INCLUDED

const static float HalfEps = 4.8828125e-4;
const static float HalfMin = 6.103515625e-5; // 2^-14, the same value for 10, 11 and 16-bit: https://www.khronos.org/opengl/wiki/Small_Float_Formats
const static float HalfMinSqrt = 0.0078125; // 2^-7 == sqrt(HALF_MIN), useful for ensuring HALF_MIN after x^2
const static float HalfMax = 65504.0;

const static float FloatEps = 5.960464478e-8; // 2^-24, machine epsilon: 1 + EPS = 1 (half of the ULP for 1.0f)
const static float FloatMin = 1.175494351e-38; // Minimum normalized positive floating-point number
const static float FloatMax = 3.402823466e+38; // Maximum representable floating-point number
const static float FloatInf = asfloat(0x7F800000);

const static uint UintMax = 0xFFFFFFFFu;
const static int IntMax = 0x7FFFFFFF;

const static float Pi = radians(180.0);
const static float TwoPi = 2.0 * Pi;
const static float FourPi = 4.0 * Pi;
const static float HalfPi = Pi / 2.0;
const static float RcpPi = rcp(Pi);
const static float RcpTwoPi = rcp(HalfPi);
const static float RcpFourPi = rcp(FourPi);
const static float RcpHalfPi = rcp(HalfPi);
const static float SqrtPi = sqrt(Pi);

float1 Sq(float1 x) { return x * x; }
float2 Sq(float2 x) { return x * x; }
float3 Sq(float3 x) { return x * x; }
float4 Sq(float4 x) { return x * x; }

float SinFromCos(float cosX) { return sqrt(saturate(1.0 - Sq(cosX))); }

// Input [0, 1] and output [0, PI/2], 9 VALU
float FastACosPos(float inX)
{
	float x = abs(inX);
	float res = (0.0468878 * x + -0.203471) * x + HalfPi; // p(x)
	return res * sqrt(1.0 - x);
}

// Input [0, 1] and output [0, PI/2], 9 VALU
float3 FastACosPos(float3 inX)
{
	float3 x = abs(inX);
	float3 res = (0.0468878 * x + -0.203471) * x + HalfPi; // p(x)
	return res * sqrt(1.0 - x);
}

// Ref: https://seblagarde.wordpress.com/2014/12/01/inverse-trigonometric-functions-gpu-optimization-for-amd-gcn-architecture/
// Input [-1, 1] and output [0, PI], 12 VALU
float FastACos(float inX)
{
	float res = FastACosPos(inX);
	return inX >= 0 ? res : Pi - res; // Undo range reduction
}

// Same cost as Acos + 1 FR
// Same error
// input [-1, 1] and output [-PI/2, PI/2]
float FastASin(float x)
{
	return HalfPi - FastACos(x);
}

// max absolute error 1.3x10^-3
// Eberly's odd polynomial degree 5 - respect bounds
// 4 VGPR, 14 FR (10 FR, 1 QR), 2 scalar
// input [0, infinity] and output [0, PI/2]
float FastATanPos(float x)
{
	float t0 = (x < 1.0) ? x : 1.0 / x;
	float t1 = t0 * t0;
	float poly = 0.0872929;
	poly = -0.301895 + poly * t1;
	poly = 1.0 + poly * t1;
	poly = poly * t0;
	return (x < 1.0) ? poly : HalfPi - poly;
}

// 4 VGPR, 16 FR (12 FR, 1 QR), 2 scalar
// input [-infinity, infinity] and output [-PI/2, PI/2]
float FastATan(float x)
{
	float t0 = FastATanPos(abs(x));
	return (x < 0.0) ? -t0 : t0;
}

#endif