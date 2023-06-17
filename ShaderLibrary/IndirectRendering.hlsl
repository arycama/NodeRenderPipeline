#ifndef INDIRECT_RENDERING_INCLUDED
#define INDIRECT_RENDERING_INCLUDED

#include "Core.hlsl"
#include "Geometry.hlsl"
#include "SpaceTransforms.hlsl"
#include "Utility.hlsl"

struct InstanceData
{
	uint row;
	uint column;
	uint lod;
	float padding;
};

int _MaxHiZMip;

bool FrustumCull(float3 center, float3 extents)
{
	for (uint i = 0; i < _CullingPlanesCount; i++)
	{
		float4 plane = _CullingPlanes[i];
		float3 p = center + (plane.xyz >= 0 ? extents : -extents);
		if (DistanceFromPlane(p, plane) < 0)
			return false;
	}
	
	return true;
}

bool HiZCull(float3 screenMin, float3 screenMax, float2 resolution)
{
	// https://interplayoflight.wordpress.com/2017/11/15/experiments-in-gpu-based-occlusion-culling/
	float2 size = (screenMax.xy - screenMin.xy) * resolution;
	float mip = ceil(log2(Max2(size)));
	
	 // Texel footprint for the lower (finer-grained) level
	float levelLower = max(mip - 1, 0);
	float2 scale = exp2(-levelLower);
	float2 a = floor(screenMin.xy * scale);
	float2 b = ceil(screenMax.xy * scale);
	float2 dims = b - a;
 
    // Use the lower level if we only touch <= 2 texels in both dimensions
	if (dims.x <= 2.0 && dims.y <= 2.0)
		mip = levelLower;
	
	if (mip < _MaxHiZMip)
	{
		// find the max depth
		#if 1
			float minDepth = _CameraMaxZTexture.SampleLevel(_PointClampSampler, float2(screenMin.x, screenMin.y), mip);
			minDepth = min(minDepth, _CameraMaxZTexture.SampleLevel(_PointClampSampler, float2(screenMax.x, screenMin.y), mip));
			minDepth = min(minDepth, _CameraMaxZTexture.SampleLevel(_PointClampSampler, float2(screenMin.x, screenMax.y), mip));
			minDepth = min(minDepth, _CameraMaxZTexture.SampleLevel(_PointClampSampler, float2(screenMax.x, screenMax.y), mip));
		#else
			float minDepth = _CameraMaxZTexture.mips[mip][float2(screenMin.x, screenMin.y) * resolution / exp2(mip)];
			minDepth = min(minDepth, _CameraMaxZTexture.mips[mip][float2(screenMax.x, screenMin.y) * resolution / exp2(mip)]);
			minDepth = min(minDepth, _CameraMaxZTexture.mips[mip][float2(screenMin.x, screenMax.y) * resolution / exp2(mip)]);
			minDepth = min(minDepth, _CameraMaxZTexture.mips[mip][float2(screenMax.x, screenMax.y) * resolution / exp2(mip)]);
		#endif
		
		if (screenMax.z < minDepth)
			return false;
	}
	
	return true;
}

bool HiZCull(float3 boundsMin, float3 boundsSize, float2 resolution, float4x4 screenMatrix)
{
	// Transform 8 corners into screen space and compute bounding box
	float3 screenMin = FloatMax, screenMax = FloatMin;
	
	[unroll]
	for (float z = 0; z < 2; z++)
	{
		[unroll]
		for (float y = 0; y < 2; y++)
		{
			[unroll]
			for (float x = 0; x < 2; x++)
			{
				float3 positionWS = boundsMin + boundsSize * float3(x, y, z);
				float3 positionCS = MultiplyPointProj(screenMatrix, positionWS).xyz;
				screenMin = min(screenMin, positionCS);
				screenMax = max(screenMax, positionCS);
			}
		}
	}
	
	return HiZCull(screenMin, screenMax, resolution);
}


#endif 