#ifndef CORE_INCLUDED
#define CORE_INCLUDED

#pragma warning (disable : 3571)

cbuffer UnityPerCamera
{
	float4 _Time; // (t/20, t, t*2, t*3)
	float4 _SinTime; // sin(t/8), sin(t/4), sin(t/2), sin(t)
	float4 _CosTime; // cos(t/8), cos(t/4), cos(t/2), cos(t)
	float4 unity_DeltaTime; // dt, 1/dt, smoothdt, 1/smoothdt

	float3 _WorldSpaceCameraPos;

	 // x = 1 or -1 (-1 if projection is flipped)
    // y = near plane
    // z = far plane
    // w = 1/far plane
	float4 _ProjectionParams;

    // x = width
    // y = height
    // z = 1 + 1.0/width
    // w = 1 + 1.0/height
	float4 _ScreenParams;

    // Values used to linearize the Z buffer (http://www.humus.name/temp/Linearize%20depth.txt)
    // x = 1-far/near
    // y = far/near
    // z = x/far
    // w = y/far
    // or in case of a reversed depth buffer (UNITY_REVERSED_Z is 1)
    // x = -1+far/near
    // y = 1
    // z = x/far
    // w = 1/far
	float4 _ZBufferParams;

    // x = orthographic camera's width
    // y = orthographic camera's height
    // z = unused
    // w = 1.0 if camera is ortho, 0.0 if perspective
	float4 unity_OrthoParams;
};

cbuffer UnityPerDraw
{
	// Space block Feature
	float3x4 unity_ObjectToWorld;
	float3x4 unity_WorldToObject;
	float4 unity_LODFade; // x is the fade value ranging within [0,1]. y is x quantized into 16 levels
	float4 unity_WorldTransformParams; // w is usually 1.0, or -1.0 for odd-negative scale transforms

	// Velocity
	float3x4 unity_MatrixPreviousM;
	float3x4 unity_MatrixPreviousMI;
	//X : Use last frame positions (right now skinned meshes are the only objects that use this
	//Y : Force No Motion
	//Z : Z bias value
	//W : Camera only
	float4 unity_MotionVectorsParams;
};

cbuffer ShaderVariablesGlobal
{
	float4x4 _ViewMatrix;
	float4x4 _CameraViewMatrix;
	float4x4 _InvViewMatrix;
	float4x4 _ProjMatrix;
	float4x4 _ViewProjMatrix;
	float4x4 _CameraViewProjMatrix;
	float4x4 _InvViewProjMatrix;
	
	float4x4 _NonJitteredViewProjMatrix;
	float4x4 _PrevViewMatrix;
	float4x4 _PrevViewProjMatrix;
	float4x4 _PrevInvViewProjMatrix;
	float4x4 _PrevInvProjMatrix;
	
	float4 _ScreenSize;
};

float3 _PreviousCameraPosition;
float3 _PreviousCameraDelta;
float4 _CullingPlanes[6];

float _InPlayMode;
uint _FrameIndex;
float2 _Jitter;
uint _CullingPlanesCount;

const static float CameraAspect = _ProjMatrix._m11;
const static float3 CameraPosition = _InvViewMatrix._m03_m13_m23;

// Some common sampler states.. to avoid them potentially being declared in multiple places.
SamplerState _PointClampSampler, _PointRepeatSampler, _LinearClampSampler, _LinearRepeatSampler, _TrilinearClampSampler, _TrilinearRepeatSampler;
SamplerComparisonState _PointClampCompareSampler, _LinearClampCompareSampler;

// Non builtin
Texture2D<float3> _CameraOpaqueTexture;
Texture2D<float> _Exposure, _CameraMaxZTexture;
float _ExposureValue, _ExposureValueRcp;

// Instancing
uint unity_BaseInstanceID;

const static float _NearClipPlane = _ProjectionParams.y;
const static float _FarClipPlane = _ProjectionParams.z;
const static float _NearClipValue = 1.0;
const static float _FarClipValue = 0.0;

cbuffer UnityInstancing_PerDraw0
{
	struct
	{
		float4x4 unity_ObjectToWorldArray;
		float4x4 unity_WorldToObjectArray;
		float2 unity_LODFadeArray;
		float unity_RenderingLayerArray;
		float padding;
	}
	
	unity_Builtins0Array[2];
};

//cbuffer UnityInstancing_PerDraw3
//{
//	struct
//	{
//		float4x4 unity_PrevObjectToWorldArray;
//		float4x4 unity_PrevWorldToObjectArray;
//	}
	
//	unity_Builtins3Array[2];
//};

// Structs
struct SurfaceData
{
	float3 Albedo;
	float Occlusion;
	float2 PerceptualRoughness;
	float3 Normal;
	float Metallic;
	float3 Emission;
	float Alpha;
	float2 Velocity;
	float3 Translucency;
	float3 tangentWS;
	bool blurryRefractions;
	float3 bentNormal;
};

struct FragmentInputImage
{
	float4 positionCS : SV_POSITION;
	float2 uv : TEXCOORD1;
};

struct v2f_img
{
	float4 positionCS : SV_Position;
	float2 uv : TEXCOORD;
};

SurfaceData DefaultSurface()
{
	SurfaceData surface;
	surface.Albedo = 0;
	surface.Alpha = 1;
	surface.Emission = 0;
	surface.PerceptualRoughness = 1.0;
	surface.Metallic = 0;
	surface.Occlusion = 1;
	surface.Normal = float3(0, 0, 1);
	surface.Velocity = 0;
	surface.Translucency = 0;
	surface.tangentWS = float3(1, 0, 0);
	surface.blurryRefractions = false;
	surface.bentNormal = float3(0, 0, 1);
	return surface;
}

// InstancedIndirect
StructuredBuffer<uint> _RendererInstanceIndexOffsets;
uint RendererOffset;
StructuredBuffer<uint> _VisibleRendererInstanceIndices;
StructuredBuffer<float3x4> _InstancePositions;
StructuredBuffer<float> _InstanceLodFades;
float4x4 _LocalToWorld;

float3x4 ApplyCameraTranslationToMatrix(float3x4 modelMatrix)
{
    // To handle camera relative rendering we substract the camera position in the model matrix
	modelMatrix._m03_m13_m23 -= _WorldSpaceCameraPos;
	return modelMatrix;
}

float4x4 ApplyCameraTranslationToMatrix(float4x4 modelMatrix)
{
    // To handle camera relative rendering we substract the camera position in the model matrix
	modelMatrix._m03_m13_m23 -= _WorldSpaceCameraPos;
    return modelMatrix;
}

float4x4 ApplyCameraTranslationToInverseMatrix(float4x4 inverseModelMatrix)
{
    // To handle camera relative rendering we need to apply translation before converting to object space
	float4x4 translationMatrix = { { 1.0, 0.0, 0.0, _WorldSpaceCameraPos.x }, { 0.0, 1.0, 0.0, _WorldSpaceCameraPos.y }, { 0.0, 0.0, 1.0, _WorldSpaceCameraPos.z }, { 0.0, 0.0, 0.0, 1.0 } };
    return mul(inverseModelMatrix, translationMatrix);
}

float3x4 ApplyCameraTranslationToInverseMatrix(float3x4 inverseModelMatrix)
{
	float4x4 mat = float4x4(inverseModelMatrix[0], inverseModelMatrix[1], inverseModelMatrix[2], float4(0, 0, 0, 1));
	
    // To handle camera relative rendering we need to apply translation before converting to object space
	float4x4 translationMatrix = { { 1.0, 0.0, 0.0, _WorldSpaceCameraPos.x }, { 0.0, 1.0, 0.0, _WorldSpaceCameraPos.y }, { 0.0, 0.0, 1.0, _WorldSpaceCameraPos.z }, { 0.0, 0.0, 0.0, 1.0 } };
	return (float3x4) mul(mat, translationMatrix);
}

float3x4 GetObjectToWorld(uint instanceID, bool cameraRelative = true)
{
	#ifdef INDIRECT_RENDERING
		uint instanceIndex = instanceID + _RendererInstanceIndexOffsets[RendererOffset];
		uint index = _VisibleRendererInstanceIndices[instanceIndex];
	
		float3x4 objectToWorld = _InstancePositions[index];
		float4x4 _InstanceToWorld = float4x4(objectToWorld[0], objectToWorld[1], objectToWorld[2], float4(0, 0, 0, 1));
	
		float3x4 localToWorld = (float3x4)mul(_InstanceToWorld, _LocalToWorld);
	#else
		#ifdef INSTANCING_ON
			float3x4 localToWorld = (float3x4)unity_Builtins0Array[unity_BaseInstanceID + instanceID].unity_ObjectToWorldArray;
		#else
			float3x4 localToWorld = unity_ObjectToWorld;
		#endif
	#endif
	
	if(cameraRelative)
		localToWorld = ApplyCameraTranslationToMatrix(localToWorld);
	
	return localToWorld;
}

float4x4 FastInverse(float4x4 m)
{
	float4 c0 = m._m00_m10_m20_m30;
	float4 c1 = m._m01_m11_m21_m31;
	float4 c2 = m._m02_m12_m22_m32;
	float4 pos = m._m03_m13_m23_m33;

	float4 t0 = float4(c0.x, c2.x, c0.y, c2.y);
	float4 t1 = float4(c1.x, 0.0, c1.y, 0.0);
	float4 t2 = float4(c0.z, c2.z, c0.w, c2.w);
	float4 t3 = float4(c1.z, 0.0, c1.w, 0.0);

	float4 r0 = float4(t0.x, t1.x, t0.y, t1.y);
	float4 r1 = float4(t0.z, t1.z, t0.w, t1.w);
	float4 r2 = float4(t2.x, t3.x, t2.y, t3.y);

	pos = -(r0 * pos.x + r1 * pos.y + r2 * pos.z);
	pos.w = 1.0f;

	return transpose(float4x4(r0, r1, r2, pos));
}

float3x4 GetWorldToObject(uint instanceID, bool cameraRelative = true)
{
	#ifdef INDIRECT_RENDERING
		uint instanceIndex = instanceID + _RendererInstanceIndexOffsets[RendererOffset];
		uint index = _VisibleRendererInstanceIndices[instanceIndex];
	
		float3x4 objectToWorld = _InstancePositions[index];
		float4x4 _InstanceToWorld = float4x4(objectToWorld[0], objectToWorld[1], objectToWorld[2], float4(0, 0, 0, 1));
	
		float4x4 localToWorld = mul(_InstanceToWorld, _LocalToWorld);
		localToWorld[3] = float4(0, 0, 0, 1);
		float3x4 worldToObject = (float3x4)FastInverse(localToWorld);
	#else
		#ifdef INSTANCING_ON
			float3x4 worldToObject = (float3x4)unity_Builtins0Array[unity_BaseInstanceID + instanceID].unity_WorldToObjectArray;
		#else
			float3x4 worldToObject = unity_WorldToObject;
		#endif
	#endif
	
	if(cameraRelative)
		worldToObject = ApplyCameraTranslationToInverseMatrix(worldToObject);
	
	return worldToObject;
}

float2 GetLodFade(uint instanceID)
{
	#ifdef INDIRECT_RENDERING
		uint instanceIndex = instanceID + _RendererInstanceIndexOffsets[RendererOffset];
		uint index = _VisibleRendererInstanceIndices[instanceIndex];
		return _InstanceLodFades[index].xx;
	#else
		#ifdef INSTANCING_ON
			return unity_Builtins0Array[unity_BaseInstanceID + instanceID].unity_LODFadeArray;
		#else
			return unity_LODFade.xy;
		#endif
	#endif
}

float3x4 GetPreviousObjectToWorld(uint instanceID)
{
	float3x4 previousObjectToWorld;
	
	#ifdef INDIRECT_RENDERING
		// No dynamic object support currently
		previousObjectToWorld = GetObjectToWorld(instanceID, false);
	#else
		if(_InPlayMode)
		{
			//#ifdef INSTANCING_ON
			//	previousObjectToWorld = unity_Builtins3Array[unity_BaseInstanceID + instanceID].unity_PrevObjectToWorldArray;
			//#else
				previousObjectToWorld = (float3x4)unity_MatrixPreviousM;
			//#endif
		}
		else
		{
			previousObjectToWorld = GetObjectToWorld(instanceID, false);
		}
	#endif
	
	//previousObjectToWorld._m03_m13_m23 -= _PreviousCameraPosition;
	
	previousObjectToWorld = ApplyCameraTranslationToMatrix(previousObjectToWorld);
	return previousObjectToWorld;
}

#endif