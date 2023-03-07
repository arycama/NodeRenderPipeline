#define REQUIRES_VERTEX_COLOR
#define REQUIRES_VERTEX_POSITION
#define REQUIRES_VERTEX_NORMAL
#define REQUIRES_VERTEX_TANGENT
#define REQUIRES_VERTEX_UV0
#define REQUIRES_VERTEX_UV1
#define REQUIRES_VERTEX_UV2
#define REQUIRES_VERTEX_UV3
#define VERTEX_UV0_TYPE float4
#define VERTEX_UV1_TYPE float4
#define VERTEX_UV2_TYPE float4
#define VERTEX_UV3_TYPE float4
#define REQUIRES_FRAGMENT_UV0
#define REQUIRES_FRAGMENT_UV1
#define REQUIRES_FRAGMENT_COLOR

#ifdef MOTION_VECTORS_ON
	#define REQUIRES_VERTEX_PREVIOUS_POSITION
#endif

#ifndef UNITY_PASS_SHADOWCASTER
	#define REQUIRES_FRAGMENT_NORMAL
	#define REQUIRES_FRAGMENT_TANGENT
#endif

#ifdef __INTELLISENSE__
	#define _BILLBOARD_ON
#endif

#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/CommonSurface.hlsl"
#include "SpeedTreeWind.hlsl"

Texture2D<float4> _MainTex, _BumpMap;
Texture2D<float3> _ExtraTex, _SubsurfaceTex;
SamplerState _TrilinearRepeatAniso4Sampler;
float _WindEnabled;

cbuffer UnityPerMaterial
{
	float _IsPalm;
	float _Subsurface;
};

cbuffer SpeedTreeWind
{
	float4 _ST_WindVector;
	float4 _ST_WindGlobal;
	float4 _ST_WindBranch;
	float4 _ST_WindBranchTwitch;
	float4 _ST_WindBranchWhip;
	float4 _ST_WindBranchAnchor;
	float4 _ST_WindBranchAdherences;
	float4 _ST_WindTurbulences;
	float4 _ST_WindLeaf1Ripple;
	float4 _ST_WindLeaf1Tumble;
	float4 _ST_WindLeaf1Twitch;
	float4 _ST_WindLeaf2Ripple;
	float4 _ST_WindLeaf2Tumble;
	float4 _ST_WindLeaf2Twitch;
	float4 _ST_WindFrondRipple;
	float4 _ST_WindAnimation;
};

cbuffer SpeedTreeWindPrevious
{
	float4 _ST_WindVector_Previous;
	float4 _ST_WindGlobal_Previous;
	float4 _ST_WindBranch_Previous;
	float4 _ST_WindBranchTwitch_Previous;
	float4 _ST_WindBranchWhip_Previous;
	float4 _ST_WindBranchAnchor_Previous;
	float4 _ST_WindBranchAdherences_Previous;
	float4 _ST_WindTurbulences_Previous;
	float4 _ST_WindLeaf1Ripple_Previous;
	float4 _ST_WindLeaf1Tumble_Previous;
	float4 _ST_WindLeaf1Twitch_Previous;
	float4 _ST_WindLeaf2Ripple_Previous;
	float4 _ST_WindLeaf2Tumble_Previous;
	float4 _ST_WindLeaf2Twitch_Previous;
	float4 _ST_WindFrondRipple_Previous;
	float4 _ST_WindAnimation_Previous;
};

#define GEOM_TYPE_BRANCH 0
#define GEOM_TYPE_FROND 1
#define GEOM_TYPE_LEAF 2
#define GEOM_TYPE_FACINGLEAF 3

static const float4 _HueVariationColor = float4(1.0, 0.214, 0.0, 0.1);

void vert(inout VertexData data)
{
	float3 binormal = cross(data.normal, data.tangent.xyz) * data.tangent.w;
	float3 normalPrev = data.normal;
	float3 tangentPrev = data.tangent.xyz;

    // handle speedtree wind and lod
	// smooth LOD
	#ifndef _BILLBOARD_ON
		data.positionOS = lerp(data.positionOS, data.uv2.xyz, GetLodFade(data.instanceID).x);
	#endif
	
	// color already contains (ao, ao, ao, blend)
    // put hue variation amount in there
	float3 treePos = GetObjectToWorld(data.instanceID, false)._m03_m13_m23;
	float hueVariationAmount = frac(treePos.x + treePos.y + treePos.z);
	data.color.g = saturate(hueVariationAmount * _HueVariationColor.a);
	data.worldPos = ObjectToWorld(data.positionOS, data.instanceID);
	
	 #ifdef _BILLBOARD_ON
        // crossfade faces
        //bool topDown = (data.uv0.z > 0.5);
       // float3 viewDir = UNITY_MATRIX_IT_MV[2].xyz;
		float3 cameraDir = MultiplyVector(GetWorldToObject(data.instanceID, false), 0.0 - GetObjectToWorld(data.instanceID, true)._m03_m13_m23, true);
        //float viewDot = max(dot(viewDir, vdata.normal), dot(cameraDir, data.normal));
        //viewDot *= viewDot;
        //viewDot *= viewDot;
        //viewDot += topDown ? 0.38 : 0.18; // different scales for horz and vert billboards to fix transition zone
        //data.color = float4(1, 1, 1, clamp(viewDot, 0, 1));

        // if invisible, avoid overdraw
        //if (viewDot < 0.3333)
        //{
        //    data.vertex.xyz = float3(0,0,0);
        //}

        //// adjust lighting on billboards to prevent seams between the different faces
        //if (topDown)
        //{
        //    data.normal += cameraDir;
        //}
        //else
        {
            half3 binormal = cross(data.normal, data.tangent.xyz) * data.tangent.w;
            float3 right = cross(cameraDir, binormal);
            data.normal = cross(binormal, right);
        }
		//data.worldNormal = ObjectToWorldDir(data.normal, data.instanceID, true);
    #endif
	
    // wind
	if (_WindEnabled <= 0.0)
		return;

	float3 rotatedWindVector = normalize(mul(_ST_WindVector.xyz, (float3x3) GetObjectToWorld(data.instanceID)));
	float3 rotatedWindVectorPrevious = normalize(mul(_ST_WindVector_Previous.xyz, (float3x3) GetObjectToWorld(data.instanceID)));
	float3 windyPosition = data.positionOS.xyz;
	float3 windyPositionPrevious = data.positionOS.xyz;

    // geometry type
	float geometryType = (int) (data.uv3.w + 0.25);
	bool leafTwo = false;
	if (geometryType > GEOM_TYPE_FACINGLEAF)
	{
		geometryType -= 2;
		leafTwo = true;
	}

    // leaves
	if (geometryType > GEOM_TYPE_FROND)
	{
        // remove anchor position
		float3 anchor = float3(data.uv1.zw, data.uv2.w);
		windyPosition -= anchor;
		windyPositionPrevious -= anchor;

		if (geometryType == GEOM_TYPE_FACINGLEAF)
		{
            // face camera-facing leaf to camera
			float offsetLen = length(windyPosition);
			windyPosition = ViewToObjectDir(windyPosition, true) * offsetLen; // make sure the offset vector is still scaled

			float offsetLenPrev = length(windyPositionPrevious);
			windyPositionPrevious = ViewToObjectDir(windyPositionPrevious, true) * offsetLenPrev; // make sure the offset vector is still scaled
		}

        // leaf wind
		float leafWindTrigOffset = anchor.x + anchor.y;
		windyPosition = LeafWind(true, leafTwo, windyPosition, data.normal, data.uv3.x, 0, data.uv3.y, data.uv3.z, leafWindTrigOffset, rotatedWindVector, _ST_WindLeaf1Ripple, _ST_WindLeaf2Ripple, _ST_WindLeaf1Tumble, _ST_WindLeaf2Tumble, _ST_WindLeaf1Twitch, _ST_WindLeaf2Twitch);

		windyPositionPrevious = LeafWind(true, leafTwo, windyPositionPrevious, normalPrev, data.uv3.x, 0, data.uv3.y, data.uv3.z, leafWindTrigOffset, rotatedWindVectorPrevious, _ST_WindLeaf1Ripple_Previous, _ST_WindLeaf2Ripple_Previous, _ST_WindLeaf1Tumble_Previous, _ST_WindLeaf2Tumble_Previous, _ST_WindLeaf1Twitch_Previous, _ST_WindLeaf2Twitch_Previous);

        // move back out to anchor
		windyPosition += anchor;
		windyPositionPrevious += anchor;
	}

    // frond wind
	if (_IsPalm && geometryType == GEOM_TYPE_FROND)
	{
		windyPosition = RippleFrond(windyPosition, data.normal, data.uv0.x, data.uv0.y, data.uv3.x, data.uv3.y, data.uv3.z, binormal, data.tangent.xyz, _ST_WindFrondRipple);

		windyPositionPrevious = RippleFrond(windyPositionPrevious, normalPrev, data.uv0.x, data.uv0.y, data.uv3.x, data.uv3.y, data.uv3.z, binormal, tangentPrev.xyz, _ST_WindFrondRipple_Previous);
	}

    // branch wind (applies to all 3D geometry)
	float3 rotatedBranchAnchor = normalize(mul(_ST_WindBranchAnchor.xyz, (float3x3) GetObjectToWorld(data.instanceID))) * _ST_WindBranchAnchor.w;
	windyPosition = BranchWind(_IsPalm, windyPosition, treePos, float4(data.uv0.zw, 0, 0), rotatedWindVector, rotatedBranchAnchor, _ST_WindBranchAdherences, _ST_WindBranchTwitch, _ST_WindBranch, _ST_WindBranchWhip, _ST_WindTurbulences, _ST_WindVector, _ST_WindAnimation);

    // global wind
	data.positionOS = GlobalWind(windyPosition, treePos, true, rotatedWindVector, _ST_WindGlobal.x, _ST_WindGlobal, _ST_WindBranchAdherences);

	data.worldPos = ObjectToWorld(data.positionOS, data.instanceID);
	data.worldNormal = ObjectToWorldNormal(data.normal, data.instanceID, false);

	// Previous position
	//float3 rotatedBranchAnchorPrevious = normalize(mul(_ST_WindBranchAnchor_Previous.xyz, (float3x3) GetObjectToWorld(data.instanceID))) * _ST_WindBranchAnchor_Previous.w;

	//windyPositionPrevious = BranchWind(_IsPalm, windyPositionPrevious, treePos, float4(data.uv0.zw, 0, 0), rotatedWindVectorPrevious, rotatedBranchAnchorPrevious, _ST_WindBranchAdherences_Previous, _ST_WindBranchTwitch_Previous, _ST_WindBranch_Previous, _ST_WindBranchWhip_Previous, _ST_WindTurbulences_Previous, _ST_WindVector_Previous, _ST_WindAnimation_Previous);

	//data.positionOS = GlobalWind(windyPositionPrevious, treePos, true, rotatedWindVectorPrevious, _ST_WindGlobal_Previous.x, _ST_WindGlobal_Previous, _ST_WindBranchAdherences_Previous);
}

void surf(inout FragmentData input, inout SurfaceData surface)
{
	input.uv0.xy = UnjitterTextureUV(input.uv0.xy);
	
	float4 color = _MainTex.Sample(_TrilinearRepeatAniso4Sampler, input.uv0);
	color.a *= input.color.a;
	
    #ifdef _CUTOUT_ON
	    clip(color.a - 0.3333);
    #endif

	#ifdef UNITY_PASS_SHADOWCASTER
		return;
	#else
	
	float4 normalData = _BumpMap.Sample(_TrilinearRepeatAniso4Sampler, input.uv0);
	float3 normalMap = UnpackNormal(normalData);
	float3 extra = _ExtraTex.Sample(_TrilinearRepeatAniso4Sampler, input.uv0);

	surface.Albedo = color.rgb;
	surface.Normal = normalMap;
	surface.PerceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(extra.r);
	surface.Metallic = extra.g;
	surface.Occlusion = extra.b * input.color.r;

	// Hue varation
	float3 shiftedColor = lerp(surface.Albedo, _HueVariationColor.rgb, input.color.g);
	surface.Albedo = saturate(shiftedColor * (Max3(surface.Albedo) / Max3(shiftedColor) * 0.5 + 0.5));

	if (_Subsurface)
	{
		surface.Translucency = _SubsurfaceTex.Sample(_TrilinearRepeatAniso4Sampler, input.uv0);
		shiftedColor = lerp(surface.Translucency, _HueVariationColor.rgb, input.color.g);
		surface.Translucency = saturate(shiftedColor * (Max3(surface.Translucency) / Max3(shiftedColor) * 0.5 + 0.5));
	}

	// Flip normal on backsides
	if (!input.isFrontFace)
		surface.Normal.z = -surface.Normal.z;
	
	surface.bentNormal = surface.Normal;
#endif
}