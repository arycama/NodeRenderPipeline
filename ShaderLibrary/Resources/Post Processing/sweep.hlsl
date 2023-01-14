sampler2D depthTex;
sampler2D normalTex;

//#include "sharedStructs.h"
//#include "sharedConstants.h"

struct LineInfo
{
	float2 startPos;
	float2 stepDir;

	int dirIndex;
	int numSteps;

	int idleSteps;
	int layerDistance; // In GPU:  Initial write pos

	int layerOffset; // In GPU:  myWriteAfter
	float tangent;
};

float2 proj(const float3 eyePos)
{
	float invZ = rcp(eyePos.z);
	return float2(-7.680000E+02f * invZ * eyePos.x + 7.680000E+02f, -7.680000E+02f * invZ * eyePos.y + 4.320000E+02f);
}

float vecFalloff(float2 horVec)
{
	const float invCoef = 2.500000E-01f;
	return (1.0f / (1.0f + invCoef * dot(horVec, horVec)));
}

float fallOff(const float distance)
{
	const float coef = 1.0f;
	return coef / (coef + distance * distance);
}

float2 snapCoord(const float2 x)
{
	return float2((float) ((int) (x.x * 1.536000E+03f)) * 6.510417E-04f + 3.255208E-04f, (float) ((int) (x.y * 8.640000E+02f)) * 1.157407E-03f + 5.787037E-04f);
}

void sweep(LineInfo liIn[], float result[])
{
	uint3 blockIdx;
	uint3 blockDim;
	uint3 threadIdx;
    
	int tid = blockIdx.x * blockDim.x + threadIdx.x;

	LineInfo li = liIn[tid];
	
	if (li.numSteps < 2)
		return;
	
	int destIndex = li.layerDistance;
	
	uint outOcc = result;
	outOcc += (li.dirIndex < 8) ? 2 : 3;
	
	int myStripe = (li.dirIndex < 8) ? 32 : -32;
	float2 dirStep = normalize(float2(li.stepDir.x * 1.536000E+03f, li.stepDir.y * 8.640000E+02f));
	int convexIndex = 1;
    
	// __shared__
	float2 convexHull[16][32];
	float2 h1, h2, h3;
	float2 pLocal;
	float2 upVec;

	h3 = float2(0.0, -10000.0f);
  
	/* Taking a sample START */
	{
		float2 tempSnapCoord = snapCoord(li.startPos);
		float height = tex2D(depthTex, tempSnapCoord.xy);
		float2 projXY = float2(1.000000E+00f + tempSnapCoord.x * -2.000000E+00f, 5.625000E-01f + tempSnapCoord.y * -1.125000E+00f);
		h2 = float2((projXY.x * dirStep.x + projXY.y * dirStep.y) * height, height);
	}
	/* Taking a sample END */
	
	/* Stepping forward START */
	li.numSteps--;
	li.idleSteps--;
	li.startPos += li.stepDir;
	/* Stepping forward END */
  
	/* Taking a sample START */
	{
		float2 tempSnapCoord = snapCoord(li.startPos);
		float height = tex2D(depthTex, tempSnapCoord.xy);
		float2 projXY = float2(1.000000E+00f + tempSnapCoord.x * -2.000000E+00f, 5.625000E-01f + tempSnapCoord.y * -1.125000E+00f);
		h1 = float2((projXY.x * dirStep.x + projXY.y * dirStep.y) * height, height);
	}
	/* Taking a sample END */
	
	/* Stepping forward START */
	li.numSteps--;
	li.idleSteps--;
	li.startPos += li.stepDir;
	/* Stepping forward END */
  
	//__shared__
	float2 pLocalS[128];
	while (li.idleSteps > 4)
	{
		for (int slot = 0; slot < 4; ++slot)
		{
			/* Taking a sample START */
			{
				float2 tempSnapCoord = snapCoord(li.startPos);
				float height = tex2D(depthTex, tempSnapCoord.xy);
				float2 projXY = float2(1.000000E+00f + tempSnapCoord.x * -2.000000E+00f, 5.625000E-01f + tempSnapCoord.y * -1.125000E+00f);
				pLocalS[slot * 32 + threadIdx.x] = float2((projXY.x * dirStep.x + projXY.y * dirStep.y) * height, height);
			}
			/* Taking a sample END */
      
			li.startPos += li.stepDir;
		}
		
		for (int slot = 0; slot < 4; ++slot)
		{
			pLocal = pLocalS[slot * 32 + threadIdx.x];
			upVec = normalize(-pLocal);
			float2 v1 = h1 - pLocal;
			float2 v2 = h2 - pLocal;
			float dot1 = (max(0.0f, dot(upVec, normalize(v1)) - -8.500000E-01f));
			float dot2 = (max(0.0f, dot(upVec, normalize(v2)) - -8.500000E-01f));
			float occ1 = dot1 * vecFalloff(v1);
			float occ2 = dot2 * vecFalloff(v2);
			int fullIters = 15;
			if (convexIndex && occ1 <= occ2 + 1.000000E-03f && dot1 <= dot2 + 1.000000E-03f)
			{
				dot1 = dot2;
				occ1 = occ2;
				h1 = h2;
				h2 = h3;
				convexIndex--;
				fullIters--;
				v2 = h2 - pLocal;
				dot2 = (max(0.0f, dot(upVec, normalize(v2)) - -8.500000E-01f));
				occ2 = dot2 * vecFalloff(v2);
				while (fullIters && convexIndex && occ1 <= occ2 + 1.000000E-03f && dot1 <= dot2 + 1.000000E-03f)
				{
					dot1 = dot2;
					occ1 = occ2;
					h1 = h2;
					convexIndex--;
					h2 = convexHull[convexIndex & 15][threadIdx.x];
					fullIters--;
					v2 = h2 - pLocal;
					dot2 = (max(0.0f, dot(upVec, normalize(v2)) - -8.500000E-01f));
					occ2 = dot2 * vecFalloff(v2);
				}
			}
			h3 = h2;
			if (fullIters == 15)
			{
				convexHull[convexIndex & 15][threadIdx.x] = h2;
			}
			convexIndex++;
			h2 = h1;
			h1 = pLocal;
			li.numSteps--;
			li.idleSteps--;
		}
	}
	
	while (li.idleSteps > 0)
	{
		/* Taking a sample START */
		{
			float2 tempSnapCoord = snapCoord(li.startPos);
			float height = tex2D(depthTex, tempSnapCoord.xy);
			float2 projXY = float2(1.000000E+00f + tempSnapCoord.x * -2.000000E+00f, 5.625000E-01f + tempSnapCoord.y * -1.125000E+00f);
			pLocal = float2((projXY.x * dirStep.x + projXY.y * dirStep.y) * height, height);
			upVec = normalize(-pLocal);
		}
		/* Taking a sample END */
		
		float2 v1 = h1 - pLocal;
		float2 v2 = h2 - pLocal;
		float dot1 = (max(0.0f, dot(upVec, normalize(v1)) - -8.500000E-01f));
		float dot2 = (max(0.0f, dot(upVec, normalize(v2)) - -8.500000E-01f));
		float occ1 = dot1 * vecFalloff(v1);
		float occ2 = dot2 * vecFalloff(v2);
		int fullIters = 15;
		
		if (convexIndex && occ1 <= occ2 + 1.000000E-03f && dot1 <= dot2 + 1.000000E-03f)
		{
			dot1 = dot2;
			occ1 = occ2;
			h1 = h2;
			h2 = h3;
			convexIndex--;
			fullIters--;
			v2 = h2 - pLocal;
			dot2 = (max(0.0f, dot(upVec, normalize(v2)) - -8.500000E-01f));
			occ2 = dot2 * vecFalloff(v2);
			
			while (fullIters && convexIndex && occ1 <= occ2 + 1.000000E-03f && dot1 <= dot2 + 1.000000E-03f)
			{
				dot1 = dot2;
				occ1 = occ2;
				h1 = h2;
				convexIndex--;
				h2 = convexHull[convexIndex & 15][threadIdx.x];
				fullIters--;
				v2 = h2 - pLocal;
				dot2 = (max(0.0f, dot(upVec, normalize(v2)) - -8.500000E-01f));
				occ2 = dot2 * vecFalloff(v2);
			}
		}
		
		h3 = h2;
		if (fullIters == 15)
		{
			convexHull[convexIndex & 15][threadIdx.x] = h2;
		}
		convexIndex++;
		h2 = h1;
		h1 = pLocal;
		
		/* Stepping forward START */
		li.numSteps--;
		li.idleSteps--;
		li.startPos += li.stepDir;
		/* Stepping forward END */
	}
	
	float occlusion = 0.0f;
	while (li.numSteps > 0)
	{
		for (int slot = 0; slot < 4; ++slot)
		{
			/* Taking a sample START */
			{
				float2 tempSnapCoord = snapCoord(li.startPos);
				float height = tex2D(depthTex, tempSnapCoord.xy);
				float2 projXY = float2(1.000000E+00f + tempSnapCoord.x * -2.000000E+00f, 5.625000E-01f + tempSnapCoord.y * -1.125000E+00f);
				pLocalS[slot * 32 + threadIdx.x] = float2((projXY.x * dirStep.x + projXY.y * dirStep.y) * height, height);
			}
			/* Taking a sample END */
      
			li.startPos += li.stepDir;
		}
		
		for (int slot = 0; slot < 4; ++slot)
		{
			pLocal = pLocalS[slot * 32 + threadIdx.x];
			upVec = normalize(-pLocal);
			float2 v1 = h1 - pLocal;
			float2 v2 = h2 - pLocal;
			float dot1 = (max(0.0f, dot(upVec, normalize(v1)) - -8.500000E-01f));
			float dot2 = (max(0.0f, dot(upVec, normalize(v2)) - -8.500000E-01f));
			float occ1 = dot1 * vecFalloff(v1);
			float occ2 = dot2 * vecFalloff(v2);
			int fullIters = 15;
			
			if (convexIndex && occ1 <= occ2 + 1.000000E-03f && dot1 <= dot2 + 1.000000E-03f)
			{
				dot1 = dot2;
				occ1 = occ2;
				h1 = h2;
				h2 = h3;
				convexIndex--;
				fullIters--;
				v2 = h2 - pLocal;
				dot2 = (max(0.0f, dot(upVec, normalize(v2)) - -8.500000E-01f));
				occ2 = dot2 * vecFalloff(v2);
				while (fullIters && convexIndex && occ1 <= occ2 + 1.000000E-03f && dot1 <= dot2 + 1.000000E-03f)
				{
					dot1 = dot2;
					occ1 = occ2;
					h1 = h2;
					convexIndex--;
					h2 = convexHull[convexIndex & 15][threadIdx.x];
					fullIters--;
					v2 = h2 - pLocal;
					dot2 = (max(0.0f, dot(upVec, normalize(v2)) - -8.500000E-01f));
					occ2 = dot2 * vecFalloff(v2);
				}
			}
			
			h3 = h2;
			
			if (fullIters == 15)
			{
				convexHull[convexIndex & 15][threadIdx.x] = h2;
			}
			convexIndex++;
			h2 = h1;
			h1 = pLocal;
			occlusion = -8.500000E-01f + occ1;
			
			// Writing out
			if (li.dirIndex < 8)
			{
				result[destIndex * 2] = pLocal.y;
			}
			
			//outOcc[destIndex * 4] = occlusion;
			result[destIndex * 4] = occlusion;
			
			li.numSteps--;
			destIndex +=myStripe;
		}
	}
}
