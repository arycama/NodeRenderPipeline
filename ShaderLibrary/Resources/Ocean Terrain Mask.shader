Shader "Hidden/Ocean Terrain Mask"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _OceanHeight;

            float frag (v2f_img i) : SV_Target
            {
                return tex2D(_MainTex, i.uv).r > _OceanHeight;
            }

            ENDHLSL
        }
    }
}
