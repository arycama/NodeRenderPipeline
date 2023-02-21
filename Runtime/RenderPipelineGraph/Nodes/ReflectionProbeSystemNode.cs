using System;
using System.Collections.Generic;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Reflection Probe System")]
public partial class ReflectionProbeSystemNode : RenderPipelineNode
{
    private readonly static Vector4[] cullingPlanes = new Vector4[6];
    private static readonly RenderBuffer[] colorBuffers = new RenderBuffer[3];

    [SerializeField, Pow2(512)] private int resolution = 128;
    [SerializeField] private float nearClip = 0.1f;
    [SerializeField] private float farClip = 1000f;
    [SerializeField] private LayerMask layerMask = ~0;
    [SerializeField, Range(1, 64)] private int maxActiveProbes = 16;

    [Header("Clustered Lighting")]
    [SerializeField, Pow2(64)] private int tileSize = 16;
    [SerializeField, Pow2(64)] private int clusterDepth = 32;
    [SerializeField, Pow2(64)] private int maxLightsPerTile = 32;

    [SerializeField] private RenderPipelineSubGraph reflectionProbeSubGraph;

    [Input] private ComputeBuffer ambient;
    [Input] private RenderTargetIdentifier skyReflection;
    [Input] private RenderTargetIdentifier atmosphereTransmittance;
    [Input] private RenderTargetIdentifier exposure;
    [Input] private GpuInstanceBuffers gpuInstanceBuffers;

    [Output] private SmartComputeBuffer<ReflectionProbeData> reflectionProbeDataBuffer;
    [Output] private ComputeBuffer ambientBuffer;
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
        reflectionProbeDataBuffer.EnsureCapcity(1);

        ambientBuffer = new ComputeBuffer(maxActiveProbes * 3, sizeof(float) * 4);

        exposureReadback = OnExposureReadback;

        if (reflectionProbeSubGraph != null)
            reflectionProbeSubGraph.Initialize();
    }

    public override void Cleanup()
    {
        foreach (var probe in readyProbes)
        {
            DestroyImmediate(probe.Camera.gameObject);

            foreach (var item in probe.AlbedoMetallic)
                DestroyImmediate(item);

            foreach (var item in probe.NormalRoughness)
                DestroyImmediate(item);

            foreach (var item in probe.Emission)
                DestroyImmediate(item);
        }

        DestroyImmediate(reflectionProbeArray);
        DestroyImmediate(tempConvolveProbe);
        DestroyImmediate(exposureTemp);

        ambientBuffer.Release();

        readyProbes.Clear();

        if (reflectionProbeSubGraph != null)
            reflectionProbeSubGraph.Cleanup();
    }

    public override void NodeChanged()
    {
        if (reflectionProbeSubGraph != null)
        {
            reflectionProbeSubGraph.Cleanup();
            reflectionProbeSubGraph.Initialize();
        }
    }

    public override void FrameRenderComplete()
    {
        if (reflectionProbeSubGraph != null)
            reflectionProbeSubGraph.FrameRenderComplete();
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
        foreach (var newProbe in CustomReflectionProbe.reflectionProbes)
        {
            // 
            if (readyProbes.Count == maxActiveProbes)
                return;

            // Skip existing probes
            if (readyProbes.Find(probe => probe.Probe == newProbe) != null)
                continue;

            var cameraGameObject = new GameObject("Reflection Camera");
            cameraGameObject.hideFlags = HideFlags.HideAndDontSave;

            var camera = cameraGameObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 90f;
            camera.aspect = 1f;
            camera.nearClipPlane = nearClip;
            camera.farClipPlane = farClip;

            // Do all faces at once for now, worry about culling later
            // Set position
            camera.transform.position = newProbe.transform.position;

            var item = new ReadyProbeData(newProbe, new RenderTexture[6], new RenderTexture[6], new RenderTexture[6], camera, previousExposure);

            using (var scope = context.ScopedCommandBuffer())
                scope.Command.EnableShaderKeyword("REFLECTION_PROBE_RENDERING");

            for (var i = 0; i < 6; i++)
            {
                var fwd = CoreUtils.lookAtList[i];
                var up = CoreUtils.upVectorList[i];
                camera.transform.rotation = Quaternion.LookRotation(fwd, up);

                if (!camera.TryGetCullingParameters(out var cullingPrameters))
                    throw new InvalidOperationException();

                var albedoMetallic = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB).Created();
                colorBuffers[0] = albedoMetallic.colorBuffer;

                var normalRoughness = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear).Created();
                colorBuffers[1] = normalRoughness.colorBuffer;

                var emission = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear).Created();
                colorBuffers[2] = emission.colorBuffer;

                var renderTargetSetup = new RenderTargetSetup(colorBuffers, albedoMetallic.depthBuffer, 0, (CubemapFace)i);
                var renderTargetBinding = new RenderTargetBinding(renderTargetSetup);

                // Draw
                cullingPrameters.cullingOptions = CullingOptions.ForceEvenIfCameraIsNotActive | CullingOptions.DisablePerObjectCulling;
                cullingPrameters.cullingMask = (uint)layerMask.value;
                var cullingResults = context.Cull(ref cullingPrameters);

                using (var scope = context.ScopedCommandBuffer("Reflection Probe", true))
                {
                    scope.Command.SetRenderTarget(renderTargetBinding);
                    scope.Command.ClearRenderTarget(true, true, Color.clear);
                    GraphicsUtilities.SetupCameraProperties(scope.Command, 0, camera, context, Vector2Int.one * resolution, true);
                    scope.Command.SetInvertCulling(true);

                    scope.Command.SetGlobalFloat("_ExposureValue", 1f);
                    scope.Command.SetGlobalFloat("_ExposureValueRcp", 1f);

                    GeometryUtilities.CalculateFrustumPlanes(camera, cullingPlanes);

                    // TODO: Convert to subgraph
                    //foreach (var terrain in TerrainRenderer.TerrainRenderers)
                    //{
                    //    terrain.Cull(scope.Command, camera.transform.position, cullingPlanes, 6);
                    //    terrain.Render(scope.Command, "ShadowCaster", camera.transform.position);
                    //}

                    var rendererList = context.CreateRendererList(new UnityEngine.Rendering.RendererUtils.RendererListDesc(new ShaderTagId("Deferred"), cullingResults, camera)
                    {
                        excludeObjectMotionVectors = true,
                        layerMask = layerMask,
                        renderQueueRange = RenderQueueRange.opaque,
                        rendererConfiguration = PerObjectData.None,
                        sortingCriteria = SortingCriteria.CommonOpaque,
                    });

                    scope.Command.DrawRendererList(rendererList);

                    item.AlbedoMetallic[i] = albedoMetallic;
                    item.NormalRoughness[i] = normalRoughness;
                    item.Emission[i] = emission;
                }
            }

            using (var scope = context.ScopedCommandBuffer())
            {
                scope.Command.SetInvertCulling(false);
                scope.Command.DisableShaderKeyword("REFLECTION_PROBE_RENDERING");
            }

            var index = readyProbes.Count;
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

            RelightProbeFace(context, relightData, camera, tempId, i, fwd, up);
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
            scope.Command.SetComputeIntParam(convolveComputeShader, "_DstOffset", index * 3);
            scope.Command.DispatchCompute(convolveComputeShader, 1, 1, 1, 1);

            ReflectionConvolution.Convolve(scope.Command, tempConvolveProbe, reflectionProbeArray, resolution, index * 6);
        }
    }

    private void RelightProbeFace(ScriptableRenderContext context, ReadyProbeData relightData, Camera camera, int tempId, int i, Vector3 fwd, Vector3 up)
    {
        // Evaluate
        if (reflectionProbeSubGraph != null)
        {
            reflectionProbeSubGraph.AddRelayInput("Depth", new RenderTargetIdentifier(relightData.AlbedoMetallic[i]));
            reflectionProbeSubGraph.AddRelayInput("_GBuffer0", new RenderTargetIdentifier(relightData.AlbedoMetallic[i]));
            reflectionProbeSubGraph.AddRelayInput("_GBuffer1", new RenderTargetIdentifier(relightData.NormalRoughness[i]));
            reflectionProbeSubGraph.AddRelayInput("_GBuffer2", new RenderTargetIdentifier(relightData.Emission[i]));
            reflectionProbeSubGraph.AddRelayInput("Result", new RenderTargetIdentifier(tempId));
            reflectionProbeSubGraph.AddRelayInput("_ExposureValue", previousExposure);
            reflectionProbeSubGraph.AddRelayInput("_ExposureValueRcp", 1f / previousExposure);
            reflectionProbeSubGraph.AddRelayInput("_AmbientSh", ambient);
            reflectionProbeSubGraph.AddRelayInput("_AtmosphereTransmittance", atmosphereTransmittance);
            reflectionProbeSubGraph.AddRelayInput("_SkyReflection", skyReflection);
            reflectionProbeSubGraph.AddRelayInput("_Resolution", resolution);
            reflectionProbeSubGraph.AddRelayInput("_ReflectionProbeData", reflectionProbeDataBuffer.ComputeBuffer);
            reflectionProbeSubGraph.AddRelayInput("_ReflectionProbes", new RenderTargetIdentifier(reflectionProbeArray));
            reflectionProbeSubGraph.AddRelayInput("_ReflectionProbeCount", reflectionProbeDataBuffer.Count);
            reflectionProbeSubGraph.AddRelayInput("GpuInstanceBuffers", gpuInstanceBuffers);

            using (var scope = context.ScopedCommandBuffer("Reflection Probe Relight", true))
            {
                GraphicsUtilities.SetupCameraProperties(scope.Command, 0, camera, context, Vector2Int.one * resolution, true);
                scope.Command.SetInvertCulling(true);
            }

            reflectionProbeSubGraph.Render(context, camera, FrameCount);

            // Copy to temp probe
            using (var scope = context.ScopedCommandBuffer("Reflection Probe Relight", true))
            {
                scope.Command.CopyTexture(tempId, 0, 0, tempConvolveProbe, i, 0);
                scope.Command.SetInvertCulling(false);
            }
        }
    }

    public class ReadyProbeData
    {
        public CustomReflectionProbe Probe;
        public RenderTexture[] AlbedoMetallic;
        public RenderTexture[] NormalRoughness;
        public RenderTexture[] Emission;
        public Camera Camera;
        public float exposure;

        public ReadyProbeData(CustomReflectionProbe item1, RenderTexture[] item2, RenderTexture[] item3, RenderTexture[] emission, Camera camera, float exposure)
        {
            Probe = item1 ?? throw new ArgumentNullException(nameof(item1));
            AlbedoMetallic = item2 ?? throw new ArgumentNullException(nameof(item2));
            NormalRoughness = item3 ?? throw new ArgumentNullException(nameof(item3));
            Emission = emission ?? throw new ArgumentNullException(nameof(emission));
            Camera = camera ?? throw new ArgumentException(nameof(camera));
            this.exposure = exposure;
        }
    }
}
