Shader "Hidden/Shore Distance Field"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
		_Offset("Offset", Float) = 0
		_Cutoff("Cutoff", Range(0, 1)) = 0.5
	}

	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			Name "Generate Seed Pixels"

			HLSLPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag

			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_TexelSize;
			float _Cutoff;

			float2 frag(v2f_img i) : SV_Target
			{
				float4 color = tex2D(_MainTex, i.uv);
				float2 offsets[8] = { float2(-1, -1), float2(0, -1), float2(1, -1), float2(-1, 0), float2(1, 0), float2(-1, 1), float2(0, 1), float2(1, 1) };

				if (color.r > _Cutoff)
				{
					UNITY_UNROLL
					for (uint j = 0; j < 8; j++)
					{
						float2 uv = i.uv + _MainTex_TexelSize.xy * offsets[j];
						float neighbor = tex2D(_MainTex, uv).r;
						if (neighbor < _Cutoff)
						{
							return i.uv;
						}
					}
				}

				return -1;
			}
			ENDHLSL
		}

		Pass
		{
			Name "Jump Flood"

			HLSLPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag

			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_TexelSize;
			float _Offset;

			float2 frag(v2f_img i) : SV_Target
			{
				float2 offsets[9] = { float2(-1, -1), float2(0, -1), float2(1, -1), float2(-1, 0), float2(0, 0), float2(1, 0), float2(-1, 1), float2(0, 1), float2(1, 1) };
				float minDist = sqrt(2);
				float2 minSeed = -1;

				UNITY_UNROLL
				for (uint j = 0; j < 9; j++)
				{
					float2 uv = i.uv + offsets[j] * _MainTex_TexelSize.xy * _Offset;
					float2 seed = tex2D(_MainTex, uv);

					if (all(seed != -1))
					{
						float dist = distance(seed.xy, i.uv);
						if (dist < minDist)
						{
							minDist = dist;
							minSeed = seed;
						}
					}
				}

				return minSeed;
			}
			ENDHLSL
		}

		Pass
		{
			Name "Combine"

			HLSLPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag

			#include "UnityCG.cginc"

			sampler2D _MainTex, _SourceTex;
			float4 _MainTex_TexelSize;
			float _Cutoff, _MaxDistance;

			float GetDistance(float2 uv)
			{
				float2 seed = tex2D(_MainTex, uv);
				float dist = distance(seed, uv);

				float height = tex2D(_SourceTex, uv).r;
				if (height > _Cutoff)
				{
					dist *= -1.0;
				}

				return dist;
			}

			float4 frag(v2f_img i) : SV_Target
			{
				float2 seed = tex2D(_MainTex, i.uv);
				float dist = distance(seed, i.uv);

				float height = tex2D(_SourceTex, i.uv).r;
				if (height > _Cutoff)
				{
					dist *= -1.0;
				}

				float normalizedDepth = saturate(1 - height / _Cutoff);

				// Direction from central difference
				float right = GetDistance(i.uv + _MainTex_TexelSize.xy * float2(1, 0));
				float up = GetDistance(i.uv + _MainTex_TexelSize.xy * float2(0, 1));
				float left = GetDistance(i.uv + _MainTex_TexelSize.xy * float2(-1, 0));
				float down = GetDistance(i.uv + _MainTex_TexelSize.xy * float2(0, -1));

				float dx = right - left;
				float dy = up - down;

				float2 direction = normalize(float2(dx, dy)) * 0.5 + 0.5;

				// Normalize so that a distance of 1 covers the whole texture
				float signedDistance = clamp(dist / _MaxDistance, -1, 1) * 0.5 + 0.5;

				return float4(normalizedDepth, signedDistance, direction);
			}

			ENDHLSL
		}
	}
}