Shader "Hidden/Deferred Lighting"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            Name "Deferred Lighting"

            Stencil
            {
                Ref 0
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma target 5.0

            #pragma vertex Vertex
            #pragma fragment Fragment

            #pragma multi_compile _ SCREENSPACE_REFLECTIONS_ON
            #pragma multi_compile _ VOXEL_GI_ON
            #pragma multi_compile _ REFLECTION_PROBE_RENDERING

            #include "DeferredLighting.hlsl"
            ENDHLSL

        }
    }
}
