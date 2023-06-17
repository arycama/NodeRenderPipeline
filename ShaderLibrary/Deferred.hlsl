#ifndef DEFERRED_INCLUDED
#define DEFERRED_INCLUDED

#include "Color.hlsl"
#include "Geometry.hlsl"
#include "Material.hlsl"
#include "MotionVectors.hlsl"
#include "Packing.hlsl"
#include "Utility.hlsl"

Texture2D<float4> _GBuffer0, _GBuffer1, _GBuffer2, _GBuffer3;
Texture2D<float3> _GBuffer4;
SamplerState _LinearMirrorSampler;

struct GBufferOut
{
	float4 gBuffer0 : SV_Target0;
	float4 gBuffer1 : SV_Target1;
	
	#ifdef REFLECTION_PROBE_RENDERING
		float3 emission : SV_Target2;
	#else
		float4 gBuffer2 : SV_Target2;
		float4 gBuffer3 : SV_Target3;
	
		#ifndef NO_EMISSION
			float3 emission : SV_Target4;
		#endif
	
		#ifdef MOTION_VECTORS_ON
			float2 motionVectors : SV_Target5;
		#endif
	#endif
};

uint2 MirrorClamp(int2 uv, uint2 resolution)
{
	uv = uv < 0 ? -uv - 1 : uv;
	bool2 flip = (uv / resolution) & 1;
	uv %= resolution;
	return flip ? resolution - uv - 1 : uv;
}

float2 GBufferPerceptualRoughness(float4 data)
{
	return data.gb;
}

float2 GBufferPerceptualRoughness(uint2 pos)
{
	return GBufferPerceptualRoughness(_GBuffer2[pos]);
}

float3 GBufferNormal(float4 data)
{
	return UnpackNormalOctQuadEncode(2.0 * Unpack888ToFloat2(data.xyz) - 1.0);
}

float3 GBufferNormal(uint2 pos)
{
	return GBufferNormal(_GBuffer1[pos]);
}

void GetAlbedoTranslucency(uint2 positionCS, out float3 albedo, out float3 translucency)
{
	float4 col = _GBuffer0[MirrorClamp(positionCS, _ScreenSize.xy)];
	float4 a0 = _GBuffer0[MirrorClamp(positionCS + uint2(1, 0), _ScreenSize.xy)];
	float4 a1 = _GBuffer0[MirrorClamp(positionCS - uint2(1, 0), _ScreenSize.xy)];
	float4 a2 = _GBuffer0[MirrorClamp(positionCS + uint2(0, 1), _ScreenSize.xy)];
	float4 a3 = _GBuffer0[MirrorClamp(positionCS - uint2(0, 1), _ScreenSize.xy)];

	float albedoChroma = YCoCgCheckBoardEdgeFilter(col.r, a0.rg, a1.rg, a2.rg, a3.rg);
	float translucencyChroma = YCoCgCheckBoardEdgeFilter(col.b, a0.ba, a1.ba, a2.ba, a3.ba);

	uint2 screenXY = positionCS.xy;
	bool pattern = (screenXY.x % 2) == (screenXY.y % 2);

	float3 albedoData = float3(col.rg, albedoChroma);
	float3 translucencyData = float3(col.ba, translucencyChroma);
	albedo = YCoCgToRGB(pattern ? albedoData.rbg : albedoData.rgb);
	translucency = YCoCgToRGB(pattern ? translucencyData.rbg : translucencyData.rgb);
}

float3 GBufferAlbedo(float2 pos)
{
	float3 albedo, translucency;
	GetAlbedoTranslucency(pos, albedo, translucency);
	return albedo;
}

float GBufferMetallic(uint2 position)
{
	float4 gbuffer2 = _GBuffer2[position];

	float metallic;
	uint tangentFlags;
	UnpackFloatInt8bit(gbuffer2.r, 8, metallic, tangentFlags);
	return metallic;
}

SurfaceData SurfaceDataFromGBuffer(uint2 positionCS)
{
	SurfaceData surface = DefaultSurface();
	
	// Load surface data from gbuffer
	float4 gbuffer0 = _GBuffer0[positionCS];
	float4 gbuffer1 = _GBuffer1[positionCS];
	float3 normalWS = GBufferNormal(gbuffer1);
	
	#ifdef REFLECTION_PROBE_RENDERING
		surface.Albedo = gbuffer0.rgb;
		surface.Metallic = gbuffer0.a;
		surface.Normal = normalWS;
		surface.PerceptualRoughness = gbuffer1.a;
		surface.tangentWS = GetLocalFrame(normalWS)[0];
		surface.Emission = _GBuffer2[positionCS].xyz;
	#else
		float4 gbuffer2 = _GBuffer2[positionCS];
		float4 gbuffer3 = _GBuffer3[positionCS];
		float3 gbuffer4 = _GBuffer4[positionCS];

		float3x3 frame = GetLocalFrame(normalWS);

		float metallic;
		uint tangentFlags;
		UnpackFloatInt8bit(gbuffer2.r, 8, metallic, tangentFlags);

		// Get the rotation angle of the actual tangent frame with respect to the default one.
		float sinOrCos = (0.5 / 256.0 * rsqrt(2)) + (255.0 / 256.0 * rsqrt(2)) * gbuffer2.a;
		float cosOrSin = sqrt(1 - sinOrCos * sinOrCos);
		bool storeSin = tangentFlags != 0;
		float sinFrame = storeSin ? sinOrCos : cosOrSin;
		float cosFrame = storeSin ? cosOrSin : sinOrCos;

		GetAlbedoTranslucency(positionCS, surface.Albedo, surface.Translucency);
		surface.Normal = GBufferNormal(gbuffer1);
		surface.PerceptualRoughness = GBufferPerceptualRoughness(gbuffer2);
		surface.Metallic = metallic;
		surface.Occlusion = gbuffer3.a;
		surface.bentNormal = GBufferNormal(gbuffer3);
		surface.Emission = gbuffer4;
		surface.Alpha = 1;
		surface.Velocity = 0;
		surface.tangentWS = sinFrame * frame[1] + cosFrame * frame[0];
	#endif
	
	return surface;
}

float4 PackGBufferAlbedoTranslucency(float3 albedo, float3 translucency, uint2 coord)
{
	float3 albedoYCoCg = RGBToYCoCg(albedo);
	float3 translucencyYCoCg = RGBToYCoCg(translucency);
	bool pattern = (coord.x % 2) == (coord.y % 2);
	float albedoData = pattern ? albedoYCoCg.b : albedoYCoCg.g;
	float translucencyData = pattern ? translucencyYCoCg.b : translucencyYCoCg.g;
	return float4(albedoYCoCg.r, albedoData, translucencyYCoCg.r, translucencyData);
}

float4 PackGBufferNormal(float3 normal, float alpha)
{
	return float4(PackFloat2To888(0.5 * PackNormalOctQuadEncode(normal) + 0.5), alpha);
}

float4 PackGBufferMetallicRoughnesTangent(float metallic, float2 perceptualRoughness, float3 tangent, float3 normal)
{
	// Anisotropy
    // Compute the rotation angle of the actual tangent frame with respect to the default one.
	float3x3 frame = GetLocalFrame(normal);
	float sinFrame = dot(tangent, frame[1]);
	float cosFrame = dot(tangent, frame[0]);
	bool quad2or4 = (sinFrame * cosFrame) < 0;

    // We need to convert the values of Sin and Cos to those appropriate for the 1st quadrant.
    // To go from Q3 to Q1, we must rotate by Pi, so taking the absolute value suffices.
    // To go from Q2 or Q4 to Q1, we must rotate by ((N + 1/2) * Pi), so we must
    // take the absolute value and also swap Sin and Cos.
	bool storeSin = (abs(sinFrame) < abs(cosFrame)) != quad2or4;
    // sin [and cos] are approximately linear up to [after] Pi/4 ± Pi.
	float sinOrCos = min(abs(sinFrame), abs(cosFrame));
    // To avoid storing redundant angles, we must convert from a node-centered representation
    // to a cell-centered one, e.i. remap: [0.5/256, 255.5/256] -> [0, 1].
	float remappedSinOrCos = Remap01(sinOrCos, sqrt(2) * 256.0 / 255.0, 0.5 / 255.0);
	float metallicSin = PackFloatInt8bit(metallic, storeSin ? 1 : 0, 8);
	return float4(metallicSin, perceptualRoughness, remappedSinOrCos);
}

GBufferOut SurfaceToGBuffer(SurfaceData surface, float2 positionCS)
{
	GBufferOut output;
	
	#ifdef REFLECTION_PROBE_RENDERING
		output.gBuffer0 = float4(surface.Albedo, surface.Metallic);
		output.gBuffer1 = PackGBufferNormal(surface.Normal, ConvertAnisotropicPerceptualRoughnessToPerceptualRoughness(surface.PerceptualRoughness));
	
		#ifndef NO_EMISSION
			output.emission = surface.Emission;
		#endif
	#else
		output.gBuffer0 = PackGBufferAlbedoTranslucency(surface.Albedo, surface.Translucency, positionCS.xy);
		output.gBuffer1 = PackGBufferNormal(surface.Normal, 0.0);
		output.gBuffer2 = PackGBufferMetallicRoughnesTangent(surface.Metallic, surface.PerceptualRoughness, surface.tangentWS, surface.Normal);
		output.gBuffer3 = PackGBufferNormal(surface.bentNormal, surface.Occlusion);
	
		#ifndef NO_EMISSION
			output.emission = surface.Emission;
		#endif
	
		#ifdef MOTION_VECTORS_ON
			output.motionVectors = surface.Velocity;
		#endif
	#endif

	return output;
}

#endif