using System;
using NodeGraph;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/Ambient Occlusion")]
public partial class AmbientOcclusionNode : RenderPipelineNode
{
    private static readonly IndexedString blueNoiseIds = new("STBN/Vec2/stbn_vec2_2Dx1D_128x128x64_");
    private static readonly IndexedString blueNoise1Ids = new("STBN/Scalar/stbn_scalar_2Dx1Dx1D_128x128x64x1_");

    [SerializeField, Input] private bool isEnabled = true;

    [Header("Appareance")]
    [Input, SerializeField] private bool debugNoise;
    [Input, SerializeField, Range(1f, 32f)] private float worldRadius = 5f;
    [Input, SerializeField, Range(0f, 8f)] private float strength = 1.5f;
    [SerializeField, Range(0f, 1f)] private float falloff = 0.75f;
    [SerializeField, Range(0f, 0.2f)] private float thinOccluderCompensation = 0.05f;
    [Input, SerializeField, Range(0f, 1f)] private float maxScreenRadius = 0.125f;
    [SerializeField, Range(1e-3f, 5f)] private float sampleDistributionPower = 2f;
    [SerializeField, Range(0f, 30f)] private float depthMipSamplingOffset = 3.3f;
    [SerializeField, Range(1, 16)] private int directionCount = 1;
    [SerializeField, Range(1, 16)] private int sampleCount = 4;

    [Header("Temporal Denoising")]
    [SerializeField, Min(1e-3f)] private float depthWeight = 64f;
    [SerializeField, Min(1e-3f)] private float depthRejection = 0.5f;
    [SerializeField] private float velocityRejection = 0.5f;
    [SerializeField] private float clampVelocityWeight = 8f;
    [SerializeField] private float clampWindowScale = 0.5f;
    [SerializeField, Range(1, 64)] private int frameCount = 20;

    [Header("Spatial Denoising")]
    [Input, SerializeField, Range(1, 32)] private int blurSamples = 8;
    [Input, SerializeField] private float distanceWeight = 1f;
    [Input, SerializeField] private float normalWeight = 1f;
    [Input, SerializeField] private float tangentWeight = 1f;

    [Input] private RenderTargetIdentifier gbuffer1;
    [Input] private RenderTargetIdentifier depth;
    [Input] private RenderTargetIdentifier motionVectors;
    [Input] private RenderTargetIdentifier visibilityCone;

    [InputNoUpdate] private RenderTargetIdentifier previousDepth;

    [Input, Output] private NodeConnection connection;

    private CameraTextureCache aoCache;
    private CameraTextureCache frameCountCache;

    private static readonly int aoTempId0 = Shader.PropertyToID("_AoTemp0"), aoTempId1 = Shader.PropertyToID("_AoTemp1");

    private int computeKernel, generateMipsKernel, temporalKernel, spatialKernel, combineKernel;
    private ComputeShader computeShader;

    public override void Initialize()
    {
        computeShader = Resources.Load<ComputeShader>("Post Processing/AmbientOcclusion");
        computeKernel = computeShader.FindKernel("Compute");
        temporalKernel = computeShader.FindKernel("Temporal");
        generateMipsKernel = computeShader.FindKernel("GenerateMips");
        spatialKernel = computeShader.FindKernel("Spatial");
        combineKernel = computeShader.FindKernel("Combine");

        aoCache = new("Ambient Occlusion");
        frameCountCache = new("Ambient Occlusion FrameCount");
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        if (!isEnabled)
            return;

        var desc0 = new RenderTextureDescriptor(camera.pixelWidth >> 1, camera.pixelHeight >> 1, GraphicsFormat.R8G8B8A8_UNorm, 0)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            mipCount = 5,
            useMipMap = true,
        };

        using var scope = context.ScopedCommandBuffer("Ambient Occlusion", true);
        scope.Command.SetRenderTarget(BuiltinRenderTextureType.None);

        scope.Command.GetTemporaryRT(aoTempId0, desc0);
        ComputeAO(scope.Command, camera, aoTempId0);

        // Generate mips
        //scope.Command.SetComputeTextureParam(computeShader, generateMipsKernel, "_Result0", aoTempId0, 0);
        //scope.Command.SetComputeTextureParam(computeShader, generateMipsKernel, "_Result1", aoTempId0, 1);
        //scope.Command.SetComputeTextureParam(computeShader, generateMipsKernel, "_Result2", aoTempId0, 2);
        //scope.Command.SetComputeTextureParam(computeShader, generateMipsKernel, "_Result3", aoTempId0, 3);
        //scope.Command.SetComputeTextureParam(computeShader, generateMipsKernel, "_Result4", aoTempId0, 4);
        scope.Command.SetComputeIntParam(computeShader, "_Width", camera.pixelWidth >> 1);
        scope.Command.SetComputeIntParam(computeShader, "_Height", camera.pixelHeight >> 1);

        //var threadsX = ((camera.pixelWidth >> 1) + 15 - 1) / 15;
        //var threadsY = ((camera.pixelHeight >> 1) + 15 - 1) / 15;

        //using (var profilerScope = scope.Command.ProfilerScope("Generate Mips"))
        //    scope.Command.DispatchCompute(computeShader, generateMipsKernel, threadsX, threadsY, 1);

        scope.Command.GenerateMips(aoTempId0);

        var desc1 = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, 0) { enableRandomWrite = true };
        aoCache.GetTexture(camera, desc1, out var current, out var previous, FrameCount);

        var frameCountDesc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8_UNorm, 0) { enableRandomWrite = true };
        frameCountCache.GetTexture(camera, frameCountDesc, out var currentFrameCount, out var previousFrameCount, FrameCount);

        var blueNoise1D = Resources.Load<Texture2D>(blueNoise1Ids.GetString(debugNoise ? 0 : FrameCount % 64));

        // Temporal kernel
        scope.Command.GetTemporaryRT(aoTempId1, desc1);
        scope.Command.SetComputeTextureParam(computeShader, temporalKernel, "_Input", aoTempId0);
        scope.Command.SetComputeTextureParam(computeShader, temporalKernel, "_Result", aoTempId1);
        scope.Command.SetComputeTextureParam(computeShader, temporalKernel, "_Depth", depth);
        scope.Command.SetComputeTextureParam(computeShader, temporalKernel, "_PreviousDepth", previousDepth);
        scope.Command.SetComputeTextureParam(computeShader, temporalKernel, "_Motion", motionVectors);
        scope.Command.SetComputeTextureParam(computeShader, temporalKernel, "_History", previous);

        scope.Command.SetComputeTextureParam(computeShader, temporalKernel, "_FrameCountResult", currentFrameCount);
        scope.Command.SetComputeTextureParam(computeShader, temporalKernel, "_FrameCountPrevious", previousFrameCount);
        scope.Command.SetComputeTextureParam(computeShader, temporalKernel, "_History", previous);
        scope.Command.SetComputeTextureParam(computeShader, temporalKernel, "_GBuffer1", gbuffer1);

        scope.Command.SetComputeFloatParam(computeShader, "_ClampWindowScale", clampWindowScale);
        scope.Command.SetComputeFloatParam(computeShader, "_DepthWeight", depthWeight);
        scope.Command.SetComputeFloatParam(computeShader, "_DepthRejection", depthRejection);
        scope.Command.SetComputeFloatParam(computeShader, "_VelocityRejection", velocityRejection);
        scope.Command.SetComputeFloatParam(computeShader, "_ClampVelocityWeight", clampVelocityWeight);
        scope.Command.SetComputeFloatParam(computeShader, "_AccumFrameCount", frameCount);

        using (var profilerScope = scope.Command.ProfilerScope("Temporal"))
            scope.Command.DispatchNormalized(computeShader, temporalKernel, camera.pixelWidth, camera.pixelHeight, 1);

        // Spatial kernel
        scope.Command.SetComputeTextureParam(computeShader, spatialKernel, "_Input", aoTempId1);
        scope.Command.SetComputeTextureParam(computeShader, spatialKernel, "_Result", current);
        scope.Command.SetComputeTextureParam(computeShader, spatialKernel, "_GBuffer1", gbuffer1);
        scope.Command.SetComputeTextureParam(computeShader, spatialKernel, "_BlueNoise1D", blueNoise1D);
        scope.Command.SetComputeTextureParam(computeShader, spatialKernel, "_FrameCount", currentFrameCount);
        scope.Command.SetComputeTextureParam(computeShader, spatialKernel, "_Depth", depth);

        scope.Command.SetComputeFloatParam(computeShader, "_DistanceWeight", distanceWeight);
        scope.Command.SetComputeFloatParam(computeShader, "_NormalWeight", normalWeight);
        scope.Command.SetComputeFloatParam(computeShader, "_TangentWeight", tangentWeight);
        scope.Command.SetComputeIntParam(computeShader, "_BlurSamples", blurSamples);
        scope.Command.SetComputeFloatParam(computeShader, "_WorldRadius", worldRadius);

        using (var profilerScope = scope.Command.ProfilerScope("Spatial"))
            scope.Command.DispatchNormalized(computeShader, spatialKernel, camera.pixelWidth, camera.pixelHeight, 1);

        scope.Command.ReleaseTemporaryRT(aoTempId1);

        // Combine
        scope.Command.SetComputeTextureParam(computeShader, combineKernel, "_Input", current);
        scope.Command.SetComputeTextureParam(computeShader, combineKernel, "_VisibilityCone", visibilityCone);

        using (var profilerScope = scope.Command.ProfilerScope("Combine"))
            scope.Command.DispatchNormalized(computeShader, combineKernel, camera.pixelWidth, camera.pixelHeight, 1);
    }

    private void ComputeAO(CommandBuffer command, Camera camera, RenderTargetIdentifier result)
    {
        var blueNoise2D = Resources.Load<Texture2D>(blueNoiseIds.GetString(debugNoise ? 0 : FrameCount % 64));

        var tanHalfFovY = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        var tanHalfFovX = tanHalfFovY * camera.aspect;
        command.SetComputeVectorParam(computeShader, "_UvToView", new Vector4(tanHalfFovX * 2f, tanHalfFovY * 2f, -tanHalfFovX, -tanHalfFovY));

        command.SetComputeFloatParam(computeShader, "_Radius", worldRadius * camera.pixelHeight / tanHalfFovY * 0.5f);
        command.SetComputeFloatParam(computeShader, "_FalloffScale", falloff == 1f ? 0f : 1f / (worldRadius * falloff - worldRadius));
        command.SetComputeFloatParam(computeShader, "_FalloffBias", falloff == 1f ? 1f : 1f / (1f - falloff));

        command.SetComputeFloatParam(computeShader, "_SampleDistributionPower", sampleDistributionPower);
        command.SetComputeFloatParam(computeShader, "_ThinOccluderCompensation", thinOccluderCompensation);
        command.SetComputeFloatParam(computeShader, "_Strength", strength);
        command.SetComputeFloatParam(computeShader, "_DepthMipSamplingOffset", depthMipSamplingOffset);

        command.SetComputeFloatParam(computeShader, "_SampleCount", sampleCount);
        command.SetComputeFloatParam(computeShader, "_DirectionCount", directionCount);
        command.SetComputeFloatParam(computeShader, "_MaxScreenRadius", maxScreenRadius * camera.pixelHeight);
        command.SetComputeFloatParam(computeShader, "_MaxMips", Texture2DExtensions.MipCount(camera.pixelWidth, camera.pixelHeight) - 1);

        command.SetComputeIntParam(computeShader, "_MaxWidth", camera.pixelWidth - 1);
        command.SetComputeIntParam(computeShader, "_MaxHeight", camera.pixelHeight - 1);

        command.SetComputeTextureParam(computeShader, computeKernel, "_BlueNoise2D", blueNoise2D);
        command.SetComputeTextureParam(computeShader, computeKernel, "_Result", result);
        command.SetComputeTextureParam(computeShader, computeKernel, "_GBuffer1", gbuffer1);
        command.SetComputeTextureParam(computeShader, computeKernel, "_Depth", depth);

        using var profilerScope = command.ProfilerScope("Compute");
        command.DispatchNormalized(computeShader, computeKernel, camera.pixelWidth >> 1, camera.pixelHeight >> 1, 1);
    }

    public override void Cleanup()
    {
        aoCache.Dispose();
        frameCountCache.Dispose();
    }
}