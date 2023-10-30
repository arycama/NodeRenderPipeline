using System;
using System.Collections.Generic;
using NodeGraph;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Reflection Probe System")]
public partial class ReflectionProbeSystemNode : RenderPipelineNode
{
    [SerializeField, Pow2(512)] private int resolution = 128;
    [SerializeField] private float nearClip = 0.1f;
    [SerializeField] private float farClip = 1000f;
    [SerializeField, Range(1, 64)] private int maxActiveProbes = 16;

    [SerializeField] private RenderPipelineSubGraph gbufferSubGraph;
    [SerializeField] private RenderPipelineSubGraph processSubGraph;
    [SerializeField] private RenderPipelineSubGraph prelightSubGraph;
    [SerializeField] private RenderPipelineSubGraph lightingSubGraph;

    [Input] private GraphicsBuffer ambient;
    [Input] private RenderTargetIdentifier skyReflection;
    [Input] private RenderTargetIdentifier atmosphereTransmittance;
    [Input] private RenderTargetIdentifier exposure;
    [Input] private GpuInstanceBuffers gpuInstanceBuffers;
    [Input] private RenderTargetIdentifier shadowMap;

    [Header("Camera")]
    [Input] private Vector3 cameraPosition;
    [Input] private Matrix4x4 viewProjectionMatrix;

    [Header("Lighting")]
    [Input] private int directionalCascades;
    [Input] private SmartComputeBuffer<DirectionalLightData> directionalLightDataBuffer;
    [Input] private SmartComputeBuffer<Matrix3x4> directionalShadowMatrices;
    [Input] private RenderTargetIdentifier directionalShadows;

    [Output] private SmartComputeBuffer<ReflectionProbeData> reflectionProbeDataBuffer;
    [Output] private ComputeBuffer ambientBuffer;
    [Output] private GraphicsBuffer skyOcclusionBuffer;
    [Output] private RenderTargetIdentifier reflectionProbeOutput;

    [Input, Output] private NodeConnection connection;

    private ReadyProbeData[] readyProbes;

    private RenderTexture reflectionProbeArray, tempConvolveProbe, gbufferDepth, gbufferAlbedo, gbufferNormal, gbufferEmission;

    // Needed to copy exposure, as its currently a RenderTargetIdentifier
    private RenderTexture exposureTemp;

    private float previousExposure = 1f;
    private int relightIndex;
    private Action<AsyncGPUReadbackRequest> exposureReadback;
    private Queue<int> availableProbeIndices = new();
    private Dictionary<EnvironmentProbe, int> probeCache = new();

    public override void Initialize()
    {
        reflectionProbeArray = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RGB111110Float)
        {
            autoGenerateMips = false,
            dimension = TextureDimension.CubeArray,
            enableRandomWrite = true,
            hideFlags = HideFlags.HideAndDontSave,
            useMipMap = true,
            volumeDepth = maxActiveProbes * 6,
        }.Created();

        tempConvolveProbe = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RGB111110Float)
        {
            autoGenerateMips = false,
            dimension = TextureDimension.Cube,
            enableRandomWrite = true,
            hideFlags = HideFlags.HideAndDontSave,
            useMipMap = true
        }.Created();

        gbufferDepth = new RenderTexture(resolution, resolution, 32, RenderTextureFormat.Depth)
        {
            dimension = TextureDimension.CubeArray,
            hideFlags = HideFlags.HideAndDontSave,
            volumeDepth = maxActiveProbes * 6,
        }.Created();

        gbufferAlbedo = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            dimension = TextureDimension.CubeArray,
            hideFlags = HideFlags.HideAndDontSave,
            volumeDepth = maxActiveProbes * 6,
        }.Created();

        gbufferNormal = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.CubeArray,
            hideFlags = HideFlags.HideAndDontSave,
            volumeDepth = maxActiveProbes * 6,
        }.Created();

        gbufferEmission = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.CubeArray,
            hideFlags = HideFlags.HideAndDontSave,
            volumeDepth = maxActiveProbes * 6,
        }.Created();

        reflectionProbeOutput = reflectionProbeArray;

        exposureTemp = new RenderTexture(1, 1, 0, RenderTextureFormat.RFloat) { hideFlags = HideFlags.HideAndDontSave };

        reflectionProbeDataBuffer = new();
        reflectionProbeDataBuffer.EnsureCapcity(maxActiveProbes);

        ambientBuffer = new ComputeBuffer(maxActiveProbes * 7, sizeof(float) * 4);
        skyOcclusionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxActiveProbes * 9, sizeof(float));

        exposureReadback = OnExposureReadback;

        if (lightingSubGraph != null)
            lightingSubGraph.Initialize();

        if (gbufferSubGraph != null)
            gbufferSubGraph.Initialize();

        if (processSubGraph != null)
            processSubGraph.Initialize();

        if (prelightSubGraph != null)
            prelightSubGraph.Initialize();

        readyProbes = new ReadyProbeData[maxActiveProbes];

        // Pre-fill the probe array
        for (var i = 0; i < maxActiveProbes; i++)
            availableProbeIndices.Enqueue(i);

        // Create a camera for each probe
        for (var i = 0; i < maxActiveProbes; i++)
        {
            // Each probe gets it's own camera. This seems to be neccessary for now
            // TODO: Try removing this after we convert the logic to use matrices instead of transforms, and see if we can just use one camera. 
            var cameraGameObject = new GameObject("Reflection Camera");
            cameraGameObject.hideFlags = HideFlags.HideAndDontSave;

            var reflectionCamera = cameraGameObject.AddComponent<Camera>();
            reflectionCamera.enabled = false;
            reflectionCamera.fieldOfView = 90f;
            reflectionCamera.aspect = 1f;
            reflectionCamera.nearClipPlane = nearClip;
            reflectionCamera.farClipPlane = farClip;
            reflectionCamera.cameraType = CameraType.Reflection;

            readyProbes[i].camera = reflectionCamera;
        }
    }

    public override void Cleanup()
    {
        foreach (var probe in readyProbes)
            probe.Cleanup();

        DestroyImmediate(reflectionProbeArray);
        DestroyImmediate(gbufferDepth);
        DestroyImmediate(gbufferAlbedo);
        DestroyImmediate(gbufferNormal);
        DestroyImmediate(gbufferEmission);
        DestroyImmediate(tempConvolveProbe);
        DestroyImmediate(exposureTemp);

        ambientBuffer.Release();
        skyOcclusionBuffer.Release();

        if (lightingSubGraph != null)
            lightingSubGraph.Cleanup();

        if (gbufferSubGraph != null)
            gbufferSubGraph.Cleanup();

        if (processSubGraph != null)
            processSubGraph.Cleanup();

        if(prelightSubGraph != null) 
            prelightSubGraph.Cleanup();
    }

    public override void NodeChanged()
    {
        Cleanup();
        Initialize();

        if (lightingSubGraph != null)
        {
            lightingSubGraph.Cleanup();
            lightingSubGraph.Initialize();
        }

        if (gbufferSubGraph != null)
        {
            gbufferSubGraph.Cleanup();
            gbufferSubGraph.Initialize();
        }

        if (processSubGraph != null)
        {
            processSubGraph.Cleanup();
            processSubGraph.Initialize();
        }

        if(prelightSubGraph != null)
        {
            prelightSubGraph.Cleanup();
            prelightSubGraph.Initialize();
        }
    }

    public override void FrameRenderComplete()
    {
        if (lightingSubGraph != null)
            lightingSubGraph.FrameRenderComplete();

        if (gbufferSubGraph != null)
            gbufferSubGraph.FrameRenderComplete();

        if(processSubGraph != null)
            processSubGraph.FrameRenderComplete();

        if (prelightSubGraph != null)
            prelightSubGraph.FrameRenderComplete();
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
            return;

        // Remove any ready probes that have been destroyed
        for (var i = 0; i < readyProbes.Length; i++)
        {
            if (readyProbes[i].isValid && (readyProbes[i].probe == null || !readyProbes[i].probe.isActiveAndEnabled))
            {
                // Set the probe as invalid and release it's index to be available for future probes
                readyProbes[i].isValid = false;
                availableProbeIndices.Enqueue(i);
                probeCache.Remove(readyProbes[i].probe);
            }
        }

        AddNewProbes(context, camera);
        RelightNextProbe(context, camera);

        // Debug
        using (var scope = context.ScopedCommandBuffer())
        {
            // Readback exposure each frame and use it to update the saved value
            scope.Command.SetGlobalTexture("_ReflectionProbes", reflectionProbeArray);
            scope.Command.CopyTexture(exposure, exposureTemp);
            scope.Command.RequestAsyncReadback(exposureTemp, exposureReadback);
        }
    }

    private void OnExposureReadback(AsyncGPUReadbackRequest obj)
    {
        if (obj.hasError)
            throw new InvalidOperationException("Async Readback Error");

        // Readback exposure each frame and use it to update the saved value
        var data = obj.GetData<float>();
        previousExposure = data[0];
    }

    private void AddNewProbes(ScriptableRenderContext context, Camera mainCamera)
    {
        foreach (var newProbe in EnvironmentProbe.reflectionProbes.Keys)
        {
            var isDirty = newProbe.IsDirty;

            // Skip existing probes
            if (probeCache.TryGetValue(newProbe, out var index) && !isDirty)
                continue;

            //Skip if there are no more indices
            if (!isDirty && !availableProbeIndices.TryDequeue(out index))
                break;

            if (!isDirty)
                probeCache.Add(newProbe, index);
            else
                newProbe.ClearDirty();

            var probeData = readyProbes[index];
            var reflectionCamera = probeData.camera;

            // Do all faces at once for now, worry about culling later
            // Set position
            reflectionCamera.transform.position = newProbe.transform.position;

            using (var scope = context.ScopedCommandBuffer())
            {
                scope.Command.EnableShaderKeyword("REFLECTION_PROBE_RENDERING");
                scope.Command.SetGlobalFloat("_ExposureValue", 1f);
                scope.Command.SetGlobalFloat("_ExposureValueRcp", 1f);
                scope.Command.SetInvertCulling(true);

#if UNITY_EDITOR
                // Also need to temporarily disable async shader compilation or we end up with holes in our reflection probes, or empty probes
                ShaderUtil.SetAsyncCompilation(scope.Command, false);
#endif
            }

            for (var i = 0; i < 6; i++)
            {
                var fwd = CoreUtils.lookAtList[i];
                var up = CoreUtils.upVectorList[i];

                // TODO: Set camera matrices instead of transform?
                reflectionCamera.transform.rotation = Quaternion.LookRotation(fwd, up);

                Matrix4x4 viewProjectionMatrix;
                using (var scope = context.ScopedCommandBuffer("Reflection Probe", true))
                {
                    GraphicsUtilities.SetupCameraProperties(scope.Command, 0, reflectionCamera, context, Vector2Int.one * resolution, out viewProjectionMatrix, true);
                }

                var cullingPlanes = GeometryUtilities.CalculateFrustumPlanes(reflectionCamera.projectionMatrix * reflectionCamera.worldToCameraMatrix);

                for (var j = 0; j < cullingPlanes.Count; j++)
                {
                    // Translate planes from world space to camera-relative space
                    var plane = cullingPlanes.GetCullingPlane(j);
                    plane.distance += Vector3.Dot(plane.normal, reflectionCamera.transform.position);
                    cullingPlanes.SetCullingPlane(j, plane);
                }

                if (gbufferSubGraph != null)
                {
                    var worldToView = Matrix4x4.Rotate(Quaternion.Inverse(reflectionCamera.transform.rotation));

                    gbufferSubGraph.AddRelayInput("View Matrix", worldToView);
                    gbufferSubGraph.AddRelayInput("View Projection Matrix", viewProjectionMatrix);
                    gbufferSubGraph.AddRelayInput("Inverse View Matrix", Matrix4x4.Rotate(reflectionCamera.transform.rotation));
                    gbufferSubGraph.AddRelayInput("Culling Planes", cullingPlanes);
                    gbufferSubGraph.AddRelayInput("Culling Planes Count", cullingPlanes.Count);
                    gbufferSubGraph.AddRelayInput("Gpu Instance Buffers", gpuInstanceBuffers);

                    gbufferSubGraph.AddRelayInput("Target Resolution", resolution);
                    gbufferSubGraph.AddRelayInput("Depth Target", new RenderTargetIdentifier(gbufferDepth, 0, CubemapFace.Unknown, index * 6 + i));
                    gbufferSubGraph.AddRelayInput("Albedo Target", new RenderTargetIdentifier(gbufferAlbedo, 0, CubemapFace.Unknown, index * 6 + i));
                    gbufferSubGraph.AddRelayInput("Normal Target", new RenderTargetIdentifier(gbufferNormal, 0, CubemapFace.Unknown, index * 6 + i));
                    gbufferSubGraph.AddRelayInput("Emission Target", new RenderTargetIdentifier(gbufferEmission, 0, CubemapFace.Unknown, index * 6 + i));
                    gbufferSubGraph.Render(context, reflectionCamera, 0);
                }
            }

            if (processSubGraph != null)
            {
                processSubGraph.AddRelayInput("Index", index);
                processSubGraph.AddRelayInput("Sky Visibility Input", (RenderTargetIdentifier)gbufferDepth);
                processSubGraph.AddRelayInput("Sky Visibility Buffer", skyOcclusionBuffer);
                processSubGraph.AddRelayInput("Sky Visibility Offset", index * 9);
                processSubGraph.Render(context, reflectionCamera, 0);
            }

            readyProbes[index].isValid = true;
            readyProbes[index].probe = newProbe;
            readyProbes[index].exposure = previousExposure;

            using (var scope = context.ScopedCommandBuffer())
            {
                scope.Command.SetInvertCulling(false);
                scope.Command.DisableShaderKeyword("REFLECTION_PROBE_RENDERING");

#if UNITY_EDITOR
                // Re-enable async shader compilation
                ShaderUtil.SetAsyncCompilation(scope.Command, true);
#endif
            }

            RelightProbe(index, context, mainCamera);
        }
    }

    private void RelightNextProbe(ScriptableRenderContext context, Camera mainCamera)
    {
        using var profilerScope = context.ScopedCommandBuffer("ReflectionProbe Relight", true);

        // Find next probe that has not been relit
        for(var i = 0; i < maxActiveProbes; i++)
        {
            var index = (i + relightIndex) % maxActiveProbes;
            if (readyProbes[index].isValid)
            {
                // Found a probe, relight
                RelightProbe(index, context, mainCamera);

                // Increment index for next iteration
                relightIndex = (index + 1) % maxActiveProbes;

                // Break, as we only relight 1 per frame
                break;
            }
        }

        // Done?
        using var scopedList = ScopedPooledList<ReflectionProbeData>.Get();

        for (var i = 0; i < maxActiveProbes; i++)
        {
            if (!readyProbes[i].isValid)
                continue;

            var current = readyProbes[i].probe;
            var data = new ReflectionProbeData(current, i, readyProbes[i].exposure);
            scopedList.Value.Add(data);
        }

        using (var scope = context.ScopedCommandBuffer())
        {
            scope.Command.SetBufferData(reflectionProbeDataBuffer, scopedList);
            scope.Command.SetGlobalBuffer("_ReflectionProbeData", reflectionProbeDataBuffer);
            scope.Command.SetGlobalBuffer("_AmbientData", ambientBuffer);
            scope.Command.SetGlobalBuffer("_SkyOcclusion", skyOcclusionBuffer);
            scope.Command.SetGlobalTexture("_ReflectionProbes", reflectionProbeArray);
            scope.Command.SetGlobalInt("_ReflectionProbeCount", reflectionProbeDataBuffer.Count);
        }
    }

    private void RelightProbe(int index, ScriptableRenderContext context, Camera mainCamera)
    {
        // Update saved exposure (This value will be used for relighting)
        readyProbes[index].exposure = previousExposure;

        // Setup camera matrix based on position
        // Do all faces at once for now, worry about culling later
        // Set position
        var camera = readyProbes[index].camera;
        var probe = readyProbes[index].probe;
        camera.transform.position = probe.transform.position;

        var shadowMatrix = Matrix4x4.identity;
        if (prelightSubGraph != null)
        {
            prelightSubGraph.AddRelayInput("Probe Center", probe.transform.position + probe.ProjectionOffset);
            prelightSubGraph.AddRelayInput("Probe Extents", Vector3.one * farClip * 0.5f);
            prelightSubGraph.AddRelayInput("GpuInstanceBuffers", gpuInstanceBuffers);
            prelightSubGraph.AddRelayInput("Shadowmap", shadowMap);
            prelightSubGraph.Render(context, mainCamera, FrameCount);

            if(prelightSubGraph.GetRelayValue<Matrix4x4>("Shadow Matrix", out var newShadowMatrix))
                shadowMatrix = newShadowMatrix;
        }

        // Shared temp texture for all passes
        var tempId = Shader.PropertyToID("_ReflectionProbeTemp");
        var tempDesc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RGB111110Float) { enableRandomWrite = true };

        using (var scope = context.ScopedCommandBuffer("Reflection Probe Rendering"))
            scope.Command.GetTemporaryRT(tempId, tempDesc);

        for (var i = 0; i < 6; i++)
        {
            var fwd = CoreUtils.lookAtList[i];
            var up = CoreUtils.upVectorList[i];
            camera.transform.rotation = Quaternion.LookRotation(fwd, up);

            RelightProbeFace(context, camera, tempId, i, index, readyProbes[index].exposure, readyProbes[index], shadowMatrix);
        }

        // Convolve
        using (var scope = context.ScopedCommandBuffer())
        {
            // Generate mips (Easier to do it here and then copy over, for now.. should eventually just do it with ComputeShader or something)
            scope.Command.GenerateMips(tempConvolveProbe);

            // Calculate ambient (Before convolution)
            var convolveComputeShader = Resources.Load<ComputeShader>("AmbientConvolution");
            scope.Command.SetComputeTextureParam(convolveComputeShader, 1, "_AmbientProbeInputCubemap", tempConvolveProbe);
            scope.Command.SetComputeBufferParam(convolveComputeShader, 1, "_AmbientProbeOutputBuffer", ambientBuffer);
            scope.Command.SetComputeIntParam(convolveComputeShader, "_DstOffset", index * 7);
            scope.Command.DispatchCompute(convolveComputeShader, 1, 1, 1, 1);

            ReflectionConvolution.Convolve(scope.Command, tempConvolveProbe, reflectionProbeArray, resolution, index * 6);
        }
    }

    private void RelightProbeFace(ScriptableRenderContext context, Camera camera, int tempId, int face, int index, float exposureValue, ReadyProbeData data, Matrix4x4 shadowMatrix)
    {
        // Evaluate
        if (lightingSubGraph != null)
        {
            lightingSubGraph.AddRelayInput("Index", index);
            lightingSubGraph.AddRelayInput("Depth", (RenderTargetIdentifier)gbufferDepth);
            lightingSubGraph.AddRelayInput("_GBuffer0", (RenderTargetIdentifier)gbufferAlbedo);
            lightingSubGraph.AddRelayInput("_GBuffer1", (RenderTargetIdentifier)gbufferNormal);
            lightingSubGraph.AddRelayInput("_GBuffer2", (RenderTargetIdentifier)gbufferEmission);
            lightingSubGraph.AddRelayInput("Result", new RenderTargetIdentifier(tempId));
            lightingSubGraph.AddRelayInput("Exposure", exposure);
            lightingSubGraph.AddRelayInput("_ExposureValue", exposureValue);
            lightingSubGraph.AddRelayInput("_ExposureValueRcp", 1f / exposureValue);
            lightingSubGraph.AddRelayInput("_AmbientSh", ambient);
            lightingSubGraph.AddRelayInput("_AtmosphereTransmittance", atmosphereTransmittance);
            lightingSubGraph.AddRelayInput("_SkyReflection", skyReflection);
            lightingSubGraph.AddRelayInput("_Resolution", resolution);
            lightingSubGraph.AddRelayInput("_ReflectionProbeData", reflectionProbeDataBuffer.ComputeBuffer);
            lightingSubGraph.AddRelayInput("_ReflectionProbes", new RenderTargetIdentifier(reflectionProbeArray));
            lightingSubGraph.AddRelayInput("_ReflectionProbeCount", reflectionProbeDataBuffer.Count);
            lightingSubGraph.AddRelayInput("GpuInstanceBuffers", gpuInstanceBuffers);

            var worldToLocal = Matrix4x4.TRS(data.probe.transform.position + data.probe.ProjectionOffset, data.probe.transform.rotation, 0.5f * data.probe.ProjectionSize).inverse;
            lightingSubGraph.AddRelayInput("Probe WorldToLocal", worldToLocal);
            lightingSubGraph.AddRelayInput("Probe Center", data.probe.transform.position);

            // Camera
            lightingSubGraph.AddRelayInput("CameraPosition", cameraPosition);
            lightingSubGraph.AddRelayInput("ViewProjectionMatrix", viewProjectionMatrix);

            // Lighting
            lightingSubGraph.AddRelayInput("DirectionalCascades", directionalCascades);
            lightingSubGraph.AddRelayInput("DirectionalLightDataBuffer", directionalLightDataBuffer);
            lightingSubGraph.AddRelayInput("DirectionalShadowMatrices", directionalShadowMatrices);
            lightingSubGraph.AddRelayInput("DirectionalShadows", directionalShadows);
            lightingSubGraph.AddRelayInput("Sky Visibility Sh", skyOcclusionBuffer);
            lightingSubGraph.AddRelayInput("Sky Visibility Offset", index * 9);

            lightingSubGraph.AddRelayInput("Shadowmap", shadowMap);
            lightingSubGraph.AddRelayInput("Shadow Matrix", shadowMatrix);

            using (var scope = context.ScopedCommandBuffer("Reflection Probe Relight", true))
            {
                GraphicsUtilities.SetupCameraProperties(scope.Command, 0, camera, context, Vector2Int.one * resolution, out var viewProjectionMatrix, true);
                scope.Command.SetInvertCulling(true);
            }

            lightingSubGraph.Render(context, camera, FrameCount);

            // Copy to temp probe
            using (var scope = context.ScopedCommandBuffer("Reflection Probe Relight", true))
            {
                scope.Command.CopyTexture(tempId, 0, 0, tempConvolveProbe, face, 0);
                scope.Command.SetInvertCulling(false);
            }
        }
    }

    public struct ReadyProbeData
    {
        public bool isValid;
        public EnvironmentProbe probe;
        public Camera camera;
        public float exposure;

        public override string ToString()
        {
            return $"IsValid: {isValid}, Probe: {probe}, Exposure: {exposure}, Camera: {camera}";
        }

        public void Cleanup()
        {
            DestroyImmediate(camera.gameObject);
        }
    }
}
