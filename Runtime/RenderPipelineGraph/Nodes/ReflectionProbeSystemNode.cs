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

    [Input] private ComputeBuffer ambient;
    [Input] private RenderTargetIdentifier skyReflection;
    [Input] private RenderTargetIdentifier atmosphereTransmittance;
    [Input] private RenderTargetIdentifier exposure;

    [Output] private SmartComputeBuffer<ReflectionProbeData> reflectionProbeDataBuffer;
    [Output] private ComputeBuffer ambientBuffer;
    [Output] private RenderTargetIdentifier reflectionProbeOutput;
    [Input, Output] private NodeConnection connection;

    private readonly List<ReadyProbeData> readyProbes = new();

    private SmartComputeBuffer<DirectionalLightData> directionalLightBuffer;
    private SmartComputeBuffer<LightData> lightDataBuffer;
    private RenderTexture reflectionProbeArray, tempConvolveProbe;

    // Needed to copy exposure, as its currently a RenderTargetIdentifier
    private RenderTexture exposureTemp;

    private float previousExposure;
    private int relightIndex;
    private ComputeBuffer lightList;
    private ComputeBuffer counterBuffer;
    private static readonly uint[] zeroArray = new uint[1] { 0 };
    private int lightClusterId;
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

        // Clustered lighting
        counterBuffer = new ComputeBuffer(1, sizeof(uint)) { name = nameof(counterBuffer) };
        lightClusterId = GetShaderPropertyId();

        reflectionProbeDataBuffer = new();
        reflectionProbeDataBuffer.EnsureCapcity(1);

        ambientBuffer = new ComputeBuffer(maxActiveProbes * 3, sizeof(float) * 4);

        exposureReadback = OnExposureReadback;

        directionalLightBuffer = new();
        lightDataBuffer = new();
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
        counterBuffer.Release();

        readyProbes.Clear();
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

            // Setup lighting
            if (!camera.TryGetCullingParameters(out var cullingPrameters))
                throw new InvalidOperationException();

            cullingPrameters.cullingOptions = CullingOptions.ForceEvenIfCameraIsNotActive | CullingOptions.DisablePerObjectCulling | CullingOptions.NeedsLighting;
            cullingPrameters.cullingMask = (uint)layerMask.value;
            var cullingResults = context.Cull(ref cullingPrameters);

            // TODO: Lots of repeated code from setup lighting node, find a way to re-use?
            using var directionalLightDatas = ScopedPooledList<DirectionalLightData>.Get();
            using var lightDatas = ScopedPooledList<LightData>.Get();

            for (var j = 0; j < cullingResults.visibleLights.Length; j++)
            {
                var visibleLight = cullingResults.visibleLights[j];

                if (visibleLight.lightType == LightType.Directional)
                {
                    var angularDiameter = visibleLight.light.TryGetComponent<CelestialBody>(out var celestialBody) ? celestialBody.AngularDiameter : 0.53f;
                    var direction = -visibleLight.localToWorldMatrix.GetColumn(2);
                    var data = new DirectionalLightData((Vector4)visibleLight.finalColor, angularDiameter, direction, -1);
                    directionalLightDatas.Value.Add(data);
                }
                // TODO: Implement some kind of processor so we can swap out any unsupported lights.. or just replace the UnityEngine.Light component entirely
                else if (GetLightType(visibleLight) != -1)
                {
                    var lightData = SetupLight(visibleLight, camera);
                    lightDatas.Value.Add(lightData);
                }
            }

            using (var scope = context.ScopedCommandBuffer())
            {
                GraphicsUtilities.SetupCameraProperties(scope.Command, 0, camera, context, Vector2Int.one * resolution, true);
                scope.Command.SetBufferData(directionalLightBuffer, directionalLightDatas);
                scope.Command.SetBufferData(lightDataBuffer, lightDatas);
            }

            // TODO: Light Culling pass
            ClusteredLIghting(context, camera);

            // Deferred
            var deferredComputeShader = Resources.Load<ComputeShader>("Core/Deferred");

            // TODO: Most of this code is copied from DeferredLightingNode, can we share it somehow?
            using (var scope = context.ScopedCommandBuffer("Reflection Probe Relight", true))
            {
                scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "Depth", relightData.AlbedoMetallic[i], 0, RenderTextureSubElement.Depth);
                scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_GBuffer0", relightData.AlbedoMetallic[i], 0, RenderTextureSubElement.Color);
                scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_GBuffer1", relightData.NormalRoughness[i], 0, RenderTextureSubElement.Color);
                scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_GBuffer2", relightData.Emission[i], 0, RenderTextureSubElement.Color);
                scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "Result", tempId);
                scope.Command.SetComputeFloatParam(deferredComputeShader, "_ExposureValue", previousExposure);
                scope.Command.SetComputeFloatParam(deferredComputeShader, "_ExposureValueRcp", 1f / previousExposure);
                scope.Command.SetComputeBufferParam(deferredComputeShader, 0, "_AmbientSh", ambient);
                scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_AtmosphereTransmittance", atmosphereTransmittance);
                scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_SkyReflection", skyReflection);

                scope.Command.SetComputeIntParam(deferredComputeShader, "_Resolution", resolution);

                scope.Command.SetComputeBufferParam(deferredComputeShader, 0, "_LightClusterList", lightList);
                scope.Command.SetComputeBufferParam(deferredComputeShader, 0, "_LightData", lightDataBuffer);
                scope.Command.SetComputeBufferParam(deferredComputeShader, 0, "_DirectionalLightData", directionalLightBuffer);
                scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_LightClusterIndices", lightClusterId);
                scope.Command.SetComputeIntParam(deferredComputeShader, "_DirectionalLightCount", directionalLightBuffer.Count);
                scope.Command.SetComputeIntParam(deferredComputeShader, "_LightCount", lightDataBuffer.Count);


                scope.Command.SetComputeBufferParam(deferredComputeShader, 0, "_ReflectionProbeData", reflectionProbeDataBuffer);
                scope.Command.SetComputeTextureParam(deferredComputeShader, 0, "_ReflectionProbes", reflectionProbeArray);
                scope.Command.SetComputeIntParam(deferredComputeShader, "_ReflectionProbeCount", reflectionProbeDataBuffer.Count);

                var clusterScale = clusterDepth / Mathf.Log(camera.farClipPlane / camera.nearClipPlane, 2f);
                var clusterBias = -(clusterDepth * Mathf.Log(camera.nearClipPlane, 2f) / Mathf.Log(camera.farClipPlane / camera.nearClipPlane, 2f));

                scope.Command.SetComputeFloatParam(deferredComputeShader, "_ClusterScale", clusterScale);
                scope.Command.SetComputeFloatParam(deferredComputeShader, "_ClusterBias", clusterBias);
                scope.Command.SetComputeIntParam(deferredComputeShader, "_TileSize", tileSize);

                var viewToWorld = Matrix4x4.Rotate(Quaternion.LookRotation(fwd, up));
                var viewDirMatrix = Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(Vector2Int.one * resolution, Vector2.zero, 90, 1, viewToWorld, true);
                scope.Command.SetComputeMatrixParam(deferredComputeShader, "_PixelCoordToViewDirWS", viewDirMatrix);

                // Dispatch
                using (var keywordScope = scope.Command.KeywordScope("REFLECTION_PROBE_RENDERING"))
                    scope.Command.DispatchNormalized(deferredComputeShader, 0, resolution, resolution, 1);

                // Copy to temp probe
                scope.Command.CopyTexture(tempId, 0, 0, tempConvolveProbe, i, 0);
            }
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

    // TODO: Duplicated from setuplighting node but without shadow, should reuse this
    private LightData SetupLight(VisibleLight visibleLight, Camera camera)
    {
        var size = Vector2.zero;
        var shapeRadius = 0.025f;
        var isAreaLight = false;
        var light = visibleLight.light;
        if (light.TryGetComponent<AdditionalLightData>(out var data))
        {
            size = new Vector2(data.ShapeWidth, data.ShapeHeight);
            shapeRadius = data.ShapeRadius;
            isAreaLight = data.AreaLightType != AreaLightType.None;
        }

        // If this is a shadow-casting spotlight, set it's shadow index and add it's visibleLightIndex to the array.
        var near = light.shadowNearPlane;
        var far = visibleLight.range;

        // Scale for box light
        var range = visibleLight.range;
        var right = visibleLight.localToWorldMatrix.Right();
        var up = visibleLight.localToWorldMatrix.Up();
        float angleScale = 0f, angleOffset = 1f;

        // Shadows
        var shadowIndex = -1;

        // Spotlight shape
        if (visibleLight.lightType == LightType.Spot)
        {
            if (isAreaLight)
            {
                size.x *= 0.5f;
                size.y *= 0.5f;
            }
            else
            {
                if (light.shape == LightShape.Box || light.shape == LightShape.Pyramid)
                {
                    if (light.shape == LightShape.Pyramid)
                    {
                        // Get width and height for the current frustum
                        var frustumSize = 2f * Mathf.Tan(visibleLight.spotAngle * Mathf.Deg2Rad * 0.5f);
                        var aspectRatio = size.x / size.y;

                        size.x = size.y = frustumSize;
                        if (aspectRatio >= 1.0f)
                            size.x *= aspectRatio;
                        else
                            size.y /= aspectRatio;
                    }

                    // Rescale for cookies and windowing.
                    right *= 2f / size.x;
                    up *= 2f / size.y;

                    // These are the neutral values allowing GetAngleAnttenuation in shader code to return 1.0
                    angleScale = 0f;
                    angleOffset = 1f;
                }
                else
                {
                    // Spotlight angle
                    var innerConePercent = light.innerSpotAngle / visibleLight.spotAngle;
                    var cosSpotOuterHalfAngle = Mathf.Clamp01(Mathf.Cos(visibleLight.spotAngle * Mathf.Deg2Rad * 0.5f));
                    var cosSpotInnerHalfAngle = Mathf.Clamp01(Mathf.Cos(visibleLight.spotAngle * Mathf.Deg2Rad * 0.5f * innerConePercent)); // inner cone
                    angleScale = 1f / Mathf.Max(1e-4f, cosSpotInnerHalfAngle - cosSpotOuterHalfAngle);
                    angleOffset = -cosSpotOuterHalfAngle * angleScale;
                }

                // Store squaredShapeRadius in size
                size.x = shapeRadius * shapeRadius;
                size.y = 0f;
            }
        }
        else if (visibleLight.lightType == LightType.Point)
        {
            // Store squaredShapeRadius in size
            size.x = shapeRadius * shapeRadius;
            size.y = 0f;
        }

        return new LightData
        (
            visibleLight.localToWorldMatrix.Position() - camera.transform.position,
            range,
            (Vector4)visibleLight.finalColor,
            (uint)GetLightType(visibleLight),
            right,
            angleScale,
            up,
            angleOffset,
            visibleLight.localToWorldMatrix.Forward(),
            (uint)shadowIndex,
            size,
            1 + far / (near - far),
            -(near * far) / (near - far)
        );
    }

    private int DivRoundUp(int x, int y) => (x + y - 1) / y;

    private void ClusteredLIghting(ScriptableRenderContext context, Camera camera)
    {
        var clusterWidth = DivRoundUp(resolution, tileSize);
        var clusterHeight = DivRoundUp(resolution, tileSize);
        var clusterCount = clusterWidth * clusterHeight * clusterDepth;

        GraphicsUtilities.SafeExpand(ref lightList, clusterCount * maxLightsPerTile, sizeof(int), ComputeBufferType.Default);

        var descriptor = new RenderTextureDescriptor(clusterWidth, clusterHeight, RenderTextureFormat.RGInt)
        {
            dimension = TextureDimension.Tex3D,
            enableRandomWrite = true,
            volumeDepth = clusterDepth
        };

        var computeShader = Resources.Load<ComputeShader>("ClusteredLightCulling");

        using var scope = context.ScopedCommandBuffer("Clustered Light Culling", true, true);
        scope.Command.GetTemporaryRT(lightClusterId, descriptor);
        scope.Command.SetBufferData(counterBuffer, zeroArray);
        scope.Command.SetComputeBufferParam(computeShader, 0, "_LightData", lightDataBuffer);
        scope.Command.SetComputeBufferParam(computeShader, 0, "_LightCounter", counterBuffer);
        scope.Command.SetComputeBufferParam(computeShader, 0, "_LightClusterListWrite", lightList);
        scope.Command.SetComputeTextureParam(computeShader, 0, "_LightClusterIndicesWrite", lightClusterId);
        scope.Command.SetComputeIntParam(computeShader, "_LightCount", lightDataBuffer.Count);
        scope.Command.SetComputeIntParam(computeShader, "_TileSize", tileSize);
        scope.Command.SetComputeFloatParam(computeShader, "_RcpClusterDepth", 1f / clusterDepth);
        scope.Command.DispatchNormalized(computeShader, 0, clusterWidth, clusterHeight, clusterDepth);
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

    // Todo duplicated from setuplightingnode
    private int GetLightType(VisibleLight visibleLight)
    {
        var hasAdditionalData = visibleLight.light.TryGetComponent<AdditionalLightData>(out var additionalLightData);

        switch (visibleLight.lightType)
        {
            case LightType.Directional:
                return 0;
            case LightType.Point:
                return 1;
            case LightType.Spot:
                if (hasAdditionalData)
                {
                    if (additionalLightData.AreaLightType == AreaLightType.Area)
                        return 6;

                    if (additionalLightData.AreaLightType == AreaLightType.Tube)
                        return 5;
                }

                switch (visibleLight.light.shape)
                {
                    case LightShape.Cone:
                        return 2;
                    case LightShape.Pyramid:
                        return 3;
                    case LightShape.Box:
                        return 4;
                    default:
                        return -1;
                }
            default:
                return -1;
        }
    }
}
