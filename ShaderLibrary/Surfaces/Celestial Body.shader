Shader "Skybox/Celestial Body"
{
    Properties
    {
        [Toggle] Limb_Darkening("Limb Darkening", Float) = 0
        _Color("Color", Color) = (1, 1, 1, 1)
        _Emission("Emission", Color) = (1, 1, 1, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.2
        _Luminance("Luminance", Vector) = (0, 0, 0, 1)
        [NoScaleOffset] _MainTex("Texture", 2D) = "white" {}
        [NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}
        _EarthAlbedo("Earth Albedo", Color) = (0.3, 0.3, 0.5, 1)
        _EdgeFade("Edge Fade", Range(0, 1)) = 0.75
    }

    SubShader
    {
        Pass
        {
            ZWrite Off

            Name "CelestialBody"
            Tags { "LightMode" = "CelestialBody" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #pragma shader_feature_local LIMB_DARKENING_ON

            #include "CelestialBody.hlsl"

            ENDHLSL
        }
    }
}