using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class ImposterBaker : ScriptableWizard
{
    [SerializeField]
    private Shader shader = null;

    [SerializeField, Range(1, 16), Tooltip("Number of angles that a single row contains. More frames reduces popping, but reduces resolution per frame")]
    private int frames = 12;

    [SerializeField, Pow2(8)]
    private int antiAliasing = 1;

    [SerializeField, Pow2(8192), Tooltip("Overall resolution of texture, higher values give sharper results, but uses more memory")]
    private int resolution = 2048;

    [SerializeField, Tooltip("HemiOctahedron only stores angles above the horizon, but results in higher resolution frames. Octahedron works at any angle, but is less detailed")]
    private ImposterMode imposterMode = ImposterMode.HemiOctahedron;

    [SerializeField, Range(4, 64)] private int maxVertices = 16;

    [SerializeField, Tooltip("If enabled, a mesh and material will be generated and applied to the selected prefab, and a LOD Gropu created if needed")]
    private bool applyToSelection = false;

    [SerializeField, Tooltip("Number of divisions that the resulting quad-mesh will contain. 3 divisions results in less texture distortion at sharp angles")]
    private int meshDivisions = 3;

    [SerializeField, Tooltip("Screen-height percentage the mesh will transition to the imposter LOD.")]
    private float lodTransitionHeight = 0.25f;

    [SerializeField, Tooltip("Screen-height percentage that the imposter will fade out and the mesh will be culled completely")]
    private float lodCullHeight = 0.05f;

    [MenuItem("Tools/Graphics/Imposter Baker")]
    public static void OnMenuSelect()
    {
        DisplayWizard<ImposterBaker>("Imposter Baker", "Bake and Close", "Bake");
    }

    private void OnWizardCreate()
    {
        Bake();
    }

    private void OnWizardOtherButton()
    {
        Bake();
    }

    private void OnEnable()
    {
        this.LoadFromEditorPrefs();
    }

    private void OnDisable()
    {
        this.SaveToEditorPrefs();
    }

    public static Vector4 Calculate(IEnumerable<Vector3> aPoints)
    {
        Vector3 xmin, xmax, ymin, ymax, zmin, zmax;
        xmin = ymin = zmin = Vector3.positiveInfinity;
        xmax = ymax = zmax = Vector3.negativeInfinity;

        foreach (var p in aPoints)
        {
            if (p.x < xmin.x) xmin = p;
            if (p.x > xmax.x) xmax = p;
            if (p.y < ymin.y) ymin = p;
            if (p.y > ymax.y) ymax = p;
            if (p.z < zmin.z) zmin = p;
            if (p.z > zmax.z) zmax = p;
        }

        var xSpan = (xmax - xmin).sqrMagnitude;
        var ySpan = (ymax - ymin).sqrMagnitude;
        var zSpan = (zmax - zmin).sqrMagnitude;
        var dia1 = xmin;
        var dia2 = xmax;
        var maxSpan = xSpan;

        if (ySpan > maxSpan)
        {
            maxSpan = ySpan;
            dia1 = ymin; dia2 = ymax;
        }

        if (zSpan > maxSpan)
        {
            dia1 = zmin; dia2 = zmax;
        }

        var center = (dia1 + dia2) * 0.5f;
        var sqRad = (dia2 - center).sqrMagnitude;
        var radius = Mathf.Sqrt(sqRad);

        foreach (var p in aPoints)
        {
            float d = (p - center).sqrMagnitude;
            if (d > sqRad)
            {
                var r = Mathf.Sqrt(d);
                radius = (radius + r) * 0.5f;
                sqRad = radius * radius;
                var offset = r - radius;
                center = (radius * center + offset * p) / r;
            }
        }

        return new Vector4(center.x, center.y, center.z, radius);
    }

    private void Bake()
    {
        var lastPath = EditorPrefs.GetString("ImposterBakeWindow.LastPath");

        lastPath = EditorUtility.SaveFolderPanel("Save Imposter", lastPath, string.Empty);
        if (string.IsNullOrEmpty(lastPath))
        {
            return;
        }

        EditorPrefs.SetString("ImposterBakeWindow.LastPath", lastPath);

        var folder = FileUtil.GetProjectRelativePath(lastPath);
        foreach (var gameObject in Selection.gameObjects)
        {
            BakeImposter(gameObject, folder);
        }
    }

    private GameObject GetPrefabRoot(GameObject gameObject, out string prefabPath)
    {
        // Set the root as the current gameObject by default
        var root = gameObject.transform;

        // Check if it's a prefab, if so, we want to get the actual prefab root and use that instead
        prefabPath = string.Empty;
        var prefab = PrefabUtility.GetCorrespondingObjectFromSource(root.gameObject);
        if (prefab != null)
        {
            // Get prefab root. This could be a model file, if so, just apply to the topmost prefab.
            if (!PrefabUtility.IsPartOfModelPrefab(prefab))
            {
                prefabPath = AssetDatabase.GetAssetPath(prefab);
                var prefabInstance = PrefabUtility.LoadPrefabContents(prefabPath);
                root = prefabInstance.transform;
            }
            else if (PrefabUtility.IsPartOfPrefabAsset(root.gameObject))
            {
                prefabPath = AssetDatabase.GetAssetPath(root.gameObject);
                var prefabInstance = PrefabUtility.LoadPrefabContents(prefabPath);
                root = prefabInstance.transform;
            }
        }

        return root.gameObject;
    }

    private IEnumerable<MeshRenderer> GetMeshRenderers(GameObject root, LODGroup lodGroup)
    {
        IEnumerable<MeshRenderer> meshRenderers; ;
        if (lodGroup != null)
        {
            // Check if last lods is an imposter
            var lods = lodGroup.GetLODs();
            var lastLod = lods.Last();

            if (lastLod.renderers.Any(renderer => renderer.gameObject.name.EndsWith("Imposter")))
            {
                if (lodGroup.lodCount < 2)
                {
                    Debug.LogError("There is only one lod, and it is an imposter. Check your prefab setup.");
                    yield break;
                }

                lastLod = lods[lodGroup.lodCount - 2];
            }

            // Set the renderable mesh renderers as the lod group.
            meshRenderers = lastLod.renderers.OfType<MeshRenderer>();
        }
        else
        {
            meshRenderers = root.GetComponentsInChildren<MeshRenderer>();
        }

        foreach (var meshRenderer in meshRenderers)
        {
            if (meshRenderer.gameObject.name.EndsWith("Imposter"))
                continue;

            if (!meshRenderer.TryGetComponent<MeshFilter>(out var meshFilter))
                continue;

            var mesh = meshFilter.sharedMesh;
            if (mesh == null)
                continue;

            yield return meshRenderer;
        }
    }

    private Vector4 GetBoundingSphere(IEnumerable<MeshRenderer> meshRenderers)
    {
        // Calculate bounds
        Vector3 xmin, xmax, ymin, ymax, zmin, zmax;
        xmin = ymin = zmin = Vector3.positiveInfinity;
        xmax = ymax = zmax = Vector3.negativeInfinity;

        var meshes = new List<Mesh>();

        foreach (var meshRenderer in meshRenderers)
        {
            if (!meshRenderer.enabled)
                continue;

            if (meshRenderer.gameObject.name.EndsWith("Imposter"))
                continue;

            var meshFilter = meshRenderer.GetComponent<MeshFilter>();
            if (meshFilter == null)
                continue;

            var mesh = meshFilter.sharedMesh;
            if (mesh == null)
                continue;

            meshes.Add(mesh);
            using var dataArray = Mesh.AcquireReadOnlyMeshData(mesh);
            var data = dataArray[0];
            var gotVertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.TempJob);

            data.GetVertices(gotVertices);
            foreach (var p in gotVertices)
            {
                if (p.x < xmin.x) xmin = p;
                if (p.x > xmax.x) xmax = p;
                if (p.y < ymin.y) ymin = p;
                if (p.y > ymax.y) ymax = p;
                if (p.z < zmin.z) zmin = p;
                if (p.z > zmax.z) zmax = p;
            }

            gotVertices.Dispose();
        }

        var sqDistX = Vector3.SqrMagnitude(xmax - xmin);
        var sqDistY = Vector3.SqrMagnitude(ymax - ymin);
        var sqDistZ = Vector3.SqrMagnitude(zmax - zmin);

        // Pick the pair of most distant points.
        var min = xmin;
        var max = xmax;
        if (sqDistY > sqDistX && sqDistY > sqDistZ)
        {
            max = ymax;
            min = ymin;
        }
        if (sqDistZ > sqDistX && sqDistZ > sqDistY)
        {
            max = zmax;
            min = zmin;
        }

        var center = (min + max) * 0.5f;
        var radius = Vector3.Distance(max, center);

        // Test every point and expand the sphere.
        // The current bounding sphere is just a good approximation and may not enclose all points.
        // From: Mathematics for 3D Game Programming and Computer Graphics, Eric Lengyel, Third Edition.
        // Page 218
        var sqRadius = radius * radius;

        foreach (var mesh in meshes)
        {
            using var dataArray = Mesh.AcquireReadOnlyMeshData(mesh);

            var data = dataArray[0];
            var gotVertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.TempJob);

            data.GetVertices(gotVertices);
            foreach (var p in gotVertices)
            {
                var diff = (p - center);
                var sqDist = diff.sqrMagnitude;
                if (sqDist > sqRadius)
                {
                    var distance = Mathf.Sqrt(sqDist); // equal to diff.Length();
                    var direction = diff / distance;
                    var G = center - radius * direction;
                    center = (G + p) / 2;
                    radius = Vector3.Distance(p, center);
                    sqRadius = radius * radius;
                }
            }

            gotVertices.Dispose();
        }

        return new Vector4(center.x, center.y, center.z, radius);
    }

    private void BakeImposter(GameObject gameObject, string folder)
    {
        var root = GetPrefabRoot(gameObject, out var prefabPath);

        // Look for a lod group. If one does not exist, just use all child mesh renderers, otherwise use the last lodgroup
        var lodGroup = root.GetComponent<LODGroup>();
        var meshRenderers = GetMeshRenderers(root, lodGroup);
        if (meshRenderers.Count() == 0)
            return;

        var boundingSphere = GetBoundingSphere(meshRenderers);
        var center = (Vector3)boundingSphere;
        var radius = boundingSphere.w;

        var propBlock = new MaterialPropertyBlock();

        var command = new CommandBuffer();

        var projectionMatrix = Matrix4x4.Ortho(-radius, radius, -radius, radius, 0f, radius * 2f);
        var gpuProjectionMatrix = GL.GetGPUProjectionMatrix(projectionMatrix, true);
        command.SetProjectionMatrix(projectionMatrix);

        // Setup view matrices
        var matrices = ListPool<Matrix4x4>.Get();
        for (var i = 0; i < frames * frames; i++)
        {
            var vec = new Vector2(i % frames, i / frames) / (frames - 1);
            var ray = imposterMode == ImposterMode.HemiOctahedron ? UnpackNormalHemiOctEncode(vec) : UnpackNormalOctQuadEncode(vec);

            var position = center + ray * radius;
            var rotation = Quaternion.LookRotation(-ray, Vector3.up);

            var viewMatrix = Matrix4x4Extensions.WorldToLocal(position, rotation);
            viewMatrix.SetRow(2, -viewMatrix.GetRow(2));
            matrices.Add(gpuProjectionMatrix * viewMatrix);
        }

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
                depthBufferBits = i == 0 ? 32 : 0,
                dimension = TextureDimension.Tex2DArray,
                msaaSamples = antiAliasing,
                sRGB = bool.Parse(this.shader.FindSubshaderTagValue(0, new ShaderTagId($"TexturesRGB{i}")).name),
                volumeDepth = frames * frames
            };

            command.GetTemporaryRT(renderTextureId, arrayDesc);

            renderTextures[i] = renderTextureId;
            descriptors[i] = arrayDesc;
        }

        command.SetRenderTarget(renderTextures, renderTextures[0], 0, CubemapFace.Unknown, Texture2DArray.allSlices);
        command.ClearRenderTarget(true, true, Color.clear);

        foreach (var meshRenderer in meshRenderers)
        {
            var objectToWorldMatrix = root.transform.worldToLocalMatrix * meshRenderer.transform.localToWorldMatrix;
            var materials = meshRenderer.sharedMaterials;

            for (var j = 0; j < materials.Length; j++)
            {
                var bakeMaterial = new Material(this.shader);
                bakeMaterial.CopyPropertiesFromMaterial(materials[j]);

                var mesh = meshRenderer.GetComponent<MeshFilter>().sharedMesh;
                propBlock.SetMatrixArray("_ViewProjectionMatrices", matrices);
                command.DrawMeshInstancedProcedural(mesh, j, bakeMaterial, 0, frames * frames, propBlock);
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
        command.DispatchNormalized(computeShader, seedPixelsKernel, resolution, resolution, frames * frames);

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
            command.DispatchNormalized(computeShader, jumpFloodKernelIndex, resolution, resolution, frames * frames);

            tempId0 = tempId;
        }

        var resolveKernel = computeShader.FindKernel("Resolve");
        command.SetComputeTextureParam(computeShader, resolveKernel, "_Input", tempId0);

        var finalRenderTextures = new RenderTexture[textureCount];
        for (var i = 0; i < textureCount; i++)
        {
            var descriptor = descriptors[i];

            descriptor.msaaSamples = 1;
            descriptor.enableRandomWrite = true;

            finalRenderTextures[i] = new RenderTexture(descriptor)
            {
                hideFlags = HideFlags.HideAndDontSave
            }.Created();
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

        // Get one final texture to write the alpha to
        var tempAlpha = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.R8)
        {
            enableRandomWrite = true,
            hideFlags = HideFlags.HideAndDontSave
        }.Created();

        command.SetComputeTextureParam(computeShader, resolveKernel, "_CombinedAlpha", tempAlpha);
        command.SetComputeIntParam(computeShader, "_TextureCount", textureCount);
        command.DispatchNormalized(computeShader, resolveKernel, resolution, resolution, frames * frames);

        // Create material
        var shader = this.shader.GetDependency("ImposterShader");
        if (shader == null)
            shader = Shader.Find("Tech Hunter/Surface/Imposter");

        var materialPath = $"{folder}/{root.name} Imposter.mat";
        var material = AssetDatabaseUtils.LoadOrCreateAssetAtPath(materialPath, () => new Material(shader));

        material.SetFloat("_ImposterFrames", frames);
        material.SetFloat("_Cutoff", 0.5f);
        material.SetFloat("Octahedron", imposterMode == ImposterMode.HemiOctahedron ? 0f : 1f);
        material.ToggleKeyword("OCTAHEDRON_ON", imposterMode == ImposterMode.Octahedron);
        material.ToggleKeyword("PARALLAX_ON", true);
        material.ToggleKeyword("PIXEL_DEPTH_OFFSET_ON", true);

        material.SetVector("_CenterOffset", center);
        material.enableInstancing = true;

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
                    dstFormat = TextureFormat.DXT5;
                    break;
                case GraphicsFormat.R8G8B8A8_SRGB:
                    dstFormat = TextureFormat.DXT5;
                    break;
                default:
                    throw new NotSupportedException(descriptor.graphicsFormat.ToString());
            }

            var textureName = this.shader.FindSubshaderTagValue(0, new ShaderTagId($"TextureName{i}")).name;
            var baseTexPath = $"{folder}/{root.name} Imposter {textureName}.asset";
            var property = this.shader.FindSubshaderTagValue(0, new ShaderTagId($"TextureProperty{i}")).name;

            var source = finalRenderTextures[i];
            command.RequestAsyncReadback(source, 0, 0, resolution, 0, resolution, 0, frames * frames, rq => OnReadbackComplete(rq, dstFormat, !descriptor.sRGB, source, baseTexPath, material, property));
        }

        command.RequestAsyncReadback(tempAlpha, 0, 0, resolution, 0, resolution, 0, 1, rq => OnTempAlphaReadbackComplete(rq, root, lodGroup, radius, center, folder, material, prefabPath));

        Graphics.ExecuteCommandBuffer(command);
    }

    private void OnTempAlphaReadbackComplete(AsyncGPUReadbackRequest request, GameObject root, LODGroup lodGroup, float radius, Vector3 center, string folder, Material material, string prefabPath)
    {
        var data = request.GetData<byte>();
        var tempTex = new Texture2D(resolution, resolution, TextureFormat.R8, false);
        tempTex.SetPixelData(data, 0);
        //tempTex.Apply(false, false);

        //var bytes = tempTex.EncodeToPNG();
        //File.WriteAllBytes(path, bytes);

        var mesh = TextureMeshGenerator.Generate(tempTex, maxVertices);

        if (applyToSelection)
        {
            // If no lod group exists, add one with all the existing renderers
            if (lodGroup == null)
            {
                lodGroup = root.AddComponent<LODGroup>();
                lodGroup.fadeMode = LODFadeMode.CrossFade;
                lodGroup.animateCrossFading = true;

                var renderers = root.GetComponentsInChildren<Renderer>();
                var lod = new LOD(lodTransitionHeight, renderers);
                lodGroup.SetLODs(new[] { lod });
            }

            var lods = new List<LOD>(lodGroup.GetLODs());

            // Check if last lod is an imposter
            GameObject imposterGameObject;

            var lastLod = lods.LastOrDefault();
            var last = lastLod.renderers.FirstOrDefault();
            var imposterExists = last != null && last.name.EndsWith("Imposter");
            if (imposterExists)
            {
                imposterGameObject = lods.Last().renderers.First().gameObject;
                imposterGameObject.transform.localScale = Vector3.one * radius * 2;
                imposterGameObject.transform.localPosition = center;
            }
            else
            {
                imposterGameObject = new GameObject($"{root.name} Imposter");
                imposterGameObject.transform.SetParent(root.transform, false);
                imposterGameObject.transform.localScale = Vector3.one * radius * 2;
                imposterGameObject.transform.localPosition = center;
            }

            // Create mesh
            //var mesh = new Mesh() { name = imposterGameObject.name };

            //var interval = 1f / meshDivisions;

            //var vertices = new Vector3[(meshDivisions + 1) * (meshDivisions + 1)];
            //for (var y = 0; y <= meshDivisions; y++)
            //{
            //    for (var x = 0; x <= meshDivisions; x++)
            //    {
            //        vertices[x + y * (meshDivisions + 1)] = new Vector3(x * interval - 0.5f, y * interval - 0.5f);
            //    }
            //}

            //var triangles = new int[meshDivisions * meshDivisions * 6];
            //for (int ti = 0, vi = 0, y = 0; y < meshDivisions; y++, vi++)
            //{
            //    for (int x = 0; x < meshDivisions; x++, ti += 6, vi++)
            //    {
            //        triangles[ti] = vi;
            //        triangles[ti + 3] = triangles[ti + 2] = vi + 1;
            //        triangles[ti + 4] = triangles[ti + 1] = vi + meshDivisions + 1;
            //        triangles[ti + 5] = vi + meshDivisions + 2;
            //    }
            //}

            //mesh.vertices = vertices;
            //mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);

            AssetDatabase.CreateAsset(mesh, $"{folder}/{root.name} Quad.asset");

            var imposterMeshFilter = imposterExists ? imposterGameObject.GetComponent<MeshFilter>() : imposterGameObject.AddComponent<MeshFilter>();
            imposterMeshFilter.sharedMesh = mesh;// Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            var imposterMeshRenderer = imposterExists ? imposterGameObject.GetComponent<MeshRenderer>() : imposterGameObject.AddComponent<MeshRenderer>();
            imposterMeshRenderer.sharedMaterial = material;

            if (!imposterExists)
            {
                // Ensure last lod doesn't transition too soon
                lastLod.screenRelativeTransitionHeight = lodTransitionHeight;
                lods[lods.Count - 1] = lastLod;

                lods.Add(new LOD(lodCullHeight, new[] { imposterMeshRenderer }));
                lodGroup.SetLODs(lods.ToArray());
            }

            EditorUtility.SetDirty(root);

            if (!string.IsNullOrEmpty(prefabPath))
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
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


}