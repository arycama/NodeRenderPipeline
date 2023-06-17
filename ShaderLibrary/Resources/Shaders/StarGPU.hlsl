#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Math.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/MatrixUtils.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Random.hlsl"

sampler1D _Gradient;
sampler2D _MainTex;
float _MinRadius, _MaxRadius, _MinBrightness, _MaxBrightness, _MinDistance, _MaxDistance;
uint _Seed;

float4x4 unity_MatrixVP;

struct g2f
{
	float4 pos : SV_Position;
	float2 uv : TEXCOORD0;
	float3 color : COLOR;
};

uint vert(uint id : SV_VertexID) : TEXCOORD
{
	return id;
}

float3 ColorTemperatureToRGB(float temperatureInKelvins)
{
	float3 retColor;
	
	temperatureInKelvins = clamp(temperatureInKelvins, 1000.0, 40000.0) / 100.0;
    
	if (temperatureInKelvins <= 66.0)
	{
		retColor.r = 1.0;
		retColor.g = saturate(0.39008157876901960784 * log(temperatureInKelvins) - 0.63184144378862745098);
	}
	else
	{
		float t = temperatureInKelvins - 60.0;
		retColor.r = saturate(1.29293618606274509804 * pow(t, -0.1332047592));
		retColor.g = saturate(1.12989086089529411765 * pow(t, -0.0755148492));
	}
    
	if (temperatureInKelvins >= 66.0)
		retColor.b = 1.0;
	else if (temperatureInKelvins <= 19.0)
		retColor.b = 0.0;
	else
		retColor.b = saturate(0.54320678911019607843 * log(temperatureInKelvins - 10.0) - 1.19625408914);

	return retColor;
}

float3 RandomPointInSphere(float u, float v, float w)
{
	float theta = u * TwoPi;
	float phi = acos(2.0 * v - 1.0);
	float r = pow(w, 1.0 / 3.0);
	float sinTheta = sin(theta);
	float cosTheta = cos(theta);
	float sinPhi = sin(phi);
	float cosPhi = cos(phi);
	float x = r * sinPhi * cosTheta;
	float y = r * sinPhi * sinTheta;
	float z = r * cosPhi;
    return float3(x, y, z);
}

[maxvertexcount(4)]
void geom(point uint p[1] : TEXCOORD, inout TriangleStream<g2f> outStream)
{
	uint state = PcgHash(p[0] + _Seed);

	float x = ConstructFloat(state);
	state = PcgHash(state);
	
	float y = ConstructFloat(state);
	state = PcgHash(state);
	
	float z = ConstructFloat(state);
	state = PcgHash(state);
	
	double colorValue = ConstructFloat(state);
	state = PcgHash(state);
	
	double sizeValue = ConstructFloat(state);
	state = PcgHash(state);
	
	double luminosityValue = ConstructFloat(state);
	
	double radius = lerp(_MinRadius, _MaxRadius, sizeValue);
	double3 position = (double3)RandomPointInSphere(x, y, z);
	position *= lerp(_MinDistance, _MaxDistance, length(position));
	
	double3 luminosity = (double3) ColorTemperatureToRGB(lerp(1000, 40000, colorValue)) * lerp(_MinBrightness, _MaxBrightness, luminosityValue);
	double starDistance = length(position);

	luminosity = luminosity / (4 * Pi * pow(starDistance, 2));

	double3 forward = position / starDistance;
	double3 right = normalize(cross(double3(0, 1, 0), forward));
	double3 up = normalize(cross(forward, right)) * radius;
	right *= radius;

	float2 uv[4] = { float2(0, 0), float2(1, 0), float2(0, 1), float2(1, 1) };
	float ups[4] = { -1, 1, -1, 1 };
	float rights[4] = { -1, -1, 1, 1 };

	for (uint i = 0; i < 4; i++)
	{
		g2f o;

		o.color = (float3) luminosity;
		o.pos = MultiplyPoint(unity_MatrixVP, position + up * ups[i] + right * rights[i]);
		o.uv = uv[i];

		outStream.Append(o);
	}
}

float3 frag(g2f i) : SV_Target
{
	return float4(smoothstep(0.5, 0.425, distance(i.uv, 0.5)) * i.color, 1);
}
