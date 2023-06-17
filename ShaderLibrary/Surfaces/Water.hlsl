#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Brdf.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Tessellation.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Deferred.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/MotionVectors.hlsl"

SamplerState _TrilinearRepeatAniso4Sampler;
Texture2DArray<float4> _OceanFoamSmoothnessMap;
Texture2DArray<float3> _OceanDisplacementMap;
Texture2DArray<float2> _OceanNormalMap;
Texture2D<float> _OceanTerrainMask;
Texture2D<float3> _WaterNormals;
Texture2D<float4> _FoamBump, _FoamTex, _OceanCausticsMap, _ShoreDistance;

float4 _OceanTerrainMask_ST;
float4 _OceanScale, _OceanTerrainMask_TexelSize, _ShoreDistance_ST;
float3 _TerrainSize;
float _WaterShadowNear, _WindSpeed, _OceanGravity;
float _MaxOceanDepth, _MaxShoreDistance, CausticsScale, _OceanCascadeScale;
uint _OceanTextureSliceOffset, _OceanTextureSlicePreviousOffset;
float4 _RcpCascadeScales;
float4 _OceanTexelSize;
float4 _PatchScaleOffset;

float _RcpVerticesPerEdgeMinusOne;
uint _VerticesPerEdge, _VerticesPerEdgeMinusOne;

Buffer<uint> _PatchData;

cbuffer UnityPerMaterial
{
	float _Smoothness;
	float _ShoreWaveHeight;
	float _ShoreWaveSteepness;
	float _ShoreWaveLength;
	float _ShoreWindAngle;
	
	// Tessellation
	float _EdgeLength;
	float _FrustumThreshold;

	// Fragment
	float _FoamNormalScale;
	float _FoamSmoothness;
	float _WaveFoamFalloff;
	float _WaveFoamSharpness;
	float _WaveFoamStrength;
	float4 _FoamTex_ST;
};

struct VertexInput
{
	uint instanceID : SV_InstanceID;
	uint vertexID : SV_VertexID;
};

struct HullInput
{
	float3 position : TEXCOORD;
	uint4 patchData : TEXCOORD1; // col, row, lod, deltas
};

struct HullConstantOutput
{
	float edgeFactors[4] : SV_TessFactor;
	float insideFactors[2] : SV_InsideTessFactor;
	float4 dx : TEXCOORD1;
	float4 dy : TEXCOORD2;
};

struct DomainInput
{
	float3 position : TEXCOORD;
};

struct FragmentInput
{
	float4 positionCS : SV_POSITION;
	float4 uv0 : TEXCOORD0;
	float4 nonJitteredPositionCS : POSITION2;
	float4 previousPositionCS : POSITION3;
};

struct FragmentOutput
{
	float4 normalFoam : SV_Target0;
	float4 roughnessMask : SV_Target1;
	float2 velocity : SV_Target2;
};

bool CheckTerrainMask(float3 p0, float3 p1, float3 p2, float3 p3)
{
	float2 bl = ApplyScaleOffset(p0.xz, _OceanTerrainMask_ST);
	float2 br = ApplyScaleOffset(p3.xz, _OceanTerrainMask_ST);
	float2 tl = ApplyScaleOffset(p1.xz, _OceanTerrainMask_ST);
	float2 tr = ApplyScaleOffset(p2.xz, _OceanTerrainMask_ST);
	
	// Return true if outside of terrain bounds
	if(any(saturate(bl) != bl || saturate(br) != br || saturate(tl) != tl || saturate(tr) != tr))
		return true;
	
	float2 minValue = min(bl, min(br, min(tl, tr))) * _OceanTerrainMask_TexelSize.zw;
	float2 maxValue = max(bl, max(br, max(tl, tr))) * _OceanTerrainMask_TexelSize.zw;

	float2 size = (maxValue - minValue);
	float2 center = 0.5 * (maxValue + minValue);
	float level = max(0.0, ceil(log2(Max2(size))));
	
	float maxMip = log2(Max2(_OceanTerrainMask_TexelSize.zw));
	if (level <= maxMip)
	{
		float4 pixel = float4(minValue, maxValue) / exp2(level);
		
		return (!_OceanTerrainMask.mips[level][pixel.xy] ||
		!_OceanTerrainMask.mips[level][pixel.zy] ||
		!_OceanTerrainMask.mips[level][pixel.xw] ||
		!_OceanTerrainMask.mips[level][pixel.zw]);
	}
	
	return true;
}

void GerstnerWaves(float3 positionWS, out float3 displacement, out float3 normal, out float3 tangent, out float shoreFactor, float time, out float breaker, out float foam)
{
	float2 uv = positionWS.xz * _ShoreDistance_ST.xy + _ShoreDistance_ST.zw;
	float4 shoreData = _ShoreDistance.SampleLevel(_LinearClampSampler, uv, 0.0);
	
	float depth = shoreData.x * _MaxOceanDepth;
	float shoreDistance = (2.0 * shoreData.y - 1.0) * _MaxShoreDistance * _TerrainSize.x;
	float2 direction = normalize(2.0 * shoreData.zw - 1.0);
	
	// Largest wave arising from a wind speed
	float waveLength = _ShoreWaveLength;
	float frequency = TwoPi / waveLength;
	float phase = sqrt(_OceanGravity * frequency) * time;
	
	// Shore waves linearly fade in on the edges of SDF
	float2 factor = saturate(10 * (1.0 - 2.0 * abs(uv - 0.5)));
	float distanceMultiplier = factor.x * factor.y;
	
	// Shore waves fade in when depth is less than half the wave length, we use 0.25 as this parameter also allows shore waves to heighten as the depth decreases
	float depthMultiplier = saturate((0.5 * waveLength - depth) / (0.25 * waveLength));
	shoreFactor = distanceMultiplier * depthMultiplier;
	
	float shorePhase = frequency * shoreDistance;
	
	// Group speed for water waves is half of the phase speed, we allow 2.7 wavelengths to be in wave group, not so much as breaking shore waves lose energy quickly
	float groupSpeedMultiplier = 0.5 + 0.5 * cos((shorePhase + frequency * phase / 2.0) / 2.7);
	
	// slowly crawling worldspace aligned checkerboard pattern that damps gerstner waves further
	float worldSpacePosMultiplier = 0.75 + 0.25 * sin(time * 0.3 + 0.5 * positionWS.x / waveLength) * sin(time * 0.4 + 0.5 * positionWS.z / waveLength);
	
	float2 windDirection = float2(cos(_ShoreWindAngle * TwoPi), sin(_ShoreWindAngle * TwoPi));
	float gerstnerMultiplier = shoreFactor * groupSpeedMultiplier * worldSpacePosMultiplier * pow(saturate(dot(windDirection, direction)), 0.5);
	float amplitude = gerstnerMultiplier * _ShoreWaveHeight;
	float steepness = amplitude * frequency > 0.0 ? _ShoreWaveSteepness / (amplitude * frequency) : 0.0;

	float sinFactor, cosFactor;
	sincos(frequency * shoreDistance + phase, sinFactor, cosFactor);

	// Normal
	normal.y = 1.0 - steepness * frequency * amplitude * sinFactor;
	normal.xz = -direction * frequency * amplitude * cosFactor;

	// Tangent (Had to swap X and Z)
	tangent.x = 1.0 - steepness * direction.y * direction.y * frequency * amplitude * sinFactor;
	tangent.y = direction.y * frequency * amplitude * cosFactor;
	tangent.z = -steepness * direction.x * direction.y * frequency * amplitude * sinFactor;

	// Gerstner wave displacement
	displacement.y = amplitude * sinFactor;
	displacement.xz = direction * cosFactor * steepness * amplitude;
	
	// Adding vertical displacement as the wave increases while rolling on the shallow area
	displacement.y += amplitude * 1.2;
	
	// Wave height is 2*amplitude, a wave will start to break when it approximately reaches a water depth of 1.28 times the wave height, empirically:
	// http://passyworldofmathematics.com/mathematics-of-ocean-waves-and-surfing/
	float breakerMultiplier = saturate((amplitude * 2.0 * 1.28 - depth) / _ShoreWaveHeight);
	
	// adding wave forward skew due to its bottom slowing down, so the forward wave front gradually becomes vertical
	displacement.xz -= direction * sinFactor * amplitude * breakerMultiplier * 2.0;
	float breakerPhase = shorePhase + phase - Pi * 0.25;
	float fp = frac(breakerPhase / TwoPi);
	float sawtooth = saturate(fp * 10.0) - saturate(fp * 10.0 - 1.0);

	// moving breaking area of the wave further forward
	displacement.xz -= 0.5 * amplitude * direction * breakerMultiplier * sawtooth;

	// calculating foam parameters
	// making narrow sawtooth pattern
	breaker = sawtooth * breakerMultiplier * gerstnerMultiplier;

	// only breaking waves leave foamy trails
	foam = (saturate(fp * 10.0) - saturate(fp * 1.1)) * breakerMultiplier * gerstnerMultiplier;
}

HullInput Vertex(VertexInput input)
{
	uint col = input.vertexID % _VerticesPerEdge;
	uint row = input.vertexID / _VerticesPerEdge;
	float x = col;
	float y = row;
	
	uint cellData = _PatchData[input.instanceID];
	uint dataColumn = (cellData >> 0) & 0x3FF;
	uint dataRow = (cellData >> 10) & 0x3FF;
	uint lod = (cellData >> 20) & 0xF;
	int4 diffs = (cellData >> uint4(24, 26, 28, 30)) & 0x3;
	
	if (col == _VerticesPerEdgeMinusOne) 
		y = (floor(row * exp2(-diffs.x)) + (frac(floor(row) * exp2(-diffs.x)) >= 0.5)) * exp2(diffs.x);

	if (row == _VerticesPerEdgeMinusOne) 
		x = (floor(col * exp2(-diffs.y)) + (frac(floor(col) * exp2(-diffs.y)) >= 0.5)) * exp2(diffs.y);
	
	if (col == 0) 
		y = (floor(row * exp2(-diffs.z)) + (frac(floor(row) * exp2(-diffs.z)) > 0.5)) * exp2(diffs.z);
	
	if (row == 0) 
		x = (floor(col * exp2(-diffs.w)) + (frac(floor(col) * exp2(-diffs.w)) > 0.5)) * exp2(diffs.w);
	
	float3 positionWS = float3(float2(dataColumn + x * _RcpVerticesPerEdgeMinusOne, dataRow + y * _RcpVerticesPerEdgeMinusOne) * exp2(lod) * _PatchScaleOffset.xy + _PatchScaleOffset.zw, -_WorldSpaceCameraPos.y).xzy;
	
	HullInput output;
	output.position = positionWS;
	output.patchData = uint4(col, row, lod, cellData);
	return output;
}

HullConstantOutput HullConstant(InputPatch<HullInput, 4> inputs)
{
	HullConstantOutput output = (HullConstantOutput) - 1;
	
	if (QuadFrustumCull(inputs[0].position, inputs[1].position, inputs[2].position, inputs[3].position, _FrustumThreshold))
		return output;
	
	if (!CheckTerrainMask(inputs[0].position + _WorldSpaceCameraPos, inputs[1].position + _WorldSpaceCameraPos, inputs[2].position + _WorldSpaceCameraPos, inputs[3].position + _WorldSpaceCameraPos))
		return output;
	
	[unroll]
	for (uint i = 0; i < 4; i++)
	{
		float3 v0 = inputs[(0 - i) % 4].position;
		float3 v1 = inputs[(1 - i) % 4].position;
		float3 edgeCenter = 0.5 * (v0 + v1);
		output.edgeFactors[i] = CalculateSphereEdgeFactor(v0, v1, _EdgeLength);
	}
	
	output.insideFactors[0] = 0.5 * (output.edgeFactors[1] + output.edgeFactors[3]);
	output.insideFactors[1] = 0.5 * (output.edgeFactors[0] + output.edgeFactors[2]);
	
	// For each vertex, average the edge factors for it's neighboring vertices in the X and Z directions
	// TODO: Could re-use the edge factor to save one of these claculations.. might not be worth the complexity though
	[unroll]
	for (i = 0; i < 4; i++)
	{
		HullInput v = inputs[i];
		float3 pc = v.position;
		
		// Compensate for neighboring patch lods
		float2 spacing = _PatchScaleOffset.xy * _RcpVerticesPerEdgeMinusOne * exp2(v.patchData.z);
		
		uint lodDeltas = v.patchData.w;
		uint4 diffs = (lodDeltas >> uint4(24, 26, 28, 30)) & 0x3;
		
		if (v.patchData.x == 0.0)
			spacing *= exp2(diffs.z);
		
		if (v.patchData.x == _VerticesPerEdgeMinusOne)
			spacing *= exp2(diffs.x);
		
		if (v.patchData.y == 0.0)
			spacing *= exp2(diffs.w);
		
		if (v.patchData.y == _VerticesPerEdgeMinusOne)
			spacing *= exp2(diffs.y);
		
		// Left
		float3 pl = pc + float3(-spacing.x, 0.0, 0.0);
		pl.y = 0.0 - _WorldSpaceCameraPos.y;
		float dx = spacing.x / CalculateSphereEdgeFactor(pc, pl, _EdgeLength);
		
		// Right
		float3 pr = pc + float3(spacing.x, 0.0, 0.0);
		pr.y = 0.0 - _WorldSpaceCameraPos.y;
		dx += spacing.x / CalculateSphereEdgeFactor(pc, pr, _EdgeLength);
		
		// Down
		float3 pd = pc + float3(0.0, 0.0, -spacing.y);
		pd.y = 0.0 - _WorldSpaceCameraPos.y;
		float dy = spacing.y / CalculateSphereEdgeFactor(pc, pd, _EdgeLength);
		
		// Up
		float3 pu = pc + float3(0.0, 0.0, spacing.y);
		pu.y = 0.0 - _WorldSpaceCameraPos.y;
		dy += spacing.y / CalculateSphereEdgeFactor(pc, pu, _EdgeLength);
		
		output.dx[i] = dx * 0.5;
		output.dy[i] = dy * 0.5;
	}
	
	return output;
}

[domain("quad")]
[partitioning("fractional_odd")]
[outputtopology("triangle_ccw")]
[patchconstantfunc("HullConstant")]
[outputcontrolpoints(4)]
DomainInput Hull(InputPatch<HullInput, 4> input, uint id : SV_OutputControlPointID)
{
	DomainInput output;
	output.position = input[id].position;
	return output;
}

float Bilerp(float4 y, float2 i)
{
	float bottom = lerp(y.x, y.w, i.x);
	float top = lerp(y.y, y.z, i.x);
	return lerp(bottom, top, i.y);
}

float3 Bilerp(float3 v0, float3 v1, float3 v2, float3 v3, float2 i)
{
	float3 bottom = lerp(v0, v3, i.x);
	float3 top = lerp(v1, v2, i.x);
	return lerp(bottom, top, i.y);
}

[domain("quad")]
FragmentInput Domain(HullConstantOutput tessFactors, OutputPatch<DomainInput, 4> input, float2 weights : SV_DomainLocation)
{
	float3 position = Bilerp(input[0].position, input[1].position, input[2].position, input[3].position, weights);
	
	// TODO: Camera relative
	float2 uv = position.xz + _WorldSpaceCameraPos.xz;
	float2 dx = float2(Bilerp(tessFactors.dx, weights), 0.0);
	float2 dy = float2(0.0, Bilerp(tessFactors.dy, weights));
	
	FragmentInput output;
	output.uv0 = float4(position + _WorldSpaceCameraPos, 0);

	float3 waveDisplacement = 0, previousWaveDisplacement = 0;

	[unroll]
	for (uint i = 0; i < 4; i++)
	{
		waveDisplacement += _OceanDisplacementMap.SampleGrad(_TrilinearRepeatSampler, float3(uv * _OceanScale[i], i + _OceanTextureSliceOffset), dx * _OceanScale[i], dy * _OceanScale[i]);
		previousWaveDisplacement += _OceanDisplacementMap.SampleGrad(_TrilinearRepeatSampler, float3(uv * _OceanScale[i], i + _OceanTextureSlicePreviousOffset), dx * _OceanScale[i], dy * _OceanScale[i]);
	}

	// shore waves
	float shoreFactor, breaker, foam;
	float3 normal, shoreDisplacement, tangent;
	GerstnerWaves(position + _WorldSpaceCameraPos, shoreDisplacement, normal, tangent, shoreFactor, _Time.y, breaker, foam);
	float3 displacement = shoreDisplacement + waveDisplacement * lerp(1.0, 0.0, 0.75 * shoreFactor);

	float3 previousPositionWS = position;

	// Apply displacement, Curve horizon
	position += displacement;
	position = PlanetCurve(position);

	#ifdef WATER_SHADOW_CASTER
		output.positionCS = MultiplyPoint(_WaterShadowMatrix, position);
	#else
	output.positionCS = WorldToClip(position);
	#endif

	// Motion vectors
	GerstnerWaves(previousPositionWS + _WorldSpaceCameraPos, shoreDisplacement, normal, tangent, shoreFactor, _Time.y - unity_DeltaTime.x, breaker, foam);
	previousPositionWS += shoreDisplacement + previousWaveDisplacement * lerp(1.0, 0.0, 0.75 * shoreFactor);
	
	output.nonJitteredPositionCS = WorldToClipNonJittered(position);
	previousPositionWS = PlanetCurvePrevious(previousPositionWS);
	output.previousPositionCS = WorldToClipPrevious(previousPositionWS);

	return output;
}

void FragmentShadow() { }

FragmentOutput Fragment(FragmentInput input)
{
	// Gerstner normals + foam
	float shoreFactor, breaker, shoreFoam;
	float3 N, displacement, T;
	GerstnerWaves(input.uv0.xyz, displacement, N, T, shoreFactor, _Time.y, breaker, shoreFoam);
	
	// Normal + Foam data
	float2 normalData = 0.0;
	float foam = 0.0;
	float smoothness = 1.0;

	[unroll]
	for (uint i = 0; i < 4; i++)
	{
		float3 uv = float3(input.uv0.xz * _OceanScale[i], i + _OceanTextureSliceOffset);
		uv.xy = UnjitterTextureUV(uv.xy);
		float4 cascadeData = _OceanFoamSmoothnessMap.Sample(_TrilinearRepeatAniso4Sampler, uv);

		normalData += _OceanNormalMap.Sample(_TrilinearRepeatAniso4Sampler, uv);
		foam += cascadeData.b * _RcpCascadeScales[i];
		smoothness *= lerp(cascadeData.a * (1.0 - 7.0 / 8.0) + 7.0 / 8.0, 1.0, shoreFactor * 0.75);
	}
	
	smoothness = LengthToSmoothness(smoothness);
	
	float3 B = cross(T, N);
	float3x3 tangentToWorld = float3x3(T, B, N);
	float3 oceanN = float3(normalData * lerp(1.0, 0.0, shoreFactor * 0.75), 1.0);
	
	// Foam calculations
	float foamFactor = saturate(lerp(_WaveFoamStrength * (-foam + _WaveFoamFalloff), breaker + shoreFoam, shoreFactor));
	if (foamFactor > 0)
	{
		float2 foamUv = input.uv0.xz * _FoamTex_ST.xy + _FoamTex_ST.zw;
		foamUv.xy = UnjitterTextureUV(foamUv.xy);
		foamFactor *= _FoamTex.Sample(_TrilinearRepeatAniso4Sampler, foamUv).r;
		
		// Sample/unpack normal, reconstruct partial derivatives, scale these by foam factor and normal scale and add.
		float3 foamNormal = UnpackNormalAG(_FoamBump.Sample(_TrilinearRepeatAniso4Sampler, foamUv));
		float2 foamDerivs = foamNormal.xy / foamNormal.z;
		oceanN.xy += foamDerivs * _FoamNormalScale * foamFactor;
		smoothness = lerp(smoothness, _FoamSmoothness, foamFactor);
	}

	N = normalize(mul(oceanN, tangentToWorld));
	
	float3x3 frame = GetLocalFrame(N);
	float sinFrame = dot(T, frame[1]);
	float cosFrame = dot(T, frame[0]);

	//smoothness = lerp(smoothness, _FoamSmoothness, foamFactor);
	float2 perceptualRoughness = 1.0 - smoothness;

	float3 normalOct = PackFloat2To888(0.5 * PackNormalOctQuadEncode(N) + 0.5);

	FragmentOutput output;
	output.velocity = MotionVectorFragment(input.nonJitteredPositionCS, input.previousPositionCS);
	output.normalFoam = float4(normalOct, foamFactor);

	bool quad2or4 = (sinFrame * cosFrame) < 0;

	// We need to convert the values of Sin and Cos to those appropriate for the 1st quadrant.
	// To go from Q3 to Q1, we must rotate by Pi, so taking the absolute value suffices.
	// To go from Q2 or Q4 to Q1, we must rotate by ((N + 1/2) * Pi), so we must
	// take the absolute value and also swap Sin and Cos.
	bool storeSin = (abs(sinFrame) < abs(cosFrame)) != quad2or4;
	// sin [and cos] are approximately linear up to [after] Pi/4 ± Pi.
	float sinOrCos = min(abs(sinFrame), abs(cosFrame));
	// To avoid storing redundant angles, we must convert from a node-centered representation
	// to a cell-centered one, e.i. remap: [0.5/256, 255.5/256] -> [0, 1].
	float remappedSinOrCos = Remap01(sinOrCos, sqrt(2) * 256.0 / 255.0, 0.5 / 255.0);
	float metallicSin = PackFloatInt8bit(0.0, storeSin ? 1 : 0, 8);

	output.roughnessMask = float4(metallicSin, perceptualRoughness, remappedSinOrCos);
	return output;
}