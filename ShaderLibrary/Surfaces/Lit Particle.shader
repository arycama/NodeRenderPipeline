Shader "Surface/Lit Particle"
{
    Properties
    {
        [Toggle] _Test("Test", Float) = 0
        _Color("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _MainTex("Albedo", 2D) = "black" {}
        [NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}

        _EmissiveExposureWeight("Emission Exposure Weight", Range(0.0, 1.0)) = 1.0
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0)
        [NoScaleOffset] _EmissionMap("Emission", 2D) = "white" {}

        _Translucency("Translucency", Range(0, 1)) = 0.5
        _Curvature("Curvature", Range(0, 1)) = 0.5
        _DepthFade("Depth Fade", Range(0, 8)) = 1

        [Toggle] _Distortion("Distortion", Float) = 0
        _DistortionStrength("Distortion Strength", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags { "Queue"="Transparent" }

        Pass
        {
            Blend One OneMinusSrcAlpha
            Name "Forward"
            ZWrite Off

            Tags { "LightMode" = "Forward" }

            HLSLPROGRAM
			#pragma vertex Vertex
			#pragma fragment Fragment

            #pragma target 5.0

            #include "LitParticle.hlsl"
            ENDHLSL
        }
    }
}