#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

Texture2D<float4> _MainTex, _BumpMap, _EmissionMap;
Texture2D<float> _UnityFBInput0;

cbuffer UnityPerMaterial
{
    float4 _MainTex_ST, _BumpMap_ST;
	float4 _Color, _EmissionColor;
    float _Curvature, _DepthFade, _Translucency, _Distortion, _DistortionStrength, _Test, _EmissiveExposureWeight;
};

struct VertexInput
{
    float3 positionWS : POSITION;
    float3 uv : TEXCOORD;
    float4 color : COLOR;
    float3 normal : NORMAL;
	float3 center : TEXCOORD1;
};

struct FragmentInput
{
    float4 positionCS : SV_Position;
    float3 positionWS : POSITION1;
    float3 uv : TEXCOORD;
    float4 color : COLOR;
    float3 normal : NORMAL;
};

FragmentInput Vertex(VertexInput input)
{
    FragmentInput output;
    output.positionWS = input.positionWS - _WorldSpaceCameraPos;
    output.positionCS = WorldToClip(output.positionWS);
    output.uv = input.uv;
    output.color = input.color;
    output.normal = lerp(input.normal, normalize(input.positionWS - input.center), _Curvature);
    return output;
}

float4 Fragment(FragmentInput input) : SV_Target
{
	float2 uv = input.uv.xy * _MainTex_ST.xy + _MainTex_ST.zw;
	float4 color = _MainTex.Sample(_TrilinearRepeatSampler, uv) * input.color * _Color;
	
    float3 N = normalize(input.normal);
    float3 illuminance = AmbientLight(N, 1.0, 1.0);

	// Treating the aprticle as a sphere, get an offset posiion for sampling lighting
	float thickness = 1.0 - saturate(distance(input.uv.xy, 0.5) * 2.0);
	float radius = input.uv.z * thickness;
	float noise = InterleavedGradientNoise(input.positionCS.xy, _FrameIndex) - 0.5;
	float3 offsetPosition = input.positionWS + _InvViewMatrix._m02_m12_m22 * radius * noise;
	
    for (uint i = 0; i < _DirectionalLightCount; i++)
    {
        DirectionalLightData lightData = _DirectionalLightData[i];
		float3 lightColor = DirectionalLightColor(i, offsetPosition, true, 0.5);
		float NdotL = dot(lightData.Direction, N);
		illuminance += lightColor * ComputeWrappedDiffuseLighting(NdotL, _Translucency) * INV_PI;
	}
    
	uint3 clusterIndex;
	clusterIndex.xy = floor(input.positionCS.xy) / _TileSize;
	clusterIndex.z = log2(input.positionCS.w) * _ClusterScale + _ClusterBias;

	uint2 lightOffsetAndCount = _LightClusterIndices[clusterIndex];
	uint startOffset = lightOffsetAndCount.x;
	uint lightCount = lightOffsetAndCount.y;

	// Would it be better to combine this with the above, so we're only calling evaluate light once?
	for (i = 0; i < lightCount; i++)
	{
		int index = _LightClusterList[startOffset + i];
		LightData lightData = _LightData[index];
		LightCommon light = GetLightColor(lightData, offsetPosition, 0, true);
		float NdotL = dot(light.direction, N);
		illuminance += light.color * ComputeWrappedDiffuseLighting(NdotL, _Translucency) * INV_PI;
	}

	color.rgb *= illuminance;
	color.rgb *= color.a;
	
	float4 emission = _EmissionMap.Sample(_TrilinearRepeatSampler, uv);
	color.rgb += emission.rgb * emission.a * lerp(ApplyExposure(_EmissionColor.rgb), _EmissionColor.rgb, _EmissiveExposureWeight) * input.color.rgb * input.color.a;

    if(_Distortion)
	{
    	return 0;//
        // Scale distortion strength based on camra radius
		float viewScale = _DistortionStrength;

		if (_Test)
			viewScale = _DistortionStrength * _ScreenParams.y * -CameraAspect * 0.25 / input.positionCS.w;

		float distortionStrength = _MainTex.Sample(_TrilinearRepeatSampler, uv).r * viewScale;
		float3 normalTS = UnpackNormal(_BumpMap.Sample(_TrilinearRepeatSampler, uv));
		float2 uv = input.positionCS.xy * _ScreenSize.zw;
		color.rgb = _CameraOpaqueTexture.Sample(_LinearClampSampler, uv + normalTS.xy * distortionStrength);
	}

    // Depth Fade
	float deviceDepth = _UnityFBInput0[input.positionCS.xy];
	float linearDepth = LinearEyeDepth(deviceDepth, _ZBufferParams);
	float depthDifference = saturate(abs(linearDepth - input.positionCS.w) * rcp(max(1e-6, _DepthFade)));
	color *= depthDifference;

	return color;
}