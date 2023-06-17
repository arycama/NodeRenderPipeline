#ifndef COLOR_INCLUDED
#define COLOR_INCLUDED

#include "Aces.hlsl"

float Luminance(float3 color) { return dot(color, float3(0.2126729, 0.7151522, 0.0721750)); }

float3 LinearToSrgb(float3 color)
{
	float3 srgbLo = color * 12.92;
	float3 srgbHi = pow(color, rcp(2.4)) * 1.055 - 0.055;
	return color <= 0.0031308 ? srgbLo : srgbHi;
}

float3 SrgbToLinear(float3 c)
{
	float3 linearRGBLo = c / 12.92;
	float3 linearRGBHi = pow((c + 0.055) / 1.055, 2.4);
	return (c <= 0.04045) ? linearRGBLo : linearRGBHi;
}

float3 Reinhard(float3 color)
{
	return color * rcp(1.0 + Luminance(color));
}

float3 InverseReinhard(float3 color)
{
	return color * rcp(1.0 - Luminance(color));
}

// This function take a rgb color (best is to provide color in sRGB space)
// and return a YCoCg color in [0..1] space for 8bit (An offset is apply in the function)
// Ref: http://www.nvidia.com/object/float-time-ycocg-dxt-compression.html
#define YCOCG_CHROMA_BIAS (128.0 / 255.0)
float3 RGBToYCoCg(float3 rgb)
{
	float3 YCoCg;
	YCoCg.x = dot(rgb, float3(0.25, 0.5, 0.25));
	YCoCg.y = dot(rgb, float3(0.5, 0.0, -0.5)) + YCOCG_CHROMA_BIAS;
	YCoCg.z = dot(rgb, float3(-0.25, 0.5, -0.25)) + YCOCG_CHROMA_BIAS;
	return YCoCg;
}

float3 YCoCgToRGB(float3 YCoCg)
{
	float Y = YCoCg.x;
	float Co = YCoCg.y - YCOCG_CHROMA_BIAS;
	float Cg = YCoCg.z - YCOCG_CHROMA_BIAS;

	float3 rgb;
	rgb.r = Y + Co - Cg;
	rgb.g = Y + Cg;
	rgb.b = Y - Co - Cg;

	return rgb;
}

// Following function can be use to reconstruct chroma component for a checkboard YCoCg pattern
// Reference: The Compact YCoCg Frame Buffer
float YCoCgCheckBoardEdgeFilter(float centerLum, float2 a0, float2 a1, float2 a2, float2 a3)
{
	float4 lum = float4(a0.x, a1.x, a2.x, a3.x);
    float4 w = 1.0 - step(30.0 / 255.0, abs(lum - centerLum));
	float W = w.x + w.y + w.z + w.w;
    // handle the special case where all the weights are zero.
	return (W == 0.0) ? a0.y : (w.x * a0.y + w.y * a1.y + w.z * a2.y + w.w * a3.y) * rcp(W);
}

// Filmic tonemapping (ACES fitting, unless TONEMAPPING_USE_FULL_ACES is set to 1)
// Input is ACES2065-1 (AP0 w/ linear encoding)
#define TONEMAPPING_USE_FULL_ACES 0

float3 AcesTonemap(float3 aces)
{
#if TONEMAPPING_USE_FULL_ACES

    float3 oces = RRT(aces);
    float3 odt = ODT_RGBmonitor_100nits_dim(oces);
    return odt;
#else
    // --- Glow module --- //
	float saturation = rgb_2_saturation(aces);
	float ycIn = rgb_2_yc(aces);
	float s = sigmoid_shaper((saturation - 0.4) / 0.2);
	float addedGlow = 1.0 + glow_fwd(ycIn, RRT_GLOW_GAIN * s, RRT_GLOW_MID);
	aces *= addedGlow;

    // --- Red modifier --- //
	float hue = rgb_2_hue(aces);
	float centeredHue = center_hue(hue, RRT_RED_HUE);
	float hueWeight;
    {
        //hueWeight = cubic_basis_shaper(centeredHue, RRT_RED_WIDTH);
		hueWeight = smoothstep(0.0, 1.0, 1.0 - abs(2.0 * centeredHue / RRT_RED_WIDTH));
		hueWeight *= hueWeight;
	}

	aces.r += hueWeight * saturation * (RRT_RED_PIVOT - aces.r) * (1.0 - RRT_RED_SCALE);

    // --- ACES to RGB rendering space --- //
	float3 acescg = max(0.0, ACES_to_ACEScg(aces));

    // --- Global desaturation --- //
    //acescg = mul(RRT_SAT_MAT, acescg);
	acescg = lerp(dot(acescg, AP1_RGB2Y).xxx, acescg, RRT_SAT_FACTOR.xxx);

    // Luminance fitting of *RRT.a1.0.3 + ODT.Academy.RGBmonitor_100nits_dim.a1.0.3*.
    // https://github.com/colour-science/colour-unity/blob/master/Assets/Colour/Notebooks/CIECAM02_Unity.ipynb
    // RMSE: 0.0012846272106
	const float a = 2.785085;
	const float b = 0.107772;
	const float c = 2.936045;
	const float d = 0.887122;
	const float e = 0.806889;
	float3 x = acescg;
	float3 rgbPost = (x * (a * x + b)) / (x * (c * x + d) + e);

    // Scale luminance to linear code value
    // float3 linearCV = Y_2_linCV(rgbPost, CINEMA_WHITE, CINEMA_BLACK);

    // Apply gamma adjustment to compensate for dim surround
	float3 linearCV = darkSurround_to_dimSurround(rgbPost);

    // Apply desaturation to compensate for luminance difference
    //linearCV = mul(ODT_SAT_MAT, color);
	linearCV = lerp(dot(linearCV, AP1_RGB2Y).xxx, linearCV, ODT_SAT_FACTOR.xxx);

    // Convert to display primary encoding
    // Rendering space RGB to XYZ
	float3 XYZ = mul(AP1_2_XYZ_MAT, linearCV);

    // Apply CAT from ACES white point to assumed observer adapted white point
	XYZ = mul(D60_2_D65_CAT, XYZ);

    // CIE XYZ to display primaries
	linearCV = mul(XYZ_2_REC709_MAT, XYZ);

	return linearCV;
#endif
}

#endif