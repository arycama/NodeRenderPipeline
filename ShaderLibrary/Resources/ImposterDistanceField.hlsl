#include "UnityCG.cginc"

#ifdef __INTELLISENSE__
	#define PASS_SEED_PIXELS
#endif

struct FragmentOutput
{
	float4 albedoAlpha : SV_TARGET0;
	float4 normalOcclusion : SV_TARGET1;
	float4 depth : SV_TARGET2;
};

#ifdef PASS_SEED_PIXELS
Texture2D<float4> _MainTex;
#else
Texture2D<int2> _MainTex;
#endif

SamplerState _LinearClampSampler, _PointClampSampler;
Texture2D<float4> _SourceTex, _DistanceTex;
float4 _MainTex_TexelSize, _TexelSize;
float _MinDistance, _MaxDistance;
int _Offset;

#ifdef PASS_SEED_PIXELS
int2 FragmentSeedPixels(v2f_img i) : SV_Target
{
	float minDist = sqrt(2);
	int2 minSeed = -1;
	
	bool isOpaque = _MainTex[i.pos.xy].a >= 0.5;
	
	for (int y = -1; y < 2; y++)
	{
		for (int x = -1; x < 2; x++)
		{
			if (x == 0 && y == 0)
				continue;
				
			int2 uv = i.pos.xy + int2(x, y);
				
			// Ensure uv is not out of bounds
			if (any(uv < 0 || uv >= _TexelSize.zw))
				continue;
				
			float isNeighborOpaque = _MainTex[uv].a >= 0.5;

			if (isNeighborOpaque != isOpaque)
			{
				float dist = distance(uv * _TexelSize.xy, i.pos.xy * _TexelSize.xy);
				if (dist < minDist)
				{
					minDist = dist;
					minSeed = uv;
				}
			}
		}
	}
	
	return minSeed;
}
#endif

int2 FragmentJumpFlood(v2f_img i) : SV_Target
{
	float minDist = sqrt(2);
	int2 minSeed = -1;

	for (int y = -1; y < 2; y++)
	{
		for (int x = -1; x < 2; x++)
		{			
			int2 uv = i.pos.xy + int2(x, y) * _Offset;
			
			// Ensure uv is not out of bounds
			if (any(uv < 0 || uv >= _TexelSize.zw))
				continue;
			
			int2 seed = _MainTex[uv];
			if (all(seed != -1))
			{
				float dist = distance(seed.xy * _TexelSize.xy, i.pos.xy * _TexelSize.xy);
				if (dist < minDist)
				{
					minDist = dist;
					minSeed = seed;
				}
			}
		}
	}

	return minSeed;
}

float4 FragmentResolve(v2f_img i) : SV_TARGET
{
	int2 seed = _MainTex[i.pos.xy];
	float dist = distance(seed * _TexelSize.xy, i.pos.xy * _TexelSize.xy);

	if (_SourceTex[i.pos.xy].a < 0.5)
	{
		// If this is a transparent pixel, invert the distance and use the original uv for dilation
		dist *= -1.0;
	}
	else
	{
		// If this is an opaque pixel, use the existing color, but find the distance to the closest non-opaque neighbor 
		seed = i.pos.xy;
		
		float minDist = sqrt(2);
		int2 minSeed = -1;
	
		for (int y = -1; y < 2; y++)
		{
			for (int x = -1; x < 2; x++)
			{
				int2 uv = i.pos.xy + int2(x, y);
			
				if (any(uv < 0 || uv >= _TexelSize.zw))
					continue;
			
				float height = _SourceTex[uv].a;
				if (height < 0.5)
				{
					float dist = distance(uv * _TexelSize.xy, i.pos.xy * _TexelSize.xy);
					if (dist < minDist)
					{
						minDist = dist;
						minSeed = uv;
					}
				}
			}
		}
		
		if (all(minSeed != -1))
		{
			dist = minDist;
		}
	}
	
	float signedDistance = saturate((dist - _MinDistance) / (_MaxDistance - _MinDistance));
	
	return float4(_SourceTex[seed].rgb, signedDistance);
}

float4 FragmentCopy(v2f_img i) : SV_TARGET
{
	int2 seed = _MainTex[i.pos.xy];

	float height = _SourceTex[i.pos.xy].a;
	if (height >= 0.5)
	{
		seed = i.pos.xy;
	}
	
	float signedDistance = _DistanceTex.Sample(_LinearClampSampler, i.uv).a;
	
	return float4(_SourceTex[seed].rgb, signedDistance);
}