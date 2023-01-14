#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Brdf.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

struct VertexInput
{
    float3 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
	uint instanceID : SV_InstanceID;
};

struct FragmentInput
{
    float4 positionCS : SV_Position;
    float2 uv : TEXCOORD0;
    float3 normal : NORMAL;
    float4 tangent : TANGENT;
};

Texture2D _MainTex, _BumpMap;

cbuffer UnityPerMaterial
{
	float3 _Color, _EarthAlbedo, _Emission;
	float3 _Luminance, _Direction;
	float _EdgeFade, _Smoothness;
};

float _AngularDiameter;
uint _CelestialBodyCount;
float4 _CelestialBodyColors[4], _CelestialBodyDirections[4];

FragmentInput Vertex(VertexInput v)
{
    FragmentInput o;
	o.positionCS = ObjectToClip(v.positionOS, v.instanceID);
    o.positionCS.z = UNITY_RAW_FAR_CLIP_VALUE;

	o.normal = ObjectToWorldNormal(v.normalOS, v.instanceID, false);
	o.tangent = float4(ObjectToWorldDir(v.tangentOS.xyz, v.instanceID, false), v.tangentOS.w);
    o.uv = v.uv;
    return o;
}

// Retrodirective function
float B(float p, float g)
{
    if (p < HALF_PI)
		return 2.0 - tan(p) / (2.0 * g) * (1.0 - exp(-g / tan(p))) * (3.0 - exp(-g / tan(p)));
    else
		return 1.0;
}

float S(float p)
{
	float t = 0.1;
	return sin(abs(p)) + (PI - abs(p)) * cos(abs(p)) * INV_PI + t * pow(1.0 - 1.0 / 2.0 * cos(abs(p)), 2.0);
}

float3 Fragment(FragmentInput input) : SV_Target
{
    // Sample Textures
    float3 albedo = _MainTex.Sample(_TrilinearClampSampler, input.uv).rgb * _Color.rgb;
	float3 normalTS = UnpackNormal(_BumpMap.Sample(_TrilinearClampSampler, input.uv));

    // 1. Considering the sun as a perfect disk, evaluate  it's solid angle (Could be precomputed)
    float solidAngle = 2 * PI * (1 - cos(radians(0.5 * _AngularDiameter)));

    // 2. Evaluate sun luiminance at ground level accoridng to solidAngle and luminance at zenith (noon)
	float3 illuminance = ApplyExposure(_Luminance) / solidAngle * albedo;

    #ifdef LIMB_DARKENING_ON
    // Model from http :// www. physics . hmc . edu / faculty / esin / a101 / limbdarkening .pdf
    float centerToEdge = length(2.0 * input.uv - 1.0);
    float3 a = float3(0.397, 0.503, 0.652); // coefficient for RGB wavelength (680 ,550 ,440)
    float3 factor = pow(sqrt(max(0.0, 1.0 - centerToEdge * centerToEdge)), a);
    illuminance *= max(0, factor);
    #endif

    float3 bitangent = cross(input.normal, input.tangent.xyz) * (input.tangent.w * unity_WorldTransformParams.w);
	float3x3 tangentToWorld = float3x3(input.tangent.xyz, bitangent, input.normal);
    float3 N = MultiplyVector(normalTS, tangentToWorld, true);

	float3 positionWS = PixelToWorld(input.positionCS.xyz);
    float3 V = normalize(-positionWS);
	V = -_Direction;

	//illuminance = 0;
	//for (uint i = 0; i < _CelestialBodyCount; i++)
 //   {
	//	float3 C = ApplyExposure(_CelestialBodyColors[i].rgb);
	//	float3 L = _CelestialBodyDirections[i].xyz;
	//	//illuminance += saturate(dot(N, L)) * C * INV_PI * albedo;
        
	//	float LdotV = dot(L, V);
	//	float NdotV = dot(N, V);
	//	float NdotL = dot(N, L);
        
	//	float p = acos(LdotV);
	//	float3 c = albedo;
	//	float r = 1737.4;
	//	float d = 384400;
	//	float3 es = C;
        
	//	float ea = -p;
 //       float ee = 0.19 * 0.5f * (1.0 - sin((PI - ea) / 2.0) * tan((PI - ea) / 2.0) * log(1.0 / tan((PI - ea) / 4.0)));
        
	//	float3 em = 2.0 / 3.0 * c * (r * r / (d * d)) * (es + ee) * (1.0 - sin(p / 2.0) * tan(p / 2.0) * log(rcp(tan(p / 4.0))));
	//	//illuminance += em;
        
	//	float g = 0.6;
	//	//float f = NdotL > 0.0 ? 2.0 / (3.0 * PI) * B(p, g) * S(p) * (1.0 / (1.0 + saturate(NdotV) / (NdotL))) : 0.0;
	//	float f = NdotL > 0.0 ? 2.0 / 3.0 * INV_PI * (1.0 / (1.0 + saturate(NdotV) / (NdotL))) : 0.0;
	//	f += ee  * 2.0 / 3.0 * INV_PI;
        
 //       //earthshine
        
        
        
	//	illuminance += f * C * albedo;
        
 //       // Earthshine
	//}

    //illuminance += _EarthAlbedo * AmbientLight(_Direction, 1.0, 1.0) * albedo * INV_PI;

    return illuminance;
}