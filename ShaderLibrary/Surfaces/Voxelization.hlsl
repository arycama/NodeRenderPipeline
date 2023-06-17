#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/SpaceTransforms.hlsl"

struct VertexInput
{
	float3 positionOS : POSITION;
	uint instanceID : SV_InstanceID;
};

struct GeometryInput
{
	// As we're using orthographic, w will be 1, so we don't need to include it
	float4 positionCS : SV_POSITION;
};

struct FragmentInput
{
	float4 positionCS : SV_POSITION;
	uint axis : TEXCOORD;
};

RWTexture3D<float> _VoxelGIWrite : register(u1);

GeometryInput Vertex(VertexInput vertex)
{
	float3 positionWS = ObjectToWorld(vertex.positionOS, vertex.instanceID);
	
	GeometryInput output;
	output.positionCS = WorldToClip(positionWS + _WorldSpaceCameraPos);
	return output;
}

[maxvertexcount(3)]
void Geometry(triangle GeometryInput input[3], inout TriangleStream<FragmentInput> stream)
{
	// Select 0, 1 or 2 based on which normal component is largest
	float3 normal = abs(cross(input[1].positionCS.xyz - input[0].positionCS.xyz, input[2].positionCS.xyz - input[0].positionCS.xyz));
	uint axis = dot(normal == Max3(normal), uint3(0, 1, 2));
	
	uint i;
	//bool isVisible = false;
	//for (i = 0; i < 3; i++)
	//{
	//	float3 position = input[i].positionCS;
	//	if (all(position.xyz >= float3(-1.0, -1.0, 0.0) && position.xyz <= float3(1.0, 1.0, 1.0)))
	//	{
	//		isVisible = true;
	//		break;
	//	}
	//}
	
	//if (!isVisible)
	//	return;
	
	for (i = 0; i < 3; i++)
	{
		float4 position = input[i].positionCS;
		
		// convert from -1:1 to 0:1
		position.xy = position.xy * 0.5 + 0.5;
		
		// Flip Y
		position.y = 1.0 - position.y;
		
		// Swizzle so that largest axis gets projected
		float3 result = position.zyx * (axis == 0);
		result += position.xzy * (axis == 1);
		result += position.xyz * (axis == 2);
		
		// Re flip Y
		result.y = 1.0 - result.y;
		
		// Convert xy back to a -1:1 ratio
		result.xy = 2.0 * result.xy - 1.0;
		
		FragmentInput output;
		output.positionCS = float4(result, position.w);
		output.axis = axis;
		stream.Append(output);
	}
}

void Fragment(FragmentInput input)
{
	float3 swizzledPosition = input.positionCS.xyz;
	swizzledPosition.z *= _VoxelResolution;
	
	// Unswizzle largest projected axis from Geometry Shader
	float3 result = swizzledPosition.zyx * (input.axis == 0);
	result += swizzledPosition.xzy * (input.axis == 1);
	result += swizzledPosition.xyz * (input.axis == 2);
	
	result.z = _VoxelResolution - result.z;
	
	// As we use toroidal addressing, we need to offset the final coordinates as the volume moves.
	// This also needs to be wrapped at the end, so that out of bounds pixels will write to the starting indices of the volume
	float3 dest = Mod(result + _VoxelOffset, _VoxelResolution);
	_VoxelGIWrite[dest] = 1;
}