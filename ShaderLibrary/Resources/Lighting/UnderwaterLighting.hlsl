#define WATER_SHADOW_ON

#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Brdf.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Deferred.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/SpaceTransforms.hlsl"

Texture2D<float> _UnderwaterDepth;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	return GetFullScreenTriangleVertexPosition(id);
}

float3 Fragment(float4 positionCS : SV_Position) : SV_Target
{
	float depth = _UnderwaterDepth[positionCS.xy];
	SurfaceData surface = SurfaceDataFromGBuffer(positionCS.xy);
	PbrInput pbrInput = SurfaceDataToPbrInput(surface);

	float linearUnderwaterDepth = LinearEyeDepth(depth);
	float3x3 frame1 = GetLocalFrame(surface.Normal);
	float3 tangentWS1 = frame1[0] * dot(surface.tangentWS, frame1[0]) + frame1[1] * dot(surface.tangentWS, frame1[1]);
	return GetLighting(float4(positionCS.xy, depth, linearUnderwaterDepth), surface.Normal, tangentWS1, pbrInput);
}
