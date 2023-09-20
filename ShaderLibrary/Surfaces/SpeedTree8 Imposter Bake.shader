Shader "Hidden/Surface/Nature/SpeedTree 8 Imposter Bake"
{
    Properties
    {
        [Toggle] _Cutout("Cutout", Float) = 0.0

        [Toggle] _Subsurface("Subsurface", Float) = 0.0
        [Enum(CullMode)] _Cull("Cull", Float) = 0.0

        [NoScaleOffset] _MainTex ("Base (RGB) Transparency (A)", 2D) = "white" {}
        [NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
        [NoScaleOffset] _ExtraTex ("Smoothness (R), Metallic (G), AO (B)", 2D) = "(0.5, 0.0, 1.0)" {}
        [NoScaleOffset] _SubsurfaceTex ("Subsurface (RGB)", 2D) = "black" {}
    }

    SubShader
    {
        Tags
        {
            "TextureCount" = "4"

            "TextureName0" = "Albedo Opacity"
            "TextureFormat0" = "ARGB32"
            "TexturesRGB0" = "True"
            "TextureProperty0" = "_MainTex"

            "TextureName1" = "Normal Smoothness"
            "TextureFormat1" = "ARGB32"
            "TexturesRGB1" = "False"
            "TextureProperty1"="_NormalSmoothness"

            "TextureName2" = "Height"
            "TextureFormat2" = "R8"
            "TexturesRGB2" = "False"
            "TextureProperty2" = "_ParallaxMap"

            "TextureName3" = "Subsurface Occlusion"
            "TextureFormat3" = "ARGB32"
            "TexturesRGB3" = "True"
            "TextureProperty3" = "_SubsurfaceOcclusion"
        }

        Pass
        {
            Cull [_Cull]

            Name "ImposterBake"
            Tags{ "LightMode" = "ImposterBake" }

            HLSLPROGRAM
            #pragma vertex Vertex
			#pragma fragment Fragment
            #pragma shader_feature_local_fragment _CUTOUT_ON
            #include "SpeedTree8ImposterBake.hlsl"
            ENDHLSL
        }
    }

    Dependency "ImposterShader" = "Surface/Nature/SpeedTree 8 Imposter"
}