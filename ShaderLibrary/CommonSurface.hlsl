#ifndef COMMON_SURFACE_INCLUDED
#define COMMON_SURFACE_INCLUDED

// Needed for normals
#if !defined(UNITY_PASS_SHADOWCASTER)
	#define	REQUIRES_FRAGMENT_WORLD_POSITION
#endif

#ifdef __INTELLISENSE__
	//#define MOTION_VECTORS_ON
#endif

#include "Brdf.hlsl"
#include "CommonSurfaceInput.hlsl"
#include "Deferred.hlsl"
#include "IndirectRendering.hlsl"
#include "MotionVectors.hlsl"

void vert(inout VertexData data);
void surf(inout FragmentData input, inout SurfaceData surface);

FragmentInput Vertex(VertexInput vertex)
{
	// Setup vertex data, then call the custom vertex function
	VertexData data = (VertexData)0;

	data.instanceID = vertex.instanceID;
	
	#ifdef REQUIRES_VERTEX_POSITION
		data.positionOS = vertex.positionOS;
	#endif

	#ifdef REQUIRES_VERTEX_UV0
		data.uv0 = vertex.uv0;
	#endif

	#ifdef REQUIRES_VERTEX_UV1
		data.uv1 = vertex.uv1;
	#endif

	#ifdef REQUIRES_VERTEX_UV2
		data.uv2 = vertex.uv2;
	#endif

	#ifdef REQUIRES_VERTEX_UV3
		data.uv3 = vertex.uv3;
	#endif

	#ifdef REQUIRES_VERTEX_PREVIOUS_POSITION
		data.previousPositionOS = vertex.previousPositionOS;
	#endif

	#ifdef REQUIRES_VERTEX_NORMAL
		data.normal = vertex.normal;
	#endif

	#ifdef REQUIRES_VERTEX_TANGENT
		data.tangent = vertex.tangent;
	#endif

	#ifdef REQUIRES_VERTEX_COLOR
		data.color = vertex.color;
	#endif
	
	data.worldPos = ObjectToWorld(data.positionOS, vertex.instanceID);
	data.worldNormal = ObjectToWorldNormal(data.normal, vertex.instanceID, false);
	data.worldTangent = float4(ObjectToWorldDir(data.tangent.xyz, vertex.instanceID, false), data.tangent.w);

	#ifdef HAS_VERTEX_MODIFIER
		vert(data);
	#endif
	
	data.worldPos = PlanetCurve(data.worldPos);

	FragmentInput output;

	#ifdef REQUIRES_FRAGMENT_WORLD_POSITION
	output.positionWS = data.worldPos;
	#endif

	output.positionCS = WorldToClip(data.worldPos);

	#ifdef REQUIRES_FRAGMENT_UV0
		output.uv0 = data.uv0;
	#endif

	#ifdef REQUIRES_FRAGMENT_UV1
		output.uv1 = data.uv1;
	#endif

	#ifdef REQUIRES_FRAGMENT_UV2
		output.uv2 = data.uv2;
	#endif

	#ifdef REQUIRES_FRAGMENT_UV3
		output.uv3 = data.uv3;
	#endif

	#ifdef REQUIRES_FRAGMENT_NORMAL
		output.normal = data.worldNormal;
	#endif

	#ifdef REQUIRES_FRAGMENT_TANGENT
		output.tangent = data.worldTangent;
	#endif

	#ifdef REQUIRES_FRAGMENT_COLOR
		output.color = data.color;
	#endif

	#ifdef MOTION_VECTORS_ON
		output.nonJitteredPositionCS = WorldToClipNonJittered(data.worldPos);
		float3 previousPositionRWS = ObjectToWorldPrevious(data.positionOS, data.instanceID);
		previousPositionRWS = PlanetCurvePrevious(previousPositionRWS);
		output.previousPositionCS = WorldToClipPrevious(previousPositionRWS);
	#endif

	output.instanceID = vertex.instanceID;

	return output;
}

EARLY_DEPTH_STENCIL
FRAGMENT_OUTPUT Fragment(FragmentInput input, bool isFrontFace : SV_IsFrontFace) FRAGMENT_OUTPUT_TYPE
{
	#ifdef INDIRECT_RENDERING
		unity_WorldTransformParams = 1;
	#endif

	#ifdef LOD_FADE_CROSSFADE
		float dither = InterleavedGradientNoise(input.positionCS.xy, 0);
		float fade = GetLodFade(input.instanceID).x;
		clip(fade + (fade < 0.0 ? dither : -dither));
	#endif

	FragmentData fragmentData = (FragmentData)0;
	fragmentData.instanceID = input.instanceID;

	#ifdef REQUIRES_FRAGMENT_WORLD_POSITION
		fragmentData.positionWS = input.positionWS;
		fragmentData.viewDirection = normalize(-fragmentData.positionWS);
	#endif

	#ifdef REQUIRES_FRAGMENT_NORMAL
		fragmentData.normal = normalize(input.normal);
	#else
		fragmentData.normal = float3(0, 1, 0);
	#endif

	#ifdef REQUIRES_FRAGMENT_TANGENT
		fragmentData.tangent = normalize(input.tangent.xyz);
		fragmentData.binormalSign = input.tangent.w;
		fragmentData.binormal = normalize(cross(fragmentData.normal, fragmentData.tangent)) * (fragmentData.binormalSign * unity_WorldTransformParams.w);
	#else
		// Default frame
		fragmentData.tangent = normalize(cross(fragmentData.normal, float3(0, 0, 1)));
		fragmentData.binormal = normalize(cross(fragmentData.tangent, fragmentData.normal));
	#endif

	#ifdef REQUIRES_FRAGMENT_UV0
		fragmentData.uv0 = input.uv0;
	#endif

	#ifdef REQUIRES_FRAGMENT_UV1
		fragmentData.uv1 = input.uv1;
	#endif

	#ifdef REQUIRES_FRAGMENT_UV2
		fragmentData.uv2 = input.uv2;
	#endif

	#ifdef REQUIRES_FRAGMENT_UV3
		fragmentData.uv3 = input.uv3;
	#endif

	#ifdef REQUIRES_FRAGMENT_COLOR
		fragmentData.color = input.color;
	#endif

	// Non-macro dependent code:
	fragmentData.positionSS = input.positionCS;
	fragmentData.isFrontFace = isFrontFace;

	SurfaceData surface = DefaultSurface();

	#ifdef MOTION_VECTORS_ON
		//surface.Velocity = unity_MotionVectorsParams.y ? MotionVectorFragment(input.nonJitteredPositionCS, input.previousPositionCS) : 0.0;
		surface.Velocity = MotionVectorFragment(input.nonJitteredPositionCS, input.previousPositionCS);
	#endif

	surf(fragmentData, surface);

	#ifdef UNITY_PASS_SHADOWCASTER
		// Transparent shadows
		#if defined(MODE_FADE) || defined(MODE_TRANSPARENT)
			float dither1 = InterleavedGradientNoise(input.positionCS.xy, 0);
			if(dither1 > surface.Alpha)
				discard;
		#endif

		return;
	#else
		float3 V = normalize(-input.positionWS);
	
		// Extract some additional data from input structure + surface function result
		float3x3 tangentToWorld = float3x3(fragmentData.tangent, fragmentData.binormal, fragmentData.normal);
		surface.Normal = normalize(mul(surface.Normal, tangentToWorld));
		surface.bentNormal = normalize(mul(surface.bentNormal, tangentToWorld));
	
		#if defined(UNITY_PASS_DEFERRED) || defined(MOTION_VECTORS_ON)
			return SurfaceToGBuffer(surface, input.positionCS.xy);
		#else
			// Apply voxel GI, not needed for deferred as it's done during lighting pass
			//float voxelOcclusion = VoxelOcclusion(fragmentData.positionWS + _WorldSpaceCameraPos);
			//surface.Occlusion = min(surface.Occlusion, voxelOcclusion);

			float2 screenPosition = input.positionCS.xy * _ScreenSize.zw;
			PbrInput pbrInput = SurfaceDataToPbrInput(surface);
			float3x3 frame = GetLocalFrame(surface.Normal);
			float3 tangentWS = frame[0] * dot(fragmentData.tangent, frame[0]) + frame[1] * dot(fragmentData.tangent, frame[1]);

			float3 illuminance, transmittance;
			float3 color = GetLighting(input.positionCS, surface.Normal, tangentWS, pbrInput, illuminance, transmittance);

			color += surface.Emission;
			//color = ApplyEffects(color, fragmentData.positionWS, input.positionCS);

			// treat behind as a light source
			float NdotV = abs(dot(surface.Normal, V));
			float fresnel = F_Schlick(0.04, NdotV);
			float alpha = 1.0 - (1.0 - surface.Alpha) * (1.0 - Min3(transmittance));

			if(surface.blurryRefractions)
			{
				// Empirical remap to try to match a bit the refraction probe blurring for the fallback
				// Use IblPerceptualRoughness so we can handle approx of clear coat.
				float3 size;
				_CameraOpaqueTexture.GetDimensions(0, size.x, size.y, size.z);

				float perceptualRoughness = ConvertAnisotropicPerceptualRoughnessToPerceptualRoughness(surface.PerceptualRoughness);
				float transparentSSMipLevel = pow(perceptualRoughness, 1.3) * uint(max(size.z - 1, 0));

				// If the hit object is in front of the refracting object, we use posInput.positionNDC to sample the color pyramid
				// This is equivalent of setting samplingPositionNDC = posInput.positionNDC when hitLinearDepth <= posInput.linearDepth
				//refractionOffsetMultiplier *= (hitLinearDepth > posInput.linearDepth);

				float3 refraction = refract(-fragmentData.viewDirection, surface.Normal, 1.0 / 1.5);
				float3 viewNormal = WorldToViewDir(refraction, false);
				float2 refractUv = viewNormal.xy * 0.05 * 0;
				float3 preLD = _CameraOpaqueTexture.SampleLevel(_TrilinearClampSampler, screenPosition + refractUv, transparentSSMipLevel).rgb;

				color.rgb += preLD * (1 - fresnel) * (1 - surface.Alpha);
				alpha = 1.0;
			}
	
			return float4(color, alpha);
#endif
	#endif
}

#endif