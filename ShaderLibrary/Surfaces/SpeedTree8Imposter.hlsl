#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/IndirectRendering.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Deferred.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/ImposterCommon.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/MotionVectors.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GeometricTools.hlsl"

#ifdef __INTELLISENSE__
#define PIXEL_DEPTH_OFFSET_ON
#define MOTION_VECTORS_ON
#define PARALLAX_ON
#define PUNCTUAL_LIGHT_SHADOW
#define UNITY_PASS_SHADOWCASTER
#endif

struct VertexInput
{
	float2 positionOS : POSITION;
	uint instanceID : SV_InstanceID;
};

struct FragmentInput
{
	linear centroid float4 positionCS : SV_Position;
	float4 uvPositionOS : TEXCOORD0;
	float4 uvViewDir[3] : TEXCOORD1;
	
#ifdef MOTION_VECTORS_ON
	float4 nonJitteredPositionCS : POSITION1;
	float4 previousPositionCS : POSITION2;
#endif
	
	uint instanceID : SV_InstanceID;
};

struct FragmentOutput
{
#ifndef UNITY_PASS_SHADOWCASTER
	GBufferOut gbufferOut;
#endif
	
#ifdef PIXEL_DEPTH_OFFSET_ON
	float depth : SV_DepthLessEqual;
#endif
};

Texture2DArray<float4> _MainTex, _NormalSmoothness, _SubsurfaceOcclusion;
Texture2DArray<float> _ParallaxMap;

cbuffer UnityPerMaterial
{
	float3 _CenterOffset;
	float _Cutoff, _ImposterFrames;
};

static const float4 _HueVariationColor = float4(1.0, 0.214, 0.0, 0.1);

FragmentInput Vertex(VertexInput input)
{
	#ifdef OCTAHEDRON_ON
		bool isOctahedron = true;
	#else
		bool isOctahedron = false;
	#endif
	
	float3 cameraRight = WorldToObjectDir(_ViewMatrix[0].xyz, input.instanceID, true);
	float3 cameraUp = WorldToObjectDir(_ViewMatrix[1].xyz, input.instanceID, true);
	float3 positionOS = cameraRight * input.positionOS.x + cameraUp * input.positionOS.y;
	
	FragmentInput output;
	float3 positionWS = ObjectToWorld(positionOS, input.instanceID);
	output.positionCS = WorldToClip(positionWS);
	output.instanceID = input.instanceID;
	
	#ifdef UNITY_PASS_SHADOWCASTER
		#ifdef PUNCTUAL_LIGHT_SHADOW
		// Convert camera position to object space, and use the vector from the origin to the camera to calculate the octahedral uv
		float3 cameraPositionOS = WorldToObject(_InvViewMatrix._m03_m13_m23, input.instanceID);
		if (!isOctahedron)
			cameraPositionOS.y = max(cameraPositionOS.y, 0.0);
	
			float3 rayOriginOS = cameraPositionOS;
			float3 rayDirectionOS = normalize(positionOS - cameraPositionOS);
			float3 viewDirectionOS = normalize(cameraPositionOS);
		#else
			float3 viewDirectionOS = WorldToObjectDir(-_ViewMatrix[2].xyz, input.instanceID, true);
			float3 rayOriginOS = positionOS;
			float3 rayDirectionOS = WorldToObjectDir(-_ViewMatrix[2].xyz, input.instanceID, true);
		#endif
	#else
		// Convert camera position to object space, and use the vector from the origin to the camera to calculate the octahedral uv
		float3 cameraPositionOS = WorldToObject(0.0, input.instanceID);
		if (!isOctahedron)
			cameraPositionOS.y = max(cameraPositionOS.y, 0.0);
	
		float3 rayOriginOS = cameraPositionOS;
		float3 rayDirectionOS = normalize(positionOS - cameraPositionOS);
		float3 viewDirectionOS = normalize(cameraPositionOS);
	#endif
	
	float2 uv = VectorToGrid(viewDirectionOS, isOctahedron) * (_ImposterFrames - 1.0);
	output.uvPositionOS = float4(uv, input.positionOS.xy);
	
	float2 cell = floor(uv);
	float2 mask = dot(frac(uv), 1.0) >= 1.0;
	float2 offsets[3] = { float2(0, 1), mask, float2(1, 0) };
	
	[unroll]
	for (uint i = 0; i < 3; i++)
	{
		float2 localUv = cell + offsets[i];
		float3 frameNormal = GridToVector(localUv / (_ImposterFrames - 1.0), isOctahedron);

		// Intersect cameraToVertex ray with the original captured rame
		
		float3 hitPositionOS = IntersectRayPlane(rayOriginOS, rayDirectionOS, 0.0, frameNormal);
		
		// Transform hit into cell coordinates
		float3x3 objectToTangent = ObjectToTangentMatrix(frameNormal);
		float3 hitPositionTS = mul(objectToTangent, hitPositionOS);
		
		#ifdef UNITY_PASS_SHADOWCASTER
			#ifdef PUNCTUAL_LIGHT_SHADOW
				// Transform camera to tangent space, then calculate vector from the corner of the virtual plane (positionOS) to camera
				float3 tangentViewDir = normalize(MultiplyVector(objectToTangent, cameraPositionOS, false) - float3(input.positionOS, 0.0));
			#else
				float3 tangentViewDir = MultiplyVector(objectToTangent, viewDirectionOS, false);
			#endif
		#else
			// Transform camera to tangent space, then calculate vector from the corner of the virtual plane (positionOS) to camera
			float3 tangentViewDir = normalize(MultiplyVector(objectToTangent, cameraPositionOS, false) - float3(input.positionOS, 0.0));
		#endif
		
		// Pack hitPosition.xy and tangentViewDir.xy into the output uv
		output.uvViewDir[i] = float4(hitPositionTS.xy + 0.5, tangentViewDir.xy);
	}
	
	#ifdef MOTION_VECTORS_ON
		output.nonJitteredPositionCS = WorldToClipNonJittered(positionWS);
		output.previousPositionCS = WorldToClipPrevious(positionWS);
	#endif
	
	#ifdef PIXEL_DEPTH_OFFSET_ON
		float3 cameraFwd = WorldToObjectDir(_ViewMatrix[2].xyz, input.instanceID, true);
	
			// Replace the z of the clipPos with a closer one, so that culling works correctly and we can use depthLesser
		float3 positionOSNearer = positionOS - cameraFwd * 0.5;
		float4 positionCSNearer = ObjectToClip(positionOSNearer, input.instanceID);
	
			// convert apply "pespective divide" to get real depth Z
		float nearerZ = positionCSNearer.z / positionCSNearer.w;
			// replace the original clip space z with the new one
		output.positionCS.z = nearerZ * output.positionCS.w;
	#endif
	
	return output;
}

FragmentOutput Fragment(FragmentInput input)
{
	#ifdef LOD_FADE_CROSSFADE
		float dither = InterleavedGradientNoise(input.positionCS.xy, 0);
		float fade = GetLodFade(input.instanceID).x;
		clip(fade + (fade < 0.0 ? dither : -dither));
	#endif

	float4 color = 0.0, packedNormal = 0.0, subsurface = 0.0;
	float depth = 0.0;
	
	float2 cell = floor(input.uvPositionOS.xy);
	float2 uv = frac(input.uvPositionOS.xy);
	float2 mask = uv.x + uv.y > 1;
	float3 weights = float3(min(1.0 - uv, uv.yx), abs(uv.x + uv.y - 1.0)).xzy;

	float2 offsets[3] = { float2(0, 1), mask, float2(1, 0) };
	
	[unroll]
	for (uint i = 0; i < 3; i++)
	{
		float4 uvViewDir = input.uvViewDir[i];
		
		float3 uv;
		uv.xy = uvViewDir.xy;
		
		float2 localUv = cell + offsets[i];
		uv.z = localUv.y * _ImposterFrames + localUv.x;
		
		float height = _ParallaxMap.Sample(_LinearClampSampler, uv) - 0.5;

		#ifdef PARALLAX_ON
			float3 viewDir;
			viewDir.xy = uvViewDir.zw;
			viewDir.z = sqrt(saturate(1.0 - dot(viewDir.xy, viewDir.xy)));
			uv.xy += viewDir.xy / viewDir.z * height;
		#endif
		
		float weight = weights[i];
		
		float4 colorSample = _MainTex.Sample(_LinearClampSampler, uv);
		
		// TODO: This emulates alpha preserve coverage, but should do this at texture creation stage
		colorSample.a *= 1 + _MainTex.CalculateLevelOfDetail(_LinearClampSampler, uv.xy) * 0.25;

		color += colorSample * weight;
		packedNormal += _NormalSmoothness.Sample(_LinearClampSampler, uv) * weight;
		subsurface += _SubsurfaceOcclusion.Sample(_LinearClampSampler, uv) * weight;
		depth += height * weight;
	}
	
	clip(color.a - _Cutoff);
	
	FragmentOutput output;
	
#ifdef PIXEL_DEPTH_OFFSET_ON
	// TODO: Is there a faster way? Eg can we keep screenSpace X and Y and just calculate Z?
	float3 cameraRight = WorldToObjectDir(_ViewMatrix[0].xyz, input.instanceID, true);
	float3 cameraUp = WorldToObjectDir(_ViewMatrix[1].xyz, input.instanceID, true);
	float3 cameraForward = WorldToObjectDir(_ViewMatrix[2].xyz, input.instanceID, true);
	float3 positionOS = cameraRight * input.uvPositionOS.x + cameraUp * input.uvPositionOS.y + -cameraForward * depth;
	float4 positionCS = ObjectToClip(positionOS, input.instanceID);
	output.depth = positionCS.z / positionCS.w;
#endif

#ifndef UNITY_PASS_SHADOWCASTER
	float3 worldNormal = ObjectToWorldNormal(packedNormal.rgb * 2 - 1, input.instanceID, true);

	SurfaceData surface = DefaultSurface();
	surface.Albedo = color.rgb;
	surface.Normal = worldNormal;
	surface.bentNormal = worldNormal;

	surface.PerceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(packedNormal.a);
	surface.Translucency = subsurface.rgb;
	surface.Occlusion = subsurface.a;

	#ifdef INDIRECT_RENDERING
		uint instanceIndex = input.instanceID + _RendererInstanceIndexOffsets[RendererOffset];
		uint index = _VisibleRendererInstanceIndices[instanceIndex];
		float3 treePos = _InstancePositions[index]._14_24_34;
	#else
		float3 treePos = GetObjectToWorld(input.instanceID, false)._14_24_34 - _CenterOffset;
	#endif
	
	float hueVariationAmount = frac(treePos.x + treePos.y + treePos.z);
	float hueVariation = saturate(hueVariationAmount * _HueVariationColor.a);
	
	// Hue varation
	float3 shiftedColor = lerp(surface.Albedo, _HueVariationColor.rgb, hueVariation);
	surface.Albedo = saturate(shiftedColor * (Max3(surface.Albedo) / Max3(shiftedColor) * 0.5 + 0.5));

	shiftedColor = lerp(surface.Translucency, _HueVariationColor.rgb, hueVariation);
	surface.Translucency = saturate(shiftedColor * (Max3(surface.Translucency) / Max3(shiftedColor) * 0.5 + 0.5));

#ifdef MOTION_VECTORS_ON
	surface.Velocity = MotionVectorFragment(input.nonJitteredPositionCS, input.previousPositionCS);
#endif

	output.gbufferOut = SurfaceToGBuffer(surface, input.positionCS.xy);
#endif
	
	return output;

}