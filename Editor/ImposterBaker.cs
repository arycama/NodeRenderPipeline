using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class ImposterBaker : ScriptableWizard
{
    [SerializeField] private Shader shader = null;
    [SerializeField, Range(1, 16)] private int frames = 8;
    [SerializeField] private int resolution = 128;
    [SerializeField] private ImposterMode imposterMode = ImposterMode.HemiOctahedron;

    [MenuItem("Tools/Imposter Baker")]
    public static void OnMenuSelect() => DisplayWizard<ImposterBaker>("Imposter Baker", "Bake and Close", "Bake");

    private void OnWizardCreate() => Bake();

    private void OnWizardOtherButton() => Bake();

    private Vector4 GetBoundingSphere(IEnumerable<MeshRenderer> meshRenderers)
    {
        // Ported from https://github.com/microsoft/DirectXMath/blob/main/Inc/DirectXCollision.inl
        Vector3 minX, maxX, minY, maxY, minZ, maxZ;
        minX = minY = minZ = Vector3.positiveInfinity;
        maxX = maxY = maxZ = Vector3.negativeInfinity;

        foreach (var meshRenderer in meshRenderers)
        {
            var meshFilter = meshRenderer.GetComponent<MeshFilter>();

            using var dataArray = Mesh.AcquireReadOnlyMeshData(meshFilter.sharedMesh);
            using var vertices = new NativeArray<Vector3>(meshFilter.sharedMesh.vertexCount, Allocator.TempJob);
            dataArray[0].GetVertices(vertices);

            foreach (var v in vertices)
            {
                var p = meshRenderer.localToWorldMatrix.MultiplyPoint3x4(v);

                if (p.x < minX.x) minX = p;
                if (p.x > maxX.x) maxX = p;
                if (p.y < minY.y) minY = p;
                if (p.y > maxY.y) maxY = p;
                if (p.z < minZ.z) minZ = p;
                if (p.z > maxZ.z) maxZ = p;
            }
        }

        // Use the min/max pair that are farthest parat t oform the initial sphere.
        var deltaX = maxX - minX;
        var distX = deltaX.magnitude;

        var deltaY = maxY - minY;
        var distY = deltaY.magnitude;

        var deltaZ = maxZ - minZ;
        var distZ = deltaZ.magnitude;

        Vector3 center;
        float radius;

        if (distX > distY)
        {
            if (distX > distZ)
            {
                // Use min/max x.
                center = 0.5f * (maxX + minX);
                radius = 0.5f * distX;
            }
            else
            {
                // Use min/max z.
                center = 0.5f * (maxZ + minZ);
                radius = 0.5f * distZ;
            }
        }
        else // Y >= X
        {
            if (distY > distZ)
            {
                // Use min/max y.
                center = 0.5f * (maxY + minY);
                radius = 0.5f * distY;
            }
            else
            {
                // Use min/max z.
                center = 0.5f * (maxZ + minZ);
                radius = 0.5f * distZ;
            }
        }

        // Add any points not inside the sphere
        foreach (var meshRenderer in meshRenderers)
        {
            var meshFilter = meshRenderer.GetComponent<MeshFilter>();

            using var dataArray = Mesh.AcquireReadOnlyMeshData(meshFilter.sharedMesh);
            using var vertices = new NativeArray<Vector3>(meshFilter.sharedMesh.vertexCount, Allocator.TempJob);
            dataArray[0].GetVertices(vertices);

            foreach (var v in vertices)
            {
                var point = meshRenderer.localToWorldMatrix.MultiplyPoint3x4(v);

                var delta = point - center;
                var dist = delta.magnitude;

                if (dist > radius)
                {
                    // Adjust sphere to include the new point.
                    radius = 0.5f * (radius + dist);
                    center = center + (1f - radius / dist) * delta;
                }
            }
        }

        return new Vector4(center.x, center.y, center.z, radius);
    }

    private static Vector3 UnpackNormalHemiOctEncode(Vector2 f)
    {
        f.x = 2f * f.x - 1f;
        f.y = 2f * f.y - 1f;
        var val = new Vector2(f.x + f.y, f.x - f.y) * 0.5f;
        var n = new Vector3(val.x, 1f - Mathf.Abs(val.x) - Mathf.Abs(val.y), val.y);
        return Vector3.Normalize(n);
    }

    private static Vector3 UnpackNormalOctQuadEncode(Vector2 f)
    {
        f.x = 2f * f.x - 1f;
        f.y = 2f * f.y - 1f;
        var n = new Vector3(f.x, 1f - Mathf.Abs(f.x) - Mathf.Abs(f.y), f.y);

        var val = new Vector2(1.0f - Mathf.Abs(n.z), 1.0f - Mathf.Abs(n.x));
        n.x = n.y < 0.0f ? (n.x >= 0.0 ? val.x : -val.x) : n.x;
        n.z = n.y < 0.0f ? (n.z >= 0.0 ? val.y : -val.y) : n.z;

        return Vector3.Normalize(n);
    }

    void OrthoBasisPixarL2(Vector3 v, out Vector3 r, out Vector3 u)
    {
        var sz = v.z < 0.0f ? -1.0f : 1.0f;
        var a = 1.0f / (sz + v.z);
        var b = v.x * v.y * a;
        r = new Vector3(v.x * v.x * a - sz, b, v.x).normalized;
        u = new Vector3(b, v.y * v.y * a - sz, v.y).normalized;
    }

    private void Bake()
    {
        var path = EditorUtility.SaveFilePanel("Save Imposter", "Assets", "Imposter", string.Empty);
        if (string.IsNullOrEmpty(path))
            return;

        path = FileUtil.GetProjectRelativePath(path);

        var gameObject = Selection.activeGameObject;
        var meshRenderers = gameObject.GetComponentsInChildren<MeshRenderer>();

        // Calculate bounding sphere for meshes
        Bounds? bounds = null;
        foreach (var renderer in meshRenderers)
        {
            if (!bounds.HasValue)
                bounds = renderer.bounds;
            else
                bounds.Value.Encapsulate(renderer.bounds);
        }

        // Construct bounding sphere from all vertices to get a tight fit
        var boundingSphere = GetBoundingSphere(meshRenderers);
        var center = (Vector3)boundingSphere;
        var radius = boundingSphere.w;

        var command = new CommandBuffer();

        // Setup textures
        var textureCount = int.Parse(this.shader.FindSubshaderTagValue(0, new ShaderTagId("TextureCount")).name);
        var renderTextures = new RenderTargetIdentifier[textureCount];
        var descriptors = new RenderTextureDescriptor[textureCount];
        for (var i = 0; i < textureCount; i++)
        {
            var renderTextureId = Shader.PropertyToID($"_RenderTextures{i}");
            var arrayDesc = new RenderTextureDescriptor(resolution, resolution)
            {
                colorFormat = Enum.Parse<RenderTextureFormat>(this.shader.FindSubshaderTagValue(0, new ShaderTagId($"TextureFormat{i}")).name),
                dimension = TextureDimension.Tex2DArray,
                sRGB = bool.Parse(this.shader.FindSubshaderTagValue(0, new ShaderTagId($"TexturesRGB{i}")).name),
                volumeDepth = frames * frames
            };

            command.GetTemporaryRT(renderTextureId, arrayDesc);

            renderTextures[i] = renderTextureId;
            descriptors[i] = arrayDesc;
        }

        // Setup view matrices

        var depthId = Shader.PropertyToID("_DepthTemp");
        command.GetTemporaryRT(depthId, resolution, resolution, 32, FilterMode.Point, RenderTextureFormat.Depth);

        var projectionMatrix = Matrix4x4.Ortho(-radius, radius, -radius, radius, 0f, radius * 2f);

        for (var i = 0; i < frames * frames; i++)
        {
            command.SetRenderTarget(renderTextures, depthId, 0, CubemapFace.Unknown, i);
            command.ClearRenderTarget(true, true, Color.clear);

            var vec = new Vector2(i % frames, i / frames) / (frames - 1);
            var forward = imposterMode == ImposterMode.HemiOctahedron ? UnpackNormalHemiOctEncode(vec) : UnpackNormalOctQuadEncode(vec);

            var position = center + forward * radius;

            var viewMatrix = Matrix4x4.TRS(position, Quaternion.LookRotation(-forward), Vector3.one).inverse;
            viewMatrix.SetRow(2, -viewMatrix.GetRow(2));

            command.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

            foreach (var meshRenderer in meshRenderers)
            {
                var materials = meshRenderer.sharedMaterials;
                for (var j = 0; j < materials.Length; j++)
                {
                    var bakeMaterial = new Material(this.shader);
                    bakeMaterial.CopyPropertiesFromMaterial(materials[j]);

                    var mesh1 = meshRenderer.GetComponent<MeshFilter>().sharedMesh;
                    command.DrawMesh(mesh1, meshRenderer.localToWorldMatrix, bakeMaterial, j);
                }
            }
        }

        var computeShader = Resources.Load<ComputeShader>("ImposterDistanceField");
        command.SetComputeIntParam(computeShader, "_CellSize", resolution);
        command.SetComputeIntParam(computeShader, "_Resolution", resolution);
        command.SetComputeFloatParam(computeShader, "_InvCellSize", 1f / resolution);
        command.SetComputeFloatParam(computeShader, "_InvResolution", 1f / resolution);

        var tempId0 = Shader.PropertyToID($"_DstTemp-1");

        command.GetTemporaryRT(tempId0, new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RGFloat)
        {
            dimension = TextureDimension.Tex2DArray,
            enableRandomWrite = true,
            volumeDepth = frames * frames,
        });

        var seedPixelsKernel = computeShader.FindKernel("SeedPixels");
        command.SetComputeTextureParam(computeShader, seedPixelsKernel, "_Texture0", renderTextures[0]);
        command.SetComputeTextureParam(computeShader, seedPixelsKernel, "_Result", tempId0);

        computeShader.GetKernelThreadGroupSizes(seedPixelsKernel, out var seedPixelSizeX, out var seedPixelSizeY, out var seedPixelSizeZ);
        var seedPixelThreadGroups = Vector3Int.CeilToInt(new Vector3((float)resolution / seedPixelSizeX, (float)resolution / seedPixelSizeY, (float)(frames * frames) / seedPixelSizeZ));
        command.DispatchCompute(computeShader, seedPixelsKernel, seedPixelThreadGroups.x, seedPixelThreadGroups.y, seedPixelThreadGroups.z);

        // Jump flood, Ping pong between two temporary textures.
        var jumpFloodKernelIndex = computeShader.FindKernel("JumpFlood");
        var passes = (int)Mathf.Log(resolution, 2);
        for (var j = 0; j < passes; j++)
        {
            var offset = (int)Mathf.Pow(2, passes - j - 1);
            command.SetComputeIntParam(computeShader, "_Offset", offset);

            var tempId = Shader.PropertyToID($"_DstTemp{j}");

            command.GetTemporaryRT(tempId, new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RGFloat)
            {
                dimension = TextureDimension.Tex2DArray,
                enableRandomWrite = true,
                volumeDepth = frames * frames,
            });

            command.SetComputeTextureParam(computeShader, jumpFloodKernelIndex, "_Input", tempId0);
            command.SetComputeTextureParam(computeShader, jumpFloodKernelIndex, "_Result", tempId);

            computeShader.GetKernelThreadGroupSizes(jumpFloodKernelIndex, out var jumpFloodSizeX, out var jumpFloodSizeY, out var jumpFloodSizeZ);
            var jumpFloodThreadGroups = Vector3Int.CeilToInt(new Vector3((float)resolution / jumpFloodSizeX, (float)resolution / jumpFloodSizeY, (float)(frames * frames) / jumpFloodSizeZ));
            command.DispatchCompute(computeShader, jumpFloodKernelIndex, jumpFloodThreadGroups.x, jumpFloodThreadGroups.y, jumpFloodThreadGroups.z);

            tempId0 = tempId;
        }

        // Calculate min max
        var minMaxBuffer = new ComputeBuffer(frames * frames, 4);
        command.SetBufferData(minMaxBuffer, new float[2] { 0.0f, 0.0f });

        var calculateMinMaxKernel = computeShader.FindKernel("CalculateMinMax");
        command.SetComputeTextureParam(computeShader, calculateMinMaxKernel, "_Input", tempId0);
        command.SetComputeTextureParam(computeShader, calculateMinMaxKernel, $"_Texture0", renderTextures[0]);
        command.SetComputeBufferParam(computeShader, calculateMinMaxKernel, "_MaxDepths", minMaxBuffer);
        computeShader.GetKernelThreadGroupSizes(calculateMinMaxKernel, out var minMaxSizeX, out var minMaxSizeY, out var minMaxSizeZ);
        var calculateMinMaxThreadGroups = Vector3Int.CeilToInt(new Vector3((float)resolution / minMaxSizeX, (float)resolution / minMaxSizeY, (float)(frames * frames) / minMaxSizeZ));
        command.DispatchCompute(computeShader, calculateMinMaxKernel, calculateMinMaxThreadGroups.x, calculateMinMaxThreadGroups.y, calculateMinMaxThreadGroups.z);

        var resolveKernel = computeShader.FindKernel("Resolve");
        command.SetComputeTextureParam(computeShader, resolveKernel, "_Input", tempId0);
        command.SetComputeBufferParam(computeShader, resolveKernel, "_MaxDepths", minMaxBuffer);

        var finalRenderTextures = new RenderTexture[textureCount];
        for (var i = 0; i < textureCount; i++)
        {
            var descriptor = descriptors[i];

            descriptor.msaaSamples = 1;
            descriptor.enableRandomWrite = true;

            finalRenderTextures[i] = new RenderTexture(descriptor) { hideFlags = HideFlags.HideAndDontSave };
            finalRenderTextures[i].Create();

            command.SetComputeTextureParam(computeShader, resolveKernel, $"_Texture{i}", renderTextures[i]);
            command.SetComputeTextureParam(computeShader, resolveKernel, $"_Texture{i}Write", finalRenderTextures[i]);
            command.SetComputeFloatParam(computeShader, $"_Texture{i}sRGB", descriptors[i].sRGB ? 1f : 0f);
        }

        var emptyTexArr = Shader.PropertyToID("_EmptyTexArr");
        command.GetTemporaryRT(emptyTexArr, new RenderTextureDescriptor(1, 1) { dimension = TextureDimension.Tex2DArray, enableRandomWrite = true });

        // Set the rest of the textures with dummy values, as compute shaders need all textures to be assigned
        for (var i = textureCount; i < 8; i++)
        {
            command.SetComputeTextureParam(computeShader, resolveKernel, $"_Texture{i}", emptyTexArr);
            command.SetComputeTextureParam(computeShader, resolveKernel, $"_Texture{i}Write", emptyTexArr);
        }

        command.SetComputeIntParam(computeShader, "_TextureCount", textureCount);

        computeShader.GetKernelThreadGroupSizes(resolveKernel, out var resolveSizeX, out var resolveSizeY, out var resolveSizeZ);
        var resolveThreadGroups = Vector3Int.CeilToInt(new Vector3((float)resolution / resolveSizeX, (float)resolution / resolveSizeY, (float)(frames * frames) / resolveSizeZ));
        command.DispatchCompute(computeShader, resolveKernel, resolveThreadGroups.x, resolveThreadGroups.y, resolveThreadGroups.z);

        // Create material
        var shader = this.shader.GetDependency("ImposterShader");
        var materialPath = $"{path}.mat";

        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        material.SetFloat("_ImposterFrames", frames);
        material.SetFloat("_FramesMinusOne", frames - 1.0f);
        material.SetFloat("_RcpFramesMinusOne", 1.0f / (frames - 1.0f));
        material.SetFloat("Octahedron", imposterMode == ImposterMode.HemiOctahedron ? 0f : 1f);
        material.SetVector("_CenterOffset", center);
        material.SetVector("_Scale", new Vector4(bounds.Value.size.x, bounds.Value.size.y, bounds.Value.size.z, 2f * radius));
        material.SetVector("_WorldOffset", -center / (2f * radius));

        for (var i = 0; i < textureCount; i++)
        {
            var descriptor = descriptors[i];
            TextureFormat dstFormat;

            switch (descriptor.graphicsFormat)
            {
                case GraphicsFormat.R32_SFloat:
                case GraphicsFormat.R8_UNorm:
                    dstFormat = TextureFormat.BC4;
                    break;
                case GraphicsFormat.R8G8B8A8_UNorm:
                    dstFormat = TextureFormat.BC7;
                    break;
                case GraphicsFormat.R8G8B8A8_SRGB:
                    dstFormat = TextureFormat.BC7;
                    break;
                default:
                    throw new NotSupportedException(descriptor.graphicsFormat.ToString());
            }

            var textureName = this.shader.FindSubshaderTagValue(0, new ShaderTagId($"TextureName{i}")).name;
            var baseTexPath = $"{path} {textureName}.asset";
            var property = this.shader.FindSubshaderTagValue(0, new ShaderTagId($"TextureProperty{i}")).name;

            var source = finalRenderTextures[i];
            command.RequestAsyncReadback(source, 0, 0, resolution, 0, resolution, 0, frames * frames, rq => OnReadbackComplete(rq, dstFormat, !descriptor.sRGB, source, baseTexPath, material, property));
        }

        command.RequestAsyncReadback(minMaxBuffer, rq => OnMinMaxReadbackComplete(rq, material));

        Graphics.ExecuteCommandBuffer(command);

        // Create mesh
        var mesh = new Mesh() { name = gameObject.name };

        Vector3[] vertices = {
            new Vector3 (0, 0, 0),
            new Vector3 (1, 0, 0),
            new Vector3 (1, 1, 0),
            new Vector3 (0, 1, 0),
            new Vector3 (0, 1, 1),
            new Vector3 (1, 1, 1),
            new Vector3 (1, 0, 1),
            new Vector3 (0, 0, 1),
        };

        var xSize = bounds.Value.size.x;
        var ySize = bounds.Value.size.y;
        var zSize = bounds.Value.size.z;
        var scale = new Vector3(xSize, ySize, zSize) / (2f * radius);
        var offset1 = bounds.Value.center - center;
        var vertOffset = new Vector3(-0.5f * xSize + offset1.x, -0.5f * ySize + offset1.y, -0.5f * zSize + offset1.z) / (2f * radius);

        for (var i = 0; i < 8; i++)
            vertices[i] = Vector3.Scale(vertices[i], scale) + vertOffset;

        var triangles = new int[]
        {
            0, 2, 1, //face front
	        0, 3, 2,
            2, 3, 4, //face top
	        2, 4, 5,
            1, 2, 5, //face right
	        1, 5, 6,
            0, 7, 4, //face left
	        0, 4, 3,
            5, 4, 7, //face back
	        5, 7, 6,
            0, 6, 7, //face bottom
	        0, 1, 6
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);

        AssetDatabase.CreateAsset(mesh, $"{path} Mesh.asset");
    }

    private void OnMinMaxReadbackComplete(AsyncGPUReadbackRequest request, Material material)
    {
        var data = request.GetData<float>();
        var min = -data[0];
        var max = data[1];
        var cutoff = Mathf.InverseLerp(min, max, 0.0f);
        material.SetFloat("_Cutoff", cutoff);
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest request, TextureFormat dstFormat, bool linear, RenderTexture source, string baseTexPath, Material material, string property)
    {
        var frameCount = frames * frames;
        var texArray = new Texture2DArray(resolution, resolution, frameCount, dstFormat, true, linear);
        texArray.wrapMode = TextureWrapMode.Clamp;
        texArray.anisoLevel = 4;

        for (var j = 0; j < frameCount; j++)
        {
            var data = request.GetData<byte>(j);
            var tempTex = new Texture2D(resolution, resolution, source.graphicsFormat, TextureCreationFlags.MipChain);
            tempTex.SetPixelData(data, 0);
            tempTex.Apply(true, false);

            EditorUtility.CompressTexture(tempTex, dstFormat, TextureCompressionQuality.Best);
            Graphics.CopyTexture(tempTex, 0, texArray, j);
        }

        // Make non readable
        texArray.Apply(false, true);
        AssetDatabase.CreateAsset(texArray, baseTexPath);
        material.SetTexture(property, texArray);
    }
}
