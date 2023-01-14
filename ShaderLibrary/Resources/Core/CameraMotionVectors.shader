Shader "Hidden/Camera Motion Vectors"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            Name "Camera Motion Vectors"

            Stencil
            {
                Ref 1
                Comp Equal
                ReadMask 3
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "CameraMotionVectors.hlsl"
            ENDHLSL
          
        }
    }
}
