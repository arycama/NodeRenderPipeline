Shader "Nature/Water"
{
	Properties
	{
		[Header(Scattering)]
		[IntRange] _Steps("Steps", Range(1, 32)) = 8
		_MaxDistance("Max Scatter Distance", Float) = 128

		[Header(Tessellation)]
		_EdgeLength("Edge Length", Float) = 16
		_FrustumThreshold("Frustum Threshold", Float) = 0

		[Header(Color)]
		_Smoothness("Smoothness", Range(0, 1)) = 1
		_Color("Albedo", Color) = (1, 1, 1, 1)
		[HDR] _Extinction("Extinction", Color) = (0.6313726, 0.2352941, 0.1960784, 1)

		_RefractOffset("Refraction Strength", Range(0, 0.1)) = 0.05

		[Header(Foam)]
		_FoamSmoothness("Foam Smoothness", Range(0, 1)) = 0.2
		_FoamNormalScale("Foam Normal Scale", Range(0, 5)) = 1
		_FoamTex("Foam Texture", 2D) = "clear" {}
		[NoScaleOffset] _FoamBump("Foam Normal", 2D) = "bump" {}

		[Header(Wave Foam)]
		_WaveFoamStrength("Wave Foam Strength", Range(0, 4)) = 1
		_WaveFoamFalloff("Wave Foam Falloff", Range(0, 2)) = 0.3
		_WaveFoamSharpness("Wave Foam Sharpness", Range(0, 4)) = 1

		[Header(Shore Waves)]
		_ShoreWindAngle("Wind Angle", Range(0, 1)) = 0.5
		_ShoreWaveLength("Wave Length", Float) = 8
		_ShoreWaveHeight("Wave Height", Float) = 0.279
		_ShoreWaveSteepness("Wave Steepness", Range(0, 1)) = 1
	}

	SubShader
	{
		Pass
		{
			Name "Water"
			Tags { "LightMode"="Water" }

			Stencil
			{
				Ref 6
				Pass Replace
				WriteMask 6
			}

			HLSLPROGRAM

			#pragma vertex Vertex
			#pragma hull Hull
			#pragma domain Domain
			#pragma fragment Fragment

			#pragma target 5.0

			#define MOTION_VECTORS_ON

			#include "Water.hlsl"

			ENDHLSL
		}

		Pass
		{
			ColorMask 0
			ZClip Off

			Name "WaterShadow"
			Tags { "LightMode" = "WaterShadow" }

			HLSLPROGRAM

			#pragma vertex Vertex
			#pragma hull Hull
			#pragma domain Domain
			#pragma fragment FragmentShadow

			#pragma target 5.0

			#define WATER_SHADOW_CASTER

			#include "Water.hlsl"

			ENDHLSL
		}
	}
}