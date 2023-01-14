Shader "Hidden/Renderpass Copy"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Linear To Gamma"

            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;

            float4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);

                float3 sRGBLo = c.rgb * 12.92;
                float3 sRGBHi = pow(c.rgb, float3(1.0 / 2.4, 1.0 / 2.4, 1.0 / 2.4)) * 1.055 - 0.055;
                float3 sRGB = (c.rgb <= 0.0031308) ? sRGBLo : sRGBHi;

                return float4(sRGB, c.a);
            }
            ENDHLSL
        }
    }
}
