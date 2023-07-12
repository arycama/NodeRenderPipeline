Shader "Surface/Nature/SpeedTree 8 Imposter"
{
    Properties
    {
        [Toggle] Parallax("Parallax", Float) = 1
        [Toggle] Pixel_Depth_Offset("Pixel Depth Offset", Float) = 1
		[Toggle] Octahedron("Octahedron", Float) = 0
        
		_Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
		_ImposterFrames("Frame Count", Float) = 8
        _CenterOffset("Center Offset", Vector) = (0, 0, 0, 0)

        [NoScaleOffset] _MainTex("RGB: Albedo, A: Transparency", 2DArray) = "" {}
        [NoScaleOffset] _NormalSmoothness("RGB: Object Normal, A: Smoothness", 2DArray) = "" {}
		[NoScaleOffset] _ParallaxMap("R: Depth", 2DArray) = "grey" {}
        [NoScaleOffset] _SubsurfaceOcclusion("RGB: Subsurface, A: Occlusion", 2DArray) = "" {}
    }

    SubShader
    {
        Tags 
        { 
            "DisableBatching"="True" 
            "PreviewType" = "Quad"
            "Queue"="AlphaTest"
        }

        HLSLINCLUDE
            #pragma target 5.0
            #pragma multi_compile_instancing

            #pragma multi_compile _ INDIRECT_RENDERING
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local_vertex OCTAHEDRON_ON
            #pragma shader_feature_local PARALLAX_ON
            #pragma shader_feature_local PIXEL_DEPTH_OFFSET_ON
        ENDHLSL

        Pass
        {
            Name "Deferred"
			Tags { "LightMode" = "Deferred" }

            Stencil
            {
                Ref 3
                Pass Replace
                WriteMask 3
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile _ REFLECTION_PROBE_RENDERING

            #include "SpeedTree8Imposter.hlsl"
            ENDHLSL
        }

        Pass
        {
            ColorMask 0
            ZClip [_ZClip]

            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile _ PUNCTUAL_LIGHT_SHADOW

            #include "SpeedTree8Imposter.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags{ "LightMode" = "MotionVectors" }

            Stencil
            {
                Ref 3
                Pass Replace
                WriteMask 3
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile _ REFLECTION_PROBE_RENDERING

            #define MOTION_VECTORS_ON
            #include "SpeedTree8Imposter.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Picking"
            Tags { "LightMode" = "Picking" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment fragPicking

            #include "SpeedTree8Imposter.hlsl"

            float4 _SelectionID;

            float4 fragPicking(FragmentInput input) : SV_Target
            {
                Fragment(input);
                return _SelectionID;
            }

            ENDHLSL
        }

        Pass
        {
            Cull Off
            Name "Selection"

            Tags { "LightMode" = "SceneSelectionPass" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment fragSelection

            #include "SpeedTree8Imposter.hlsl"

            int _ObjectId, _PassValue;

            float4 fragSelection(FragmentInput input) : SV_Target
            {
                Fragment(input);
                return float4(_ObjectId, _PassValue, 1, 1);
            }

            ENDHLSL
        }
    }
}