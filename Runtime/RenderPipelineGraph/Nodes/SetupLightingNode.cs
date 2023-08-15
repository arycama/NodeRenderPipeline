using System;
using System.Collections.Generic;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Setup Lighting")]
public partial class SetupLightingNode : RenderPipelineNode
{
    private static readonly Plane[] frustumPlanes = new Plane[6];
    private static readonly IndexedString cascadeIds = new("Cascade ");

    private int directionalShadowsId, pointShadowsId, spotlightShadowsId, areaShadowsId;

    [Header("Directional")]
    [SerializeField, Input, Output, Pow2(4096), Tooltip("Resolution of each shadowmap cascade")] private int directionalResolution = 2048;
    [SerializeField, Input, Output, Tooltip("Higher values reduce self-shadowing, but can result in peter-panning")] private float directionalBias = 1f;
    [SerializeField, Tooltip("Depth of shadowmap, 16 is faster but less precise")] private DepthBits directionalDepth = DepthBits.Depth16;
    [SerializeField, Input, Output, Range(1, 8), Tooltip("Number of cascades, more cascades distributes resolution more evenly, but is more expensive to render and sample")] private int directionalCascades = 4;
    [SerializeField, Input, Output, Range(0f, 1f)] private float directionalCascadeFade = 0.1f;
    [SerializeField, Input, Output, Range(0f, 10f)] private float directionalShadowNearPlane = 0.1f;

    [SerializeField, Range(0f, 1f)] private float cascadeDistribution = 0.5f;
    [SerializeField, Range(1, 16)] private int pcfSamples = 4;
    [SerializeField] private float pcfRadius = 0.001f;

    [Header("Point"), SerializeField, Pow2(1024)] private int pointResolution = 256;
    [SerializeField] private float pointBias = 1f;
    [SerializeField] private DepthBits pointDepth = DepthBits.Depth16;

    [Header("Spot"), SerializeField, Pow2(2048)] private int spotResolution = 512;
    [SerializeField, Range(0, 4)] private float spotBias = 1f;
    [SerializeField] private DepthBits spotDepth = DepthBits.Depth16;

    [Header("Area"), SerializeField, Pow2(2048)] private int areaResolution = 512;
    [SerializeField, Min(0f)] private float areaBias = 1f;
    [SerializeField] private DepthBits areaDepth = DepthBits.Depth16;
    //[SerializeField, Min(0f)] private float blurSigma = 1f;

    [SerializeField] private RenderPipelineSubGraph shadowsSubGraph;

    [Input] private CullingResults cullingResults;
    [Input] private float shadowDistance;
    [Input] private GpuInstanceBuffers gpuInstanceBuffers;

    [Output] private SmartComputeBuffer<DirectionalLightData> directionalLightDataBuffer;
    [Output] private SmartComputeBuffer<LightData> lightDataBuffer;
    [Output] private SmartComputeBuffer<Matrix4x4> spotlightShadowMatricesBuffer;
    [Output] private SmartComputeBuffer<Matrix4x4> areaShadowMatricesBuffer;
    [Output] private SmartComputeBuffer<Matrix3x4> directionalShadowMatrices;

    [Output] private RenderTargetIdentifier directionalShadows;
    [Input, Output] private NodeConnection connection;

    public override void Initialize()
    {
        if (shadowsSubGraph != null)
            shadowsSubGraph.Initialize();

        directionalLightDataBuffer = new();
        lightDataBuffer = new();
        spotlightShadowMatricesBuffer = new();
        areaShadowMatricesBuffer = new();
        directionalShadowMatrices = new();

        directionalShadowsId = GetShaderPropertyId("_DirectionalShadows");
        pointShadowsId = GetShaderPropertyId("_PointShadows");
        spotlightShadowsId = GetShaderPropertyId("_SpotlightShadows");
        areaShadowsId = GetShaderPropertyId("_AreaShadows");

        directionalShadows = directionalShadowsId;
    }

    public override void Cleanup()
    {
        if (shadowsSubGraph != null)
            shadowsSubGraph.Cleanup();
    }

    public override void NodeChanged()
    {
        if (shadowsSubGraph != null)
            shadowsSubGraph.Cleanup();

        if (shadowsSubGraph != null)
            shadowsSubGraph.Initialize();
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var directionalShadowRequests = ScopedPooledList<DirectionalShadowRequestData>.Get();
        using var pointShadowRequests = ScopedPooledList<PointLightShadowRequestData>.Get();
        using var spotlightShadowRequests = ScopedPooledList<SpotShadowRequestData>.Get();
        using var areaShadowRequests = ScopedPooledList<SpotShadowRequestData>.Get();
        using var directionalLightDatas = ScopedPooledList<DirectionalLightData>.Get();
        using var lightDatas = ScopedPooledList<LightData>.Get();

        for (var i = 0; i < cullingResults.visibleLights.Length; i++)
        {
            var visibleLight = cullingResults.visibleLights[i];

            if (visibleLight.lightType == LightType.Directional)
            {
                var directionalLightData = SetupDirectionalLight(visibleLight, i, cullingResults, camera, directionalShadowRequests);
                directionalLightDatas.Value.Add(directionalLightData);
            }
            // TODO: Implement some kind of processor so we can swap out any unsupported lights.. or just replace the UnityEngine.Light component entirely
            else if (GetLightType(visibleLight) != -1)
            {
                var lightData = SetupLight(visibleLight, i, cullingResults, spotlightShadowRequests, pointShadowRequests, areaShadowRequests, camera);
                lightDatas.Value.Add(lightData);
            }
        }

        RenderDirectionalShadows(context, camera, directionalShadowRequests);

        using var scope = context.ScopedCommandBuffer("Setup Lighting", true);

        // Directional lights
        scope.Command.SetBufferData(directionalLightDataBuffer, directionalLightDatas);

        RenderPointShadows(context, pointShadowRequests, camera);
        RenderSpotShadows(context, spotlightShadowRequests, spotlightShadowsId, spotlightShadowMatricesBuffer, (int)spotDepth, spotResolution, spotBias, camera, "_SpotlightShadows", "_SpotlightShadowMatrices");
        RenderSpotShadows(context, areaShadowRequests, areaShadowsId, areaShadowMatricesBuffer, (int)areaDepth, areaResolution, areaBias, camera, "_AreaShadows", "_AreaShadowMatrices");

        // Point/spot lights
        scope.Command.SetBufferData(lightDataBuffer, lightDatas);
    }

    public override void FrameRenderComplete()
    {
        if (shadowsSubGraph != null)
            shadowsSubGraph.FrameRenderComplete();
    }

    private DirectionalLightData SetupDirectionalLight(VisibleLight visibleLight, int index, CullingResults cullingResults, Camera camera, List<DirectionalShadowRequestData> directionalShadowRequests)
    {
        var angularDiameter = visibleLight.light.TryGetComponent<CelestialBody>(out var celestialBody)
            ? celestialBody.AngularDiameter
            : 0.53f;

        var shadowIndex = -1;
        if (visibleLight.light.shadows != LightShadows.None)
        {
            var isValid = false;
            var shadowRequestData = new DirectionalShadowRequestData(index);

            var lightToWorld = visibleLight.localToWorldMatrix.NoTranslation();
            var worldToLight = lightToWorld.inverse;

            var projMatrix = camera.projectionMatrix;
            camera.ResetProjectionMatrix();

            for (var i = 0; i < directionalCascades; i++)
            {
                // Calculate logarithmic split ratios
                var cascadeStart = i == 0 ? camera.nearClipPlane : directionalShadowNearPlane * Mathf.Pow(shadowDistance / directionalShadowNearPlane, (float)i / directionalCascades);
                var cascadeEnd = directionalShadowNearPlane * Mathf.Pow(shadowDistance / directionalShadowNearPlane, (float)(i + 1) / directionalCascades);

                // To fade between cascades, slightly decrease the start distance of subsequent cascades
                if (i > 0)
                {
                    var previousEnd = directionalShadowNearPlane * Mathf.Pow(shadowDistance / directionalShadowNearPlane, (float)(i - 1) / directionalCascades);
                    cascadeStart = Mathf.Lerp(cascadeStart, previousEnd, directionalCascadeFade);
                }

                // Transform camera bounds to light space
                Vector3 minValue = Vector3.positiveInfinity, maxValue = Vector3.negativeInfinity;
                for (var z = 0; z < 2; z++)
                {
                    for (var y = 0; y < 2; y++)
                    {
                        for (var x = 0; x < 2; x++)
                        {
                            var far = z == 0 ? cascadeStart : cascadeEnd;
                            var worldPoint = camera.ViewportToWorldPoint(new(x, y, far));
                            var localPoint = worldToLight.MultiplyPoint3x4(worldPoint);
                            minValue = Vector3.Min(minValue, localPoint);
                            maxValue = Vector3.Max(maxValue, localPoint);
                        }
                    }
                }

                // Snap to texels to avoid shimmering
                var worldUnitsPerTexel = (maxValue - minValue).XY() / directionalResolution;
                var viewBoundsMin = new Vector3(Mathf.Floor(minValue.x / worldUnitsPerTexel.x) * worldUnitsPerTexel.x, Mathf.Floor(minValue.y / worldUnitsPerTexel.y) * worldUnitsPerTexel.y, minValue.z);
                var viewBoundsMax = new Vector3(Mathf.Floor(maxValue.x / worldUnitsPerTexel.x) * worldUnitsPerTexel.x, Mathf.Floor(maxValue.y / worldUnitsPerTexel.y) * worldUnitsPerTexel.y, maxValue.z);

                var viewCenter = 0.5f * (viewBoundsMax + viewBoundsMin);
                var viewSize = viewBoundsMax - viewBoundsMin;
                var viewExtents = 0.5f * viewSize;

                var localView = new Vector3(viewCenter.x, viewCenter.y, viewCenter.z - viewExtents.z);
                var viewPos = lightToWorld.MultiplyPoint3x4(localView);
                var viewMatrix = Matrix4x4.TRS(viewPos, lightToWorld.rotation, Vector3.one).inverse;
                var projectionMatrix = Matrix4x4.Ortho(-viewExtents.x, viewExtents.x, -viewExtents.y, viewExtents.y, 0f, viewSize.z);
                projectionMatrix.SetColumn(2, -projectionMatrix.GetColumn(2));

                // Calculate culling planes
                using var cullingPlanes = ScopedPooledList<Plane>.Get();

                // First get the planes from the view projection matrix
                var viewProjectionMatrix = projectionMatrix * viewMatrix;
                GeometryUtility.CalculateFrustumPlanes(viewProjectionMatrix, frustumPlanes);
                for (var j = 0; j < 6; j++)
                {
                    // Skip near plane
                    if (j != 4)
                        cullingPlanes.Value.Add(frustumPlanes[j]);
                }

                // Now also add any main camera-frustum planes that are not facing away from the light
                var lightDirection = -visibleLight.localToWorldMatrix.Forward();
                GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
                for (var j = 0; j < 6; j++)
                {
                    var plane = frustumPlanes[j];
                    if (Vector3.Dot(plane.normal, lightDirection) > 0.0f)
                        cullingPlanes.Value.Add(plane);
                }

                var shadowSplitData = new ShadowSplitData()
                {
                    cullingPlaneCount = cullingPlanes.Value.Count,
                    shadowCascadeBlendCullingFactor = 1
                };

                for (var j = 0; j < cullingPlanes.Value.Count; j++)
                {
                    shadowSplitData.SetCullingPlane(j, cullingPlanes.Value[j]);
                }

                // Do final matrix in RWS for rendering
                var viewMatrixRWS = Matrix4x4Extensions.WorldToLocal(viewPos - camera.transform.position, lightToWorld.rotation);

                // GetShadowCasterBounds may return false if no Unity meshes should cast shadows, but we may have custom meshes (Eg terrain, GPU-driven rendering) that should cast shadows
                var hasShadows = cullingResults.GetShadowCasterBounds(index, out var casterBounds);

                shadowRequestData[i] = new ShadowRequestData(viewMatrixRWS, projectionMatrix, shadowSplitData, 0f, viewSize.z, hasShadows);
                isValid = true;
            }

            camera.projectionMatrix = projMatrix;

            if (isValid)
            {
                shadowIndex = directionalShadowRequests.Count;
                directionalShadowRequests.Add(shadowRequestData);
            }
        }

        var direction = -visibleLight.localToWorldMatrix.GetColumn(2);
        return new DirectionalLightData((Vector4)visibleLight.finalColor, angularDiameter, direction, shadowIndex);
    }

    private LightData SetupLight(VisibleLight visibleLight, int index, CullingResults cullingResults, List<SpotShadowRequestData> spotShadowRequests, List<PointLightShadowRequestData> pointShadowRequests, List<SpotShadowRequestData> areaShadowRequests, Camera camera)
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
        if (light.shadows != LightShadows.None)
        {
            var hasBuiltinShadows = cullingResults.GetShadowCasterBounds(index, out var shadowBounds);

            if (visibleLight.lightType == LightType.Point)
            {
                EnqueuePointLightShadow(ref visibleLight, index, ref cullingResults, pointShadowRequests, camera, ref near, ref far, ref shadowIndex, shadowBounds, hasBuiltinShadows);
            }

            // Spotlight/area Shadow casting
            if (visibleLight.lightType == LightType.Spot)
            {
                if (cullingResults.ComputeSpotShadowMatricesAndCullingPrimitives(index, out var viewMatrix, out var projectionMatrix, out var shadowSplitData))
                {
                    // Convert to camera relative
                    var viewMatrix2 = Matrix4x4.TRS(light.transform.position - camera.transform.position, light.transform.rotation, new Vector3(1f, 1f, -1f)).inverse;

                    viewMatrix.SetColumn(3, viewMatrix2.GetColumn(3));

                    if (isAreaLight)
                    {
                        shadowIndex = areaShadowRequests.Count;
                        areaShadowRequests.Add(new SpotShadowRequestData(index, shadowBounds, new ShadowRequestData(viewMatrix, projectionMatrix, shadowSplitData, light.shadowNearPlane, light.range, hasBuiltinShadows)));
                    }
                    else
                    {

                        // Box lights need an ortho matrix
                        if (light.shape == LightShape.Box)
                            projectionMatrix = Matrix4x4.Ortho(-size.x * 0.5f, size.x * 0.5f, -size.y * 0.5f, size.y * 0.5f, near, far);

                        shadowIndex = spotShadowRequests.Count;
                        spotShadowRequests.Add(new SpotShadowRequestData(index, shadowBounds, new ShadowRequestData(viewMatrix, projectionMatrix, shadowSplitData, near, far, hasBuiltinShadows)));
                    }

                    near = projectionMatrix.Near();
                    far = projectionMatrix.Far();
                }
            }
        }

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

    private static void EnqueuePointLightShadow(ref VisibleLight visibleLight, int index, ref CullingResults cullingResults, List<PointLightShadowRequestData> pointShadowRequests, Camera camera, ref float near, ref float far, ref int shadowIndex, Bounds shadowBounds, bool hasBuiltinShadows)
    {
        var isValid = false;
        var shadowRequestData = new PointLightShadowRequestData(index, shadowBounds);

        for (var i = 0; i < 6; i++)
        {
            if (!cullingResults.ComputePointShadowMatricesAndCullingPrimitives(index, (CubemapFace)i, 0, out var viewMatrix, out var projectionMatrix, out var splitData))
                continue;

            // To undo unity's builtin inverted culling for point shadows, flip the y axis.
            // Y also needs to be done in the shader
            viewMatrix.SetRow(1, -viewMatrix.GetRow(1));

            // Convert to camera relative
            // TODO: There is probably a faster/more direct way of doing this
            var rotation = Quaternion.LookRotation(CoreUtils.lookAtList[i], CoreUtils.upVectorList[i]);
            var viewMatrix2 = Matrix4x4.TRS(visibleLight.light.transform.position - camera.transform.position, rotation, new Vector3(1f, 1f, -1f)).inverse;

            viewMatrix.SetColumn(3, viewMatrix2.GetColumn(3));

            near = projectionMatrix.Near();
            far = projectionMatrix.Far();

            shadowRequestData[i] = new ShadowRequestData(viewMatrix, projectionMatrix, splitData, near, far, hasBuiltinShadows);
            isValid = true;
        }

        if (isValid)
        {
            shadowIndex = pointShadowRequests.Count;
            pointShadowRequests.Add(shadowRequestData);
        }
    }

    private void RenderShadowSubGraph(ScriptableRenderContext context, Camera camera, int visibleLightIndex, ShadowRequestData shadowRequestData, RenderTargetIdentifier output, int resolution, Vector3 viewPosition)
    {
        if (shadowsSubGraph == null)
            return;

        // Extract culling planes
        var shadowSplitData = shadowRequestData.ShadowSplitData;
        var cullingPlanes = new CullingPlanes { Count = shadowSplitData.cullingPlaneCount };
        for (var i = 0; i < shadowSplitData.cullingPlaneCount; i++)
        {
            // Translate planes from world space to camera-relative space
            var plane = shadowSplitData.GetCullingPlane(i);
            plane.distance += Vector3.Dot(plane.normal, viewPosition);
            cullingPlanes.SetCullingPlane(i, plane);
        }

        shadowsSubGraph.AddRelayInput("CullingResults", cullingResults);
        shadowsSubGraph.AddRelayInput("VisibleLightIndex", visibleLightIndex);
        shadowsSubGraph.AddRelayInput("ShadowSplitData", shadowRequestData.ShadowSplitData);
        shadowsSubGraph.AddRelayInput("RenderShadowCasters", shadowRequestData.RenderShadowCasters);
        shadowsSubGraph.AddRelayInput("CullingPlanes", cullingPlanes);
        shadowsSubGraph.AddRelayInput("CullingPlanesCount", cullingPlanes.Count);
        shadowsSubGraph.AddRelayInput("GpuInstanceBuffers", gpuInstanceBuffers);

        // Matrices
        shadowsSubGraph.AddRelayInput("ViewProjMatrix", GL.GetGPUProjectionMatrix(shadowRequestData.ProjectionMatrix, true) * shadowRequestData.ViewMatrix);
        shadowsSubGraph.AddRelayInput("ViewMatrix", shadowRequestData.ViewMatrix);
        shadowsSubGraph.AddRelayInput("InvViewMatrix", shadowRequestData.ViewMatrix.inverse);

        shadowsSubGraph.AddRelayInput("Output", output);

        shadowsSubGraph.AddRelayInput("Resolution", resolution);

        shadowsSubGraph.Render(context, camera, FrameCount);
    }

    private void RenderDirectionalShadows(ScriptableRenderContext context, Camera camera, List<DirectionalShadowRequestData> requests)
    {
        var shadowMatrices = ListPool<Matrix3x4>.Get();

        using var scope = context.ScopedCommandBuffer("Render Directional Shadows", true);

        // If no shadows, just allocate a small empty shadowmap and return
        if (requests.Count == 0)
        {
            var descriptor = new RenderTextureDescriptor(1, 1, RenderTextureFormat.Shadowmap, (int)directionalDepth)
            { dimension = TextureDimension.Tex2DArray, volumeDepth = 1 };
            scope.Command.GetTemporaryRT(directionalShadowsId, descriptor);
            scope.Command.SetRenderTarget(directionalShadowsId, 0, CubemapFace.Unknown, RenderTargetIdentifier.AllDepthSlices);
            scope.Command.ClearRenderTarget(true, false, new Color(), 1f);
        }
        else
        {
            var descriptor = new RenderTextureDescriptor(directionalResolution, directionalResolution, RenderTextureFormat.Shadowmap, (int)directionalDepth)
            { dimension = TextureDimension.Tex2DArray, volumeDepth = requests.Count * directionalCascades };
            scope.Command.GetTemporaryRT(directionalShadowsId, descriptor);

            // Clear all depth slices at once
            scope.Command.SetGlobalFloat("_ZClip", 0);

            for (var i = 0; i < requests.Count; i++)
            {
                var directionalShadowRequestData = requests[i];
                var visibleLightIndex = directionalShadowRequestData.VisibleLightIndex;
                var visibleLight = cullingResults.visibleLights[visibleLightIndex];
                scope.Command.SetGlobalDepthBias(directionalBias, visibleLight.light.shadowBias);
                context.ExecuteCommandBuffer(scope.Command);
                scope.Command.Clear();

                for (var j = 0; j < directionalCascades; j++)
                {
                    scope.Command.BeginSample(cascadeIds.GetString(j));
                    context.ExecuteCommandBuffer(scope.Command);
                    scope.Command.Clear();

                    var cascadeIndex = i * directionalCascades + j;

                    // Shadow split has non-relative planes, as it uses unity's builtin culling. So re-extract the camera-relative planes
                    var shadowRequestData = directionalShadowRequestData[j];

                    // Copy shadow split data to our own culling planes struct
                    var output = new RenderTargetIdentifier(directionalShadowsId, 0, CubemapFace.Unknown, cascadeIndex);

                    RenderShadowSubGraph(context, camera, visibleLightIndex, shadowRequestData, output, directionalResolution, camera.transform.position);
                    shadowMatrices.Add((shadowRequestData.ProjectionMatrix * shadowRequestData.ViewMatrix).ConvertToAtlasMatrix());

                    scope.Command.EndSample(cascadeIds.GetString(j));
                    context.ExecuteCommandBuffer(scope.Command);
                    scope.Command.Clear();
                }
            }

            scope.Command.SetGlobalDepthBias(0f, 0f);
        }

        scope.Command.SetGlobalFloat("_ZClip", 1);

        // Set cascade matrices
        scope.Command.SetBufferData(directionalShadowMatrices, shadowMatrices);
        ListPool<Matrix3x4>.Release(shadowMatrices);

        var log2e = Mathf.Log(Mathf.Exp(1f), 2f);

        // Other data
        scope.Command.SetGlobalInt("_PcfSamples", pcfSamples);
        scope.Command.SetGlobalFloat("_ShadowPcfRadius", pcfRadius);
        scope.Command.SetGlobalFloat("_DirectionalShadowDistance", shadowDistance);
        scope.Command.SetGlobalFloat("_DirectionalShadowCascadeScale", directionalCascades / Mathf.Log(shadowDistance / directionalShadowNearPlane) * (1 / log2e));
        scope.Command.SetGlobalFloat("_DirectionalShadowCascadeBias", -directionalCascades * Mathf.Log(directionalShadowNearPlane) / Mathf.Log(shadowDistance / directionalShadowNearPlane));
        scope.Command.SetGlobalFloat("_DirectionalShadowCascadeFade", directionalCascadeFade);
        scope.Command.SetGlobalFloat("_DirectionalShadowCascadeFadeScale", 1f / directionalCascadeFade);
        scope.Command.SetGlobalFloat("_DirectionalShadowCascadeFadeBias", (directionalCascadeFade - 1f) / directionalCascadeFade);
    }

    private void RenderPointShadows(ScriptableRenderContext context, List<PointLightShadowRequestData> pointShadowRequests, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Render Point Shadows", true);
        using var keywordScope = scope.Command.KeywordScope("PUNCTUAL_LIGHT_SHADOW");

        // If no shadows, just allocate a small empty shadowmap and return
        if (pointShadowRequests.Count == 0)
        {
            var descriptor = new RenderTextureDescriptor(1, 1, RenderTextureFormat.Shadowmap, (int)pointDepth)
            { dimension = TextureDimension.CubeArray, volumeDepth = 6 };
            scope.Command.GetTemporaryRT(pointShadowsId, descriptor);
        }
        else
        {
            var descriptor = new RenderTextureDescriptor(pointResolution, pointResolution, RenderTextureFormat.Shadowmap, (int)pointDepth) { dimension = TextureDimension.CubeArray, volumeDepth = pointShadowRequests.Count * 6 };
            scope.Command.GetTemporaryRT(pointShadowsId, descriptor);

            // Clear all slices at once
            for (var i = 0; i < pointShadowRequests.Count; i++)
            {
                var pointShadowRequestData = pointShadowRequests[i];
                var visibleLightIndex = pointShadowRequestData.VisibleLightIndex;
                var visibleLight = cullingResults.visibleLights[visibleLightIndex];

                using var profilerScope = scope.Command.ProfilerScope("Point Light Shadow");

                // Only need to set this once
                scope.Command.SetGlobalDepthBias(pointBias, visibleLight.light.shadowBias);
                context.ExecuteCommandBuffer(scope.Command);
                scope.Command.Clear();

                for (var j = 0; j < 6; j++)
                {
                    var shadowRequestData = pointShadowRequestData[j];

                    // As shadowRequestData is a struct, some can be set to default value, as it can't be null
                    if (!shadowRequestData.IsValid)
                        continue;

                    // We also need to swap the top/bottom faces of the cubemap
                    var index = j;
                    if (j == 2) index = 3;
                    else if (j == 3) index = 2;

                    var output = new RenderTargetIdentifier(pointShadowsId, 0, CubemapFace.Unknown, i * 6 + index);
                    RenderShadowSubGraph(context, camera, visibleLightIndex, shadowRequestData, output, pointResolution, camera.transform.position);
                }
            }

            scope.Command.SetGlobalDepthBias(0f, 0f);
        }

        // Set the shadowmap array
        scope.Command.SetGlobalTexture("_PointShadows", pointShadowsId);
    }

    private void RenderSpotShadows(ScriptableRenderContext context, List<SpotShadowRequestData> shadowRequests, int propertyId, SmartComputeBuffer<Matrix4x4> computeBuffer, int depth, int resolution, float bias, Camera camera, string texturePropertyName, string matrixPropertyName)
    {
        var shadowMatrices = ListPool<Matrix4x4>.Get();

        using var scope = context.ScopedCommandBuffer("Render Shadows", true);
        using var keywordScope = scope.Command.KeywordScope("PUNCTUAL_LIGHT_SHADOW");

        // If no shadows, just allocate a small empty shadowmap and return
        if (shadowRequests.Count == 0)
        {
            scope.Command.GetTemporaryRTArray(propertyId, 1, 1, 1, depth, FilterMode.Bilinear, RenderTextureFormat.Shadowmap);
        }
        else
        {
            // Evsm is half res
            scope.Command.GetTemporaryRTArray(propertyId, resolution, resolution, shadowRequests.Count, depth, FilterMode.Bilinear, RenderTextureFormat.Shadowmap);

            // Clear all slices
            for (var i = 0; i < shadowRequests.Count; i++)
            {
                var spotShadowRequest = shadowRequests[i];
                var visibleLight = cullingResults.visibleLights[spotShadowRequest.VisibleLightIndex];

                var shadowRequestData = spotShadowRequest.ShadowRequestData;
                scope.Command.SetGlobalDepthBias(bias, visibleLight.light.shadowBias * 2);

                context.ExecuteCommandBuffer(scope.Command);
                scope.Command.Clear();

                var output = new RenderTargetIdentifier(propertyId, 0, CubemapFace.Unknown, i);
                RenderShadowSubGraph(context, camera, spotShadowRequest.VisibleLightIndex, shadowRequestData, output, spotResolution, camera.transform.position);

                // Add to shadow matrices list
                var viewProjectionMatrix = (shadowRequestData.ProjectionMatrix * shadowRequestData.ViewMatrix).ConvertToAtlasMatrix();
                shadowMatrices.Add(viewProjectionMatrix);
            }

            scope.Command.SetGlobalDepthBias(0f, 0f);
        }

        // Set the shadowmap array
        scope.Command.SetGlobalTexture(texturePropertyName, propertyId);

        scope.Command.SetBufferData(computeBuffer, shadowMatrices);
        ListPool<Matrix4x4>.Release(shadowMatrices);
        scope.Command.SetGlobalBuffer(matrixPropertyName, computeBuffer);
    }

    public override void FinishRendering(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer();
        scope.Command.ReleaseTemporaryRT(directionalShadowsId);
        scope.Command.ReleaseTemporaryRT(spotlightShadowsId);
        scope.Command.ReleaseTemporaryRT(pointShadowsId);
    }

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