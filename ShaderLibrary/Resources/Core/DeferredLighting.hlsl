#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Brdf.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Deferred.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Utility.hlsl"

float4x4 _PixelCoordToViewDirWS;
Texture2D<float> _UnityFBInput0;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	return GetFullScreenTriangleVertexPosition(id);
}

float4 SampleReflectionProbeAmbient(float3 positionWS, float3 N, float3 albedo, float occlusion)
{
	// todo: camera relative
	positionWS += _WorldSpaceCameraPos;
	
	float weightSum = 0.0;
	float4 sh[7];
	for (uint i = 0; i < _ReflectionProbeCount; i++)
	{
		ReflectionProbeData probe = _ReflectionProbeData[i];
			
		// Calculate distance from AABB center
		float blend = abs(probe.blendDistance);
		
		float3 localPosition = MultiplyPoint3x4(probe.worldToLocal, positionWS);
		
		float3 dist = max(0, (1 - abs(localPosition)) / (blend / ((probe.max - probe.min) * 0.5)));
		float weight = Min3(dist);
		
		if (weight <= 0.0)
			continue;
			
		// Remove the exposure the probe was baked with, before applying the current exposure
		float exposureFactor = ApplyExposure(rcp(probe.exposure + (probe.exposure == 0.0))).r;
		weightSum += weight;
		
		[unroll]
		for (uint j = 0; j < 7; j++)
			sh[j] = _AmbientData[i * 7 + j] * weight * exposureFactor;
	}
	
	if (weightSum <= 0.0)
		return 0.0;
	
	// Normalize
	[unroll]
	for (i = 0; i < 7; i++)
		sh[i] /= weightSum;
	
	
	return float4(EvaluateSH(N, albedo, occlusion, sh), min(1.0, weightSum));
}

float3 Fragment(float4 positionCS : SV_Position) : SV_Target
{
	float depth = _UnityFBInput0[positionCS.xy];

	// Pixel to world
	SurfaceData surface = SurfaceDataFromGBuffer(positionCS.xy);
	float linearEyeDepth = LinearEyeDepth(depth);
	
	//float3 positionWS = PixelToWorld(positionCS.xy, depth);
	//float4 ambient = SampleReflectionProbeAmbient(positionWS, surface.bentNormal, surface.Albedo, surface.Occlusion);
	//if(ambient.a < 1.0)
	//	ambient.rgb = lerp(AmbientLight(surface.Normal, surface.Albedo, surface.Occlusion), ambient.rgb, ambient.a);
	//return ambient.rgb;
	
	PbrInput input = SurfaceDataToPbrInput(surface);
	float3x3 frame = GetLocalFrame(surface.Normal);
	float3 tangentWS = frame[0] * dot(surface.tangentWS, frame[0]) + frame[1] * dot(surface.tangentWS, frame[1]);
	return GetLighting(float4(positionCS.xy, depth, linearEyeDepth), surface.Normal, tangentWS, input) + surface.Emission;
}
;