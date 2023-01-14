Shader "Hidden/Gamma Correction"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off
        //ZWrite Off
        ZTest Always

        Pass
        {
            Name "Gamma To Linear"

            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "GammaCorrection.hlsl"
            ENDHLSL
        }
    }
}