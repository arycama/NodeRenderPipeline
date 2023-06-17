#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/MotionVectors.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/SpaceTransforms.hlsl"

Texture2D<float> _UnityFBInput0;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	return GetFullScreenTriangleVertexPosition(id);
}

float2 Fragment(float4 positionCS : SV_Position) : SV_Target
{
	float depth = _UnityFBInput0[positionCS.xy];
	float3 positionWS = PixelToWorld(positionCS.xy, depth);
	float4 nonJitteredPositionCS = WorldToClipNonJittered(positionWS);
	float4 previousPositionCS = WorldToClipPrevious(positionWS);
	return MotionVectorFragment(nonJitteredPositionCS, previousPositionCS);
}
