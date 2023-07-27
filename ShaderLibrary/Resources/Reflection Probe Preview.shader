Shader"Hidden/Reflection Probe Preview"
{
    Properties 
    {
        _Color("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "Forward"

            Tags
            {
                "LightMode" = "Forward"
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma target 5.0
            #include "ReflectionProbePreview.hlsl"
            ENDHLSL
        }
    }
}
