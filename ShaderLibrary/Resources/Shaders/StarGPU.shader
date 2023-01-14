Shader "Hidden/StarGPU"
{
	SubShader
	{
		Blend One One
		Cull Off
		ZClip Off
		ZWrite Off
		ZTest Always

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma geometry geom
			#pragma fragment frag

			#pragma target 5.0

			#include "StarGPU.hlsl"
			ENDHLSL
		}
	}
}