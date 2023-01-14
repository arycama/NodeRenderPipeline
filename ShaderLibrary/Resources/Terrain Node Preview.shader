Shader "Hidden/Terrain Node Preview"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float2 _MinMax;
            Buffer<float2> MinMax;

            float Remap(float value, float sourceMin, float sourceMax, float destMin, float destMax)
            {
                return (value - sourceMin) / (sourceMax - sourceMin) * (destMax - destMin) + destMin;
            }

            float4 frag(v2f_img i) : SV_Target
            {
                return tex2D(_MainTex, i.uv).r;
            }

            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float2 _MinMax;
            Buffer<float2> MinMax;

            float3 heatMap(float greyValue)
            {
	            float3 heat;
                heat.r = smoothstep(0.5, 0.8, greyValue);
                if(greyValue >= 0.90) {
    	            heat.r *= (1.1 - greyValue) * 5.0;
                }
	            if(greyValue > 0.7) {
		            heat.g = smoothstep(1.0, 0.7, greyValue);
	            } else {
		            heat.g = smoothstep(0.0, 0.7, greyValue);
                }
	            heat.b = smoothstep(1.0, 0.0, greyValue);
                if(greyValue <= 0.3) {
    	            heat.b *= greyValue / 0.3;
                }
	            return heat;
            }

            float Remap(float value, float sourceMin, float sourceMax, float destMin, float destMax)
            {
                return (value - sourceMin) / (sourceMax - sourceMin) * (destMax - destMin) + destMin;
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float normalized = saturate(Remap(tex2D(_MainTex, i.uv).r, _MinMax.x, _MinMax.y, 0, 1));
                return float4(normalized.rrr, 1.0);
                // Heatmap
                return float4(heatMap(normalized), 1.0);
            }

            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;

            float4 frag(v2f_img i) : SV_Target
            {
                return float4(tex2D(_MainTex, i.uv).rgb, 1);
            }

            ENDHLSL
        }
    }
}