#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Brdf.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Packing.hlsl"

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
    o.positionCS.z = _FarClipValue;

	o.normal = ObjectToWorldNormal(v.normalOS, v.instanceID, false);
	o.tangent = float4(ObjectToWorldDir(v.tangentOS.xyz, v.instanceID, false), v.tangentOS.w);
    o.uv = v.uv;
    return o;
}

// Retrodirective function
float B(float p, float g)
{
    if (p < HalfPi)
		return 2.0 - tan(p) / (2.0 * g) * (1.0 - exp(-g / tan(p))) * (3.0 - exp(-g / tan(p)));
    else
		return 1.0;
}

float S(float p)
{
	float t = 0.1;
	return sin(abs(p)) + (Pi - abs(p)) * cos(abs(p)) * RcpPi + t * pow(1.0 - 1.0 / 2.0 * cos(abs(p)), 2.0);
}

float3 Fragment(FragmentInput input) : SV_Target
{
    // Sample Textures
    float3 albedo = _MainTex.Sample(_TrilinearClampSampler, input.uv).rgb * _Color.rgb;
	float3 normalTS = UnpackNormalAG(_BumpMap.Sample(_TrilinearClampSampler, input.uv));

    // 1. Considering the sun as a perfect disk, evaluate  it's solid angle (Could be precomputed)
    float solidAngle = 2 * Pi * (1 - cos(radians(0.5 * _AngularDiameter)));

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

	for (uint i = 0; i < _CelestialBodyCount; i++)
	{
		float3 C = ApplyExposure(_CelestialBodyColors[i].rgb);
		float3 L = _CelestialBodyDirections[i].xyz;
        
		illuminance += saturate(dot(N, L)) * albedo / Pi * C;
	}

	//illuminance += _EarthAlbedo * AmbientLight(_Direction) * albedo;

    return illuminance;
}