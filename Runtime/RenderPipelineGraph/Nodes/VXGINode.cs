using System;
using NodeGraph;
using UnityEngine;

[NodeMenuItem("Lighting/VXGI")]
public partial class VXGINode : RenderPipelineNode
{
    [SerializeField, Pow2(256), Tooltip("Resolution of one dimension of the 3D volume"), Output] private int resolution = 64;
    [SerializeField, Tooltip("World size of the volume around the camera")] private int size = 128;
    [SerializeField, Range(1, 128)] private int samples = 32;
    [SerializeField, Range(0f, 1f)] private float smoothing = 0.05f;
    [SerializeField, Range(0, 1)] private float bias = 0.25f;

    [Input, Output] private NodeConnection connection;

    //private readonly SingleTextureCache cameraXTextures = new("Voxel GI X");
    //private readonly SingleTextureCache cameraYTextures = new("Vovel GI Y");
    //private readonly SingleTextureCache cameraZTextures = new("Voxel GI Z");
    //private readonly SingleTextureCache occlusionCache = new("Voxel Occlusion");
    //private readonly SingleTextureCache opacityCache = new("Voxel Opacity");

    // Commented out for now
    //private readonly Dictionary<Camera, (Camera, Vector3)> voxelCameras = new();

    //public override void Initialize()
    //{
    //    Shader.EnableKeyword("VOXEL_GI_ON");
    //}

    //public override void Cleanup()
    //{
    //    foreach (var voxelCamera in voxelCameras)
    //        DestroyImmediate(voxelCamera.Value.Item1.gameObject);

    //    voxelCameras.Clear();

    //    Shader.DisableKeyword("VOXEL_GI_ON");

    //    cameraXTextures.Dispose();
    //    cameraYTextures.Dispose();
    //    cameraZTextures.Dispose();
    //    occlusionCache.Dispose();
    //    opacityCache.Dispose();
    //}

    //public override void Execute(ScriptableRenderContext context, Camera camera)
    //{
    //    var opacityDesc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.R8)
    //    {
    //        dimension = TextureDimension.Tex3D,
    //        enableRandomWrite = true,
    //        volumeDepth = resolution
    //    };

    //    var opacityVolumeRT = opacityCache.GetTexture(camera, opacityDesc);

    //    var texelSize = size / resolution;
    //    var texelCenter = Vector3Int.FloorToInt(camera.transform.position / texelSize);
    //    var worldCenter = texelCenter * texelSize;
    //    var extents = size / 2;
    //    var view = worldCenter + new Vector3Int(0, 0, -extents);

    //    var texelExtents = resolution / 2;

    //    var worldMin = worldCenter - Vector3Int.one * extents;
    //    var texelMin = texelCenter - Vector3Int.one * texelExtents;
    //    var texelOffset = new Vector3Int(texelMin.x % resolution, texelMin.y % resolution, texelMin.z % resolution);

    //    var delta = Vector3.zero;
    //    Camera voxelCamera;
    //    if (!voxelCameras.TryGetValue(camera, out var cameraData))
    //    {
    //        var cameraGameObject = new GameObject("Voxel GI Camera")
    //        {
    //            hideFlags = HideFlags.HideAndDontSave,
    //        };

    //        voxelCamera = cameraGameObject.AddComponent<Camera>();
    //        voxelCamera.enabled = false;
    //        cameraData = (voxelCamera, new Vector3());
    //        voxelCameras.Add(camera, cameraData);
    //    }
    //    else
    //    {
    //        voxelCamera = cameraData.Item1;
    //        delta = cameraData.Item2;
    //        voxelCameras[camera] = (voxelCamera, worldCenter);
    //    }

    //    if (!voxelCamera.TryGetCullingParameters(out var cullingPrameters))
    //        return;

    //    cullingPrameters.cullingOptions = CullingOptions.ForceEvenIfCameraIsNotActive | CullingOptions.DisablePerObjectCulling;
    //    var cullingResults = context.Cull(ref cullingPrameters);

    //    using (var scope = context.ScopedCommandBuffer("VXGI Setup"))
    //    {
    //        // If the position has changed, we need to clear some  of the cells, as we use toroidal addressing
    //        var computeShader = Resources.Load<ComputeShader>("Lighting/VoxelGI");
    //        var clearKernel = computeShader.FindKernel("Clear");

    //        voxelCamera.worldToCameraMatrix = Matrix4x4.TRS(view, Quaternion.identity, new Vector3(1, 1, -1)).inverse;
    //        voxelCamera.projectionMatrix = Matrix4x4.Ortho(-extents, extents, -extents, extents, 0f, size);

    //        scope.Command.SetGlobalVector("_VoxelCenter", (Vector3)worldCenter);
    //        scope.Command.SetGlobalVector("_VoxelOffset", (Vector3)texelOffset);
    //        scope.Command.SetGlobalVector("_VoxelMin", (Vector3)worldMin);

    //        scope.Command.SetGlobalMatrix("_ViewProjMatrix", GL.GetGPUProjectionMatrix(voxelCamera.projectionMatrix, false) * voxelCamera.worldToCameraMatrix);
    //        scope.Command.SetGlobalMatrix("_InvViewMatrix", voxelCamera.worldToCameraMatrix.inverse);

    //        var voxelToWorldMatrix = (voxelCamera.projectionMatrix * voxelCamera.worldToCameraMatrix).ConvertToAtlasMatrix(false);

    //        scope.Command.SetGlobalMatrix("_WorldToVoxel", voxelToWorldMatrix);
    //        scope.Command.SetGlobalMatrix("_VoxelToWorld", voxelToWorldMatrix.inverse);
    //        scope.Command.SetGlobalFloat("_VoxelResolution", resolution);
    //        scope.Command.SetGlobalFloat("_VoxelSize", size);

    //        scope.Command.SetRenderTarget(opacityVolumeRT, 0, CubemapFace.Unknown, -1);
    //        scope.Command.ClearRenderTarget(false, true, Color.clear);

    //        var dummyId = Shader.PropertyToID("_Dummy");
    //        scope.Command.GetTemporaryRT(dummyId, resolution, resolution);
    //        scope.Command.SetRenderTarget(dummyId);

    //        scope.Command.ClearRandomWriteTargets();
    //        scope.Command.SetRandomWriteTarget(1, opacityVolumeRT);
    //        scope.Command.SetGlobalTexture("_VoxelGIWrite", opacityVolumeRT);
    //    }

    //    // Draw
    //    var drawingSettings = new DrawingSettings()
    //    {
    //        enableInstancing = true,
    //        sortingSettings = new SortingSettings(camera) { criteria = SortingCriteria.OptimizeStateChanges },
    //    };

    //    drawingSettings.SetShaderPassName(0, new ShaderTagId("Voxelization"));

    //    var filteringSettings = new FilteringSettings(RenderQueueRange.all);
    //    context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
    //    //var cullingPlaneList = ListPool<Plane>.Get();
    //    //GeometryUtilities.CalculateFrustumPlanes(camera, cullingPlaneList);
    //    //CustomRenderer.DrawRenderers("Voxelization", camera, context, cullingPlaneList, binding, camera.transform.position);
    //    //ListPool<Plane>.Release(cullingPlaneList);

    //    // Process
    //    using (var scope = context.ScopedCommandBuffer("VXGI Process"))
    //    {
    //        var computeShader = Resources.Load<ComputeShader>("Lighting/VoxelGI");
    //        var resultDesc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.R16)
    //        {
    //            dimension = TextureDimension.Tex3D,
    //            enableRandomWrite = true,
    //            volumeDepth = resolution * 2
    //        };

    //        var textureX = cameraXTextures.GetTexture(camera, resultDesc);
    //        var textureY = cameraYTextures.GetTexture(camera, resultDesc);
    //        var textureZ = cameraZTextures.GetTexture(camera, resultDesc);

    //        var occlusionDesc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.R16)
    //        {
    //            dimension = TextureDimension.Tex3D,
    //            enableRandomWrite = true,
    //            volumeDepth = resolution,
    //        };

    //        var occlusion = occlusionCache.GetTexture(camera, occlusionDesc);

    //        var voxelAoKernel = computeShader.FindKernel("VoxelAO");

    //        scope.Command.ClearRandomWriteTargets();

    //        scope.Command.SetComputeTextureParam(computeShader, voxelAoKernel, "_Input", opacityVolumeRT);
    //        scope.Command.SetComputeTextureParam(computeShader, voxelAoKernel, "_ResultX", textureX);
    //        scope.Command.SetComputeTextureParam(computeShader, voxelAoKernel, "_ResultY", textureY);
    //        scope.Command.SetComputeTextureParam(computeShader, voxelAoKernel, "_ResultZ", textureZ);
    //        scope.Command.SetComputeTextureParam(computeShader, voxelAoKernel, "_OcclusionResult", occlusion);

    //        scope.Command.SetComputeFloatParam(computeShader, "_Smoothing", smoothing);
    //        scope.Command.SetComputeFloatParam(computeShader, "_Samples", samples);

    //        scope.Command.DispatchNormalized(computeShader, voxelAoKernel, resolution, resolution, resolution);

    //        scope.Command.SetGlobalTexture("_VoxelGIX", textureX);
    //        scope.Command.SetGlobalTexture("_VoxelGIY", textureY);
    //        scope.Command.SetGlobalTexture("_VoxelGIZ", textureZ);
    //        scope.Command.SetGlobalTexture("_VoxelOcclusion", occlusion);
    //        scope.Command.SetGlobalTexture("_VoxelOpacity", opacityVolumeRT);
    //        scope.Command.SetGlobalFloat("_VoxelBias", bias);
    //    }
    //}

    //public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    //{
    //    var dummyId = Shader.PropertyToID("_Dummy");
    //    using var scope = context.ScopedCommandBuffer();
    //    scope.Command.ReleaseTemporaryRT(dummyId);
    //}
}