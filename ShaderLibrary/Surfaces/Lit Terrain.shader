Shader "Nature/Terrain/Lit Terrain"
{
    Properties
    {
        _Displacement("Displacement", Range(0, 32)) = 1
        _EdgeLength("Tessellation Edge Length", Range(4, 128)) = 64
        _DistanceFalloff("Tessellation Distance Falloff", Float) = 1
        _FrustumThreshold("Frustum Cull Threshold", Float) = 0
        _BackfaceCullThreshold("Backface Cull Threshold", Float) = 0
        _DisplacementMipBias("Displacement Mip Bias", Range(-2, 2)) = 0.5
    }

    SubShader
    {
        Pass
        {
            Name "Terrain"
            Tags { "LightMode" = "Terrain"}

            Stencil
            {
                Ref 1
                Pass Replace
                WriteMask 1
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex
            #pragma hull Hull
            #pragma domain Domain
            #pragma fragment Fragment

            #pragma multi_compile _ REFLECTION_PROBE_RENDERING

            #include "LitTerrain.hlsl"
            ENDHLSL
        }

        Pass
        {
            ColorMask 0
            ZClip [_ZClip]
            Cull Off

            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster"}

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex
            #pragma hull Hull
            #pragma domain Domain
            #pragma fragment FragmentShadow

            #include "LitTerrain.hlsl"
            ENDHLSL
        }

        //Pass
        //{
        //    Name "Voxelization"
        //    Tags{ "LightMode" = "Voxelization" }

        //    ColorMask 0
        //    ZWrite Off
        //    Cull Off
        //    ZTest Always
        //    Conservative True

        //    HLSLPROGRAM
        //    #pragma target 5.0
        //    #pragma vertex VertexVoxel
        //    #pragma geometry Geometry
        //    #pragma fragment FragmentVoxel

        //    #include "LitTerrain.hlsl"
        //    ENDHLSL
        //}
    }
}
