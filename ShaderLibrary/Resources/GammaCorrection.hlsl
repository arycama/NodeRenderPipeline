#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Core.hlsl"

Texture2D<float4> _UITarget;
Texture2D<float3> _MainTex;
Texture2D<float> _Depth;

float4x4 unity_MatrixVP;

// Common vert shaders
v2f_img vert_img(float3 vertex : POSITION, float2 uv : TEXCOORD)
{
	v2f_img o;
	o.positionCS = MultiplyPoint(unity_MatrixVP, MultiplyPoint3x4(unity_ObjectToWorld, vertex));
	o.uv = uv;
	return o;
}

float4 frag (v2f_img i, out float depth : SV_Depth) : SV_Target
{
    float3 scene = _MainTex.Sample(_LinearClampSampler, i.uv);
    float4 ui = _UITarget.Sample(_LinearClampSampler, i.uv);
    
    // Symmetric triangular distribution on [-1,1] with maximal density at 0
	//float noise = BlueNoise1D(i.positionCS.xy) * 2.0 - 1.0;
	//noise = sign(noise) * (1.0 - sqrt(1.0 - abs(noise))); //?

    // Convert scene to sRGB so we can blend "incorrectly"
	scene = LinearToSRGB(scene);//	+noise / 255.0; // ;
	float3 result = scene * (1.0 - ui.a) + ui.rgb;

    // Now convert blended result back to linear, output merger will convert back to sRGB
	result = SRGBToLinear(result);

	depth = _Depth[i.positionCS.xy];
	
    return float4(result, 1.0);
}