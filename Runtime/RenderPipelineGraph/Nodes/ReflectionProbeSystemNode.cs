using System;
using System.Collections.Generic;
using NodeGraph;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Reflection Probe System")]
public partial class ReflectionProbeSystemNode : RenderPipelineNode
{
    private readonly static Vector4[] cullingPlanes = new Vector4[6];

    [SerializeField, Pow2(512)] private int resolution = 128;
    [SerializeField] private float nearClip = 0.1f;
    [SerializeField] private float farClip = 1000f;
    [SerializeField, Range(1, 64)] private int maxActiveProbes = 16;

    [SerializeField] private RenderPipelineSubGraph gbufferSubGraph;
    [SerializeField] private RenderPipelineSubGraph processSubGraph;
    [SerializeField] private RenderPipelineSubGraph lightingSubGraph;

    [Input] private GraphicsBuffer ambient;
    [Input] private RenderTargetIdentifier skyReflection;
    [Input] private RenderTargetIdentifier atmosphereTransmittance;
    [Input] private RenderTargetIdentifier exposure;
    [Input] private GpuInstanceBuffers gpuInstanceBuffers;

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

    private readonly List<ReadyProbeData> readyProbes = new();

    private RenderTexture reflectionProbeArray, tempConvolveProbe;

    // Needed to copy exposure, as its currently a RenderTargetIdentifier
    private RenderTexture exposureTemp;

    private float previousExposure;
    private int relightIndex;
    private Action<AsyncGPUReadbackRequest> exposureReadback;

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
    }

    public override void Cleanup()
    {
        foreach (var probe in readyProbes)
            probe.Cleanup();

        DestroyImmediate(reflectionProbeArray);
        DestroyImmediate(tempConvolveProbe);
        DestroyImmediate(exposureTemp);

        ambientBuffer.Release();
        skyOcclusionBuffer.Release();

        readyProbes.Clear();

        if (lightingSubGraph != null)
            lightingSubGraph.Cleanup();

        if (gbufferSubGraph != null)
            gbufferSubGraph.Cleanup();

        if (processSubGraph != null)
            processSubGraph.Cleanup();
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
    }

    public override void FrameRenderComplete()
    {
        if (lightingSubGraph != null)
            lightingSubGraph.FrameRenderComplete();

        if (gbufferSubGraph != null)
            gbufferSubGraph.FrameRenderComplete();
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        // Remove any ready probes that have been destroyed
        readyProbes.RemoveAll(probe => probe.Probe == null || !probe.Probe.isActiveAndEnabled);

        AddNewProbes(context);
        RelightNextProbe(context);

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

    private void AddNewProbes(ScriptableRenderContext context)
    {
        foreach (var newProbe in EnvironmentProbe.reflectionProbes.Keys)
        {
            // Skip if we have max probe
            if (readyProbes.Count == maxActiveProbes)
                return;

            // Skip existing probes
            if (readyProbes.Find(probe => probe.Probe == newProbe) != null)
                continue;

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

            var depth = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.Depth) { dimension = TextureDimension.Cube }.Created();
            var albedo = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { dimension = TextureDimension.Cube }.Created();
            var normal = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { dimension = TextureDimension.Cube }.Created();
            var emission = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear) { dimension = TextureDimension.Cube }.Created();

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
                    GeometryUtilities.CalculateFrustumPlanes(reflectionCamera, cullingPlanes);
                }

                if (gbufferSubGraph != null)
                {
                    var worldToView = Matrix4x4.Rotate(Quaternion.Inverse(reflectionCamera.transform.rotation));

                    gbufferSubGraph.AddRelayInput("View Matrix", worldToView);
                    gbufferSubGraph.AddRelayInput("View Projection Matrix", viewProjectionMatrix);
                    gbufferSubGraph.AddRelayInput("Inverse View Matrix", Matrix4x4.Rotate(reflectionCamera.transform.rotation));
                    gbufferSubGraph.AddRelayInput("Culling Planes", new Vector4Array(cullingPlanes));
                    gbufferSubGraph.AddRelayInput("Culling Planes Count", cullingPlanes.Length);
                    gbufferSubGraph.AddRelayInput("Gpu Instance Buffers", gpuInstanceBuffers);

                    gbufferSubGraph.AddRelayInput("Target Resolution", resolution);
                    gbufferSubGraph.AddRelayInput("Depth Target", new RenderTargetIdentifier(depth, 0, (CubemapFace)i));
                    gbufferSubGraph.AddRelayInput("Albedo Target", new RenderTargetIdentifier(albedo, 0, (CubemapFace)i));
                    gbufferSubGraph.AddRelayInput("Normal Target", new RenderTargetIdentifier(normal, 0, (CubemapFace)i));
                    gbufferSubGraph.AddRelayInput("Emission Target", new RenderTargetIdentifier(emission, 0, (CubemapFace)i));
                    gbufferSubGraph.Render(context, reflectionCamera, 0);
                }
            }

            var index = readyProbes.Count;
            if (processSubGraph != null)
            {
                processSubGraph.AddRelayInput("Sky Visibility Input", (RenderTargetIdentifier)depth);
                processSubGraph.AddRelayInput("Sky Visibility Buffer", skyOcclusionBuffer);
                processSubGraph.AddRelayInput("Sky Visibility Offset", index * 9);
                processSubGraph.Render(context, reflectionCamera, 0);
            }

            var item = new ReadyProbeData(newProbe, depth, albedo, normal, emission, reflectionCamera, previousExposure);

            using (var scope = context.ScopedCommandBuffer())
            {
                scope.Command.SetInvertCulling(false);
                scope.Command.DisableShaderKeyword("REFLECTION_PROBE_RENDERING");

#if UNITY_EDITOR
                // Re-enable async shader compilation
                ShaderUtil.SetAsyncCompilation(scope.Command, true);
#endif
            }

            readyProbes.Add(item);
            RelightProbe(index, context);
        }
    }

    private void RelightNextProbe(ScriptableRenderContext context)
    {
        using var profilerScope = context.ScopedCommandBuffer("ReflectionProbe Relight", true);

        if (readyProbes.Count > 0)
        {
            // Incase any have been removed
            relightIndex = relightIndex % readyProbes.Count;
            RelightProbe(relightIndex, context);
            relightIndex = (relightIndex + 1) % readyProbes.Count;
        }

        // Done?
        using var scopedList = ScopedPooledList<ReflectionProbeData>.Get();

        for (var i = 0; i < readyProbes.Count; i++)
        {
            var current = readyProbes[i].Probe;
            var min = current.transform.position - current.Size * 0.5f;
            var max = current.transform.position + current.Size * 0.5f;

            var data = new ReflectionProbeData(current.transform.position, current.transform.rotation, min, current.BlendDistance, max, i, current.transform.position, current.BoxProjection, readyProbes[i].exposure);
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

    private void RelightProbe(int index, ScriptableRenderContext context)
    {
        var relightData = readyProbes[index];
        var probe = relightData.Probe;

        // Update saved exposure (This value will be used for relighting)
        relightData.exposure = previousExposure;

        // Setup camera matrix based on position
        // Do all faces at once for now, worry about culling later
        // Set position
        var camera = relightData.Camera;
        camera.transform.position = probe.transform.position;

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

            RelightProbeFace(context, relightData, camera, tempId, i, index);
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

    private void RelightProbeFace(ScriptableRenderContext context, ReadyProbeData relightData, Camera camera, int tempId, int face, int index)
    {
        // Evaluate
        if (lightingSubGraph != null)
        {
            lightingSubGraph.AddRelayInput("Depth", new RenderTargetIdentifier(relightData.Depth));
            lightingSubGraph.AddRelayInput("_GBuffer0", new RenderTargetIdentifier(relightData.Albedo));
            lightingSubGraph.AddRelayInput("_GBuffer1", new RenderTargetIdentifier(relightData.Normal));
            lightingSubGraph.AddRelayInput("_GBuffer2", new RenderTargetIdentifier(relightData.Emission));
            lightingSubGraph.AddRelayInput("Result", new RenderTargetIdentifier(tempId));
            lightingSubGraph.AddRelayInput("Exposure", exposure);
            lightingSubGraph.AddRelayInput("_ExposureValue", relightData.exposure);
            lightingSubGraph.AddRelayInput("_ExposureValueRcp", 1f / relightData.exposure);
            lightingSubGraph.AddRelayInput("_AmbientSh", ambient);
            lightingSubGraph.AddRelayInput("_AtmosphereTransmittance", atmosphereTransmittance);
            lightingSubGraph.AddRelayInput("_SkyReflection", skyReflection);
            lightingSubGraph.AddRelayInput("_Resolution", resolution);
            lightingSubGraph.AddRelayInput("_ReflectionProbeData", reflectionProbeDataBuffer.ComputeBuffer);
            lightingSubGraph.AddRelayInput("_ReflectionProbes", new RenderTargetIdentifier(reflectionProbeArray));
            lightingSubGraph.AddRelayInput("_ReflectionProbeCount", reflectionProbeDataBuffer.Count);
            lightingSubGraph.AddRelayInput("GpuInstanceBuffers", gpuInstanceBuffers);

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

    public class ReadyProbeData
    {
        public EnvironmentProbe Probe;
        public RenderTexture Depth;
        public RenderTexture Albedo;
        public RenderTexture Normal;
        public RenderTexture Emission;
        public Camera Camera;
        public float exposure;

        public ReadyProbeData(EnvironmentProbe item1, RenderTexture depth, RenderTexture albedo, RenderTexture normal, RenderTexture emission, Camera camera, float exposure)
        {
            Depth = depth ?? throw new ArgumentNullException(nameof(depth));
            Probe = item1 ?? throw new ArgumentNullException(nameof(item1));
            Albedo = albedo ?? throw new ArgumentNullException(nameof(albedo));
            Normal = normal ?? throw new ArgumentNullException(nameof(normal));
            Emission = emission ?? throw new ArgumentNullException(nameof(emission));
            Camera = camera ?? throw new ArgumentException(nameof(camera));
            this.exposure = exposure;
        }

        public void Cleanup()
        {
            DestroyImmediate(Camera.gameObject);
            DestroyImmediate(Depth);
            DestroyImmediate(Albedo);
            DestroyImmediate(Normal);
            DestroyImmediate(Emission);
        }
    }
}
