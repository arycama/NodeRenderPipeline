// Contains macros for defining availablility of various inputs, outputs, etc.
// Used to optimise shader processing as much as possible

#ifndef COMMON_SURFACE_CONFIG_INCLUDED
#define COMMON_SURFACE_CONFIG_INCLUDED

// Config stuff
// If we need other fragment attributes, can replace this with a generic thing like FRAGMENT_ATTRIBUTES
#ifndef EARLY_DEPTH_STENCIL
	#define EARLY_DEPTH_STENCIL
#endif

// Defines the type for each interpolator, these can be overridden if they are being used for custom packing
#ifndef VERTEX_UV0_TYPE
	#define VERTEX_UV0_TYPE float2
#endif

#ifndef VERTEX_UV1_TYPE
	#define VERTEX_UV1_TYPE float2
#endif

#ifndef VERTEX_UV2_TYPE
	#define VERTEX_UV2_TYPE float2
#endif

#ifndef VERTEX_UV3_TYPE
	#define VERTEX_UV3_TYPE float2
#endif

#ifndef FRAGMENT_UV0_TYPE
	#define FRAGMENT_UV0_TYPE float2
#endif

#ifndef FRAGMENT_UV1_TYPE
	#define FRAGMENT_UV1_TYPE float2
#endif

#ifndef FRAGMENT_UV2_TYPE
	#define FRAGMENT_UV2_TYPE float2
#endif

#ifndef FRAGMENT_UV3_TYPE
	#define FRAGMENT_UV3_TYPE float2
#endif

// To use a feature, simply define "REQUIRES_FRAGMENT_UV2", replace UV2 with NORMAL, TANGENT, COLOR, or whatever else you need
#ifdef REQUIRES_VERTEX_POSITION
	#define VERTEX_POSITION_INPUT float3 positionOS : POSITION0;
#else
	#define VERTEX_POSITION_INPUT
#endif

#ifdef REQUIRES_VERTEX_PREVIOUS_POSITION
	#define VERTEX_PREVIOUS_POSITION_INPUT float3 previousPositionOS : TEXCOORD4;
#else
	#define VERTEX_PREVIOUS_POSITION_INPUT
#endif

#ifdef REQUIRES_VERTEX_NORMAL
	#define VERTEX_NORMAL_INPUT float3 normal : NORMAL;
#else
	#define VERTEX_NORMAL_INPUT
#endif

#ifdef REQUIRES_VERTEX_TANGENT
	#define VERTEX_TANGENT_INPUT float4 tangent : TANGENT;
#else
	#define VERTEX_TANGENT_INPUT
#endif

#ifdef REQUIRES_VERTEX_COLOR
	#define VERTEX_COLOR_INPUT float4 color : COLOR;
#else
	#define VERTEX_COLOR_INPUT
#endif

#ifdef REQUIRES_VERTEX_UV0
	#define VERTEX_UV0_INPUT VERTEX_UV0_TYPE uv0 : TEXCOORD0;
#else
	#define VERTEX_UV0_INPUT
#endif

#ifdef REQUIRES_VERTEX_UV1
	#define VERTEX_UV1_INPUT VERTEX_UV1_TYPE uv1 : TEXCOORD1;
#else
	#define VERTEX_UV1_INPUT
#endif

#ifdef REQUIRES_VERTEX_UV2
	#define VERTEX_UV2_INPUT VERTEX_UV2_TYPE uv2 : TEXCOORD2;
#else
	#define VERTEX_UV2_INPUT
#endif

#ifdef REQUIRES_VERTEX_UV3
	#define VERTEX_UV3_INPUT VERTEX_UV3_TYPE uv3 : TEXCOORD3;
#else
	#define VERTEX_UV3_INPUT
#endif

#ifdef REQUIRES_FRAGMENT_WORLD_POSITION
	#define FRAGMENT_WORLD_POSITION_INPUT float3 positionWS : POSITION1;
#else
	#define FRAGMENT_WORLD_POSITION_INPUT
#endif

#ifdef REQUIRES_FRAGMENT_NORMAL
	#define FRAGMENT_NORMAL_INPUT float3 normal : NORMAL;
#else
	#define FRAGMENT_NORMAL_INPUT
#endif

#ifdef REQUIRES_FRAGMENT_TANGENT
	#define FRAGMENT_TANGENT_INPUT float4 tangent : TANGENT;
#else
	#define FRAGMENT_TANGENT_INPUT
#endif

#ifdef REQUIRES_FRAGMENT_COLOR
	#define FRAGMENT_COLOR_INPUT float4 color : COLOR;
#else
	#define FRAGMENT_COLOR_INPUT
#endif

#ifdef REQUIRES_FRAGMENT_UV0
	#define FRAGMENT_UV0_INPUT FRAGMENT_UV0_TYPE uv0 : TEXCOORD0;
#else
	#define FRAGMENT_UV0_INPUT
#endif

#ifdef REQUIRES_FRAGMENT_UV1
	#define FRAGMENT_UV1_INPUT FRAGMENT_UV1_TYPE uv1 : TEXCOORD1;
#else
	#define FRAGMENT_UV1_INPUT
#endif

#ifdef REQUIRES_FRAGMENT_UV2
	#define FRAGMENT_UV2_INPUT FRAGMENT_UV2_TYPE uv2 : TEXCOORD2;
#else
	#define FRAGMENT_UV2_INPUT
#endif

#ifdef REQUIRES_FRAGMENT_UV3
	#define FRAGMENT_UV3_INPUT FRAGMENT_UV3_TYPE uv3 : TEXCOORD3;
#else
	#define FRAGMENT_UV3_INPUT
#endif

#endif