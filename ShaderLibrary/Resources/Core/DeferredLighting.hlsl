#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Brdf.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Deferred.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Utility.hlsl"

float4x4 _PixelCoordToViewDirWS;
Texture2D<float> _UnityFBInput0;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	return GetFullScreenTriangleVertexPosition(id);
}

float3 Fragment(float4 positionCS : SV_Position) : SV_Target
{
	float depth = _UnityFBInput0[positionCS.xy];

	// Pixel to world
	SurfaceData surface = SurfaceDataFromGBuffer(positionCS.xy);
	float linearEyeDepth = LinearEyeDepth(depth);
	
	//return surface.Occlusion;
	
	//if(positionCS.x < _ScreenSize.x / 2)
	{
		//return LinearToSrgb(surface.Translucency);
		//return LinearToSrgb(surface.Albedo);
		//return surface.Normal * 0.5 + 0.5;
	}
	
	PbrInput input = SurfaceDataToPbrInput(surface);
	float3x3 frame = GetLocalFrame(surface.Normal);
	float3 tangentWS = frame[0] * dot(surface.tangentWS, frame[0]) + frame[1] * dot(surface.tangentWS, frame[1]);
	return GetLighting(float4(positionCS.xy, depth, linearEyeDepth), surface.Normal, tangentWS, input) + surface.Emission;
}
;