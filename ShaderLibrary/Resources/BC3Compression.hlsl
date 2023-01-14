#include "UnityCG.cginc"

Texture2D _MainTex;
uint _Index;

float3 RoundAndExpand(float3 v, out uint w)
{
	int3 c = round(v * float3(31, 63, 31));
	w = (c.r << 11) | (c.g << 5) | c.b;

	c.rb = (c.rb << 3) | (c.rb >> 2);
	c.g = (c.g << 2) | (c.g >> 4);

	return (float3) c * (1.0 / 255.0);
}

uint2 BC1Compress(float4 block[16], float3 mincol, float3 maxcol)
{
	uint2 output = 0;
	
	uint2 endPoints = 0;
	maxcol.rgb = RoundAndExpand(maxcol.rgb, endPoints.x);
	mincol.rgb = RoundAndExpand(mincol.rgb, endPoints.y);

    // We have to do this in case we select an alternate diagonal.
	if (endPoints.x < endPoints.y)
	{
		float3 tmp = mincol;
		mincol.rgb = maxcol;
		maxcol.rgb = tmp;
		output.x = endPoints.y | (endPoints.x << 16);
	}
	else
	{
		output.x = endPoints.x | (endPoints.y << 16);
	}
	
	const float RGB_RANGE = 3;

	float3 dir = (maxcol - mincol);
	float3 origin = maxcol.rgb + dir / (2.0 * RGB_RANGE);
	dir /= dot(dir, dir);

    // Compute indices
	uint indices = 0;
	for (int i = 0; i < 16; i++)
	{
		uint index = saturate(dot(origin - block[i].rgb, dir)) * RGB_RANGE;
		output.y |= index << (i * 2);
	}

	uint i0 = (output.y & 0x55555555);
	uint i1 = (output.y & 0xAAAAAAAA) >> 1;
	output.y = ((i0 ^ i1) << 1) | i1;
	
	return output;
}

uint2 BC4Compress(float4 block[16], float minAlpha, float maxAlpha)
{
	// Alpha end points	
	// Optimized index selection
	const int ALPHA_RANGE = 7;

	float bias = maxAlpha + (maxAlpha - minAlpha) / (2.0 * ALPHA_RANGE);
	float scale = 1.0f / (maxAlpha - minAlpha);
	
	uint2 output = 0;
	for (int i = 0; i < 6; i++)
	{
		uint index = saturate((bias - block[i].a) * scale) * ALPHA_RANGE;
		output.x |= index << (3 * i);
	}

	for (i = 6; i < 16; i++)
	{
		uint index = saturate((bias - block[i].a) * scale) * ALPHA_RANGE;
		output.y |= index << (3 * i - 18);
	}

	uint2 i0 = (output >> 0) & 0x09249249;
	uint2 i1 = (output >> 1) & 0x09249249;
	uint2 i2 = (output >> 2) & 0x09249249;

	i2 ^= i0 & i1;
	i1 ^= i0;
	i0 ^= (i1 | i2);

	output.x = (i2.x << 2) | (i1.x << 1) | i0.x;
	output.y = (((i2.y << 2) | (i1.y << 1) | i0.y) << 2) | (output.x >> 16);
	output.x <<= 16;
	
	// Alpha endpoints
	uint c0 = round(minAlpha * 255);
	uint c1 = round(maxAlpha * 255);

	output.x |= (c0 << 8) | c1;
	return output;
}

// compress a 4x4 block to DXT5 format
// integer version, renders to 4 x int32 buffer
uint4 frag(v2f_img input) : SV_Target
{
    // read block
	float4 block[16];
	float4 mincol = 1, maxcol = 0;
	
	for (int y = -2, i = 0; y < 2; y++)
	{
		for (int x = -2; x < 2; x++, i++)
		{
			float4 value = _MainTex.mips[_Index][input.pos.xy * 4 + int2(x, y)];
			block[i] = value;
			
			mincol = min(mincol, value);
			maxcol = max(maxcol, value);
		}
	}

	// Select diagonal
	float4 center = (mincol + maxcol) * 0.5;
	float3 cov = 0;
	for (i = 0; i < 16; i++)
	{
		float4 t = block[i] - center;
		cov += t.xyw * t.zzw;
	}
	
	float3 temp = maxcol.xyw;
	maxcol.xyw = cov < 0 ? mincol.xyw : maxcol.xyw;
	mincol.xyw = cov < 0 ? temp : mincol.xyw;

	// Inset bbox
	float4 inset = (maxcol - mincol) / float2(16, 32).xxxy - (float2(8, 16).xxxy / 255) / float2(16, 32).xxxy;
	mincol = saturate(mincol + inset);
	maxcol = saturate(maxcol - inset);
	
	uint4 output = 0;
	
	output.xy = BC4Compress(block, mincol.a, maxcol.a);
	output.zw = BC1Compress(block, mincol.rgb, maxcol.rgb);
	
	return output;
}