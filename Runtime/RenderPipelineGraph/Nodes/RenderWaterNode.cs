using System.Collections.Generic;
using NodeGraph;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Water FFT")]
public partial class RenderWaterNode : RenderPipelineNode
{
    [SerializeField, Tooltip("The resolution of the simulation, higher numbers give more detail but are more expensive")] private int resolution = 128;
    [SerializeField, Tooltip("Use Trilinear for the normal/foam map, improves quality of lighting/reflections in shader")] private bool useTrilinear = true;
    [SerializeField, Range(1, 16), Tooltip("Anisotropic level for the normal/foam map")] private int anisoLevel = 4;
    [SerializeField] private Material material;
    [SerializeField] private WaterProfile profile;

    [Input, Output] private NodeConnection connection;

    private RenderTexture normalMap, foamSmoothness, DisplacementMap;

    private readonly Dictionary<Camera, bool> flips = new();
    private RenderTexture lengthToRoughness;
    private static readonly IndexedShaderPropertyId smoothnessMapIds = new("SmoothnessOutput");

    public override void Initialize()
    {
        // Initialize textures
        normalMap = new RenderTexture(resolution, resolution, 0, GraphicsFormat.R8G8_SNorm)
        {
            anisoLevel = anisoLevel,
            autoGenerateMips = false,
            dimension = TextureDimension.Tex2DArray,
            enableRandomWrite = true,
            filterMode = useTrilinear ? FilterMode.Trilinear : FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
            name = "Ocean Normal Map",
            useMipMap = true,
            volumeDepth = 8,
            wrapMode = TextureWrapMode.Repeat,
        }.Created();

        // Initialize textures
        foamSmoothness = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            anisoLevel = anisoLevel,
            autoGenerateMips = false,
            dimension = TextureDimension.Tex2DArray,
            enableRandomWrite = true,
            filterMode = useTrilinear ? FilterMode.Trilinear : FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
            name = "Ocean Normal Map",
            useMipMap = true,
            volumeDepth = 8,
            wrapMode = TextureWrapMode.Repeat,
        }.Created();

        DisplacementMap = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf)
        {
            anisoLevel = anisoLevel,
            autoGenerateMips = false,
            dimension = TextureDimension.Tex2DArray,
            enableRandomWrite = true,
            filterMode = useTrilinear ? FilterMode.Trilinear : FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
            name = "Ocean Displacement",
            useMipMap = true,
            volumeDepth = 8,
            wrapMode = TextureWrapMode.Repeat,
        }.Created();

        lengthToRoughness = new RenderTexture(256, 1, 0, RenderTextureFormat.R16)
        {
            enableRandomWrite = true,
            hideFlags = HideFlags.HideAndDontSave,
            name = "Length to Smoothness",
        }.Created();

        // First pass will shorten normal based on the average normal length from the smoothness
        var computeShader = Resources.Load<ComputeShader>("Utility/SmoothnessFilter");
        var generateLengthToSmoothnessKernel = computeShader.FindKernel("GenerateLengthToSmoothness");
        computeShader.SetFloat("_MaxIterations", 32);
        computeShader.SetFloat("_Resolution", 256);
        computeShader.SetTexture(generateLengthToSmoothnessKernel, "_LengthToRoughnessResult", lengthToRoughness);
        computeShader.DispatchNormalized(generateLengthToSmoothnessKernel, 256, 1, 1);
    }

    public override void Cleanup()
    {
        // Release temporary RTs
        DestroyImmediate(normalMap);
        DestroyImmediate(foamSmoothness);
        DestroyImmediate(DisplacementMap);
        DestroyImmediate(lengthToRoughness);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        if (anisoLevel != DisplacementMap.anisoLevel)
        {
            Cleanup();
            Initialize();
        }

        // Simulate
        var tempBufferID4 = Shader.PropertyToID("TempFFTBuffer4");
        var fftNormalBufferId = Shader.PropertyToID("_FFTNormalBuffer");
        var tempBufferID2 = Shader.PropertyToID("TempFFTBuffer2");

        // Calculate constants
        var rcpScales = new Vector4(1f / Mathf.Pow(profile.CascadeScale, 0f), 1f / Mathf.Pow(profile.CascadeScale, 1f), 1f / Mathf.Pow(profile.CascadeScale, 2f), 1f / Mathf.Pow(profile.CascadeScale, 3f));
        var patchSizes = new Vector4(profile.PatchSize / Mathf.Pow(profile.CascadeScale, 0f), profile.PatchSize / Mathf.Pow(profile.CascadeScale, 1f), profile.PatchSize / Mathf.Pow(profile.CascadeScale, 2f), profile.PatchSize / Mathf.Pow(profile.CascadeScale, 3f));
        var spectrumStart = new Vector4(0, profile.MaxWaveNumber * patchSizes.y / patchSizes.x, profile.MaxWaveNumber * patchSizes.z / patchSizes.y, profile.MaxWaveNumber * patchSizes.w / patchSizes.z);
        var spectrumEnd = new Vector4(profile.MaxWaveNumber, profile.MaxWaveNumber, profile.MaxWaveNumber, resolution);
        var oceanScale = new Vector4(1f / patchSizes.x, 1f / patchSizes.y, 1f / patchSizes.z, 1f / patchSizes.w);
        var rcpTexelSizes = new Vector4(resolution / patchSizes.x, resolution / patchSizes.y, resolution / patchSizes.z, resolution / patchSizes.w);
        var texelSizes = patchSizes / resolution;

        // Load resources
        var computeShader = Resources.Load<ComputeShader>("Ocean FFT");

        if (!flips.TryGetValue(camera, out var flip))
        {
            flips.Add(camera, false);
        }
        else
        {
            flip = !flip;
            flips[camera] = flip;
        }

        // Set Vectors
        using var scope = context.ScopedCommandBuffer("Ocean", true);
        GraphicsUtilities.SetupCameraProperties(scope.Command, FrameCount, camera, context, camera.Resolution(), out var viewProjectionMatrix);
        scope.Command.SetGlobalVector("_OceanScale", oceanScale);
        scope.Command.SetGlobalVector("_RcpCascadeScales", rcpScales);
        scope.Command.SetGlobalVector("_OceanTexelSize", rcpTexelSizes);

        profile.SetShaderProperties(scope.Command);

        scope.Command.SetGlobalInt("_OceanTextureSliceOffset", flip ? 4 : 0);
        scope.Command.SetGlobalInt("_OceanTextureSlicePreviousOffset", flip ? 0 : 4);

        scope.Command.SetComputeVectorParam(computeShader, "SpectrumStart", spectrumStart);
        scope.Command.SetComputeVectorParam(computeShader, "SpectrumEnd", spectrumEnd);
        scope.Command.SetComputeVectorParam(computeShader, "_RcpCascadeScales", rcpScales);
        scope.Command.SetComputeVectorParam(computeShader, "_CascadeTexelSizes", texelSizes);

        // Get Textures
        scope.Command.SetComputeFloatParam(computeShader, "Smoothness", material.GetFloat("_Smoothness"));
        scope.Command.SetComputeFloatParam(computeShader, "Time", Time.timeSinceLevelLoad);
        scope.Command.GetTemporaryRTArray(tempBufferID4, resolution, resolution, 4, 0, FilterMode.Point, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear, 1, true);
        scope.Command.GetTemporaryRTArray(fftNormalBufferId, resolution, resolution, 4, 0, FilterMode.Point, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear, 1, true);
        scope.Command.GetTemporaryRTArray(tempBufferID2, resolution, resolution, 4, 0, FilterMode.Point, RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear, 1, true);

        // FFT Row
        scope.Command.SetComputeTextureParam(computeShader, 0, "targetTexture", tempBufferID4);
        scope.Command.SetComputeTextureParam(computeShader, 0, "targetTexture1", tempBufferID2);
        scope.Command.SetComputeTextureParam(computeShader, 0, "targetTexture2", fftNormalBufferId);

        scope.Command.SetComputeTextureParam(computeShader, 1, "sourceTexture", tempBufferID4);
        scope.Command.SetComputeTextureParam(computeShader, 1, "sourceTexture1", tempBufferID2);
        scope.Command.SetComputeTextureParam(computeShader, 1, "sourceTexture2", fftNormalBufferId);
        scope.Command.SetComputeTextureParam(computeShader, 1, "DisplacementOutput", DisplacementMap);
        scope.Command.SetComputeTextureParam(computeShader, 1, "NormalOutput", normalMap);

        scope.Command.SetComputeTextureParam(computeShader, 2, "DisplacementInput", DisplacementMap);
        scope.Command.SetComputeTextureParam(computeShader, 2, "_NormalFoamSmoothness", foamSmoothness);
        scope.Command.SetComputeTextureParam(computeShader, 2, "_NormalMap", normalMap);

        scope.Command.DispatchCompute(computeShader, 0, 1, resolution, 4);
        scope.Command.DispatchCompute(computeShader, 1, 1, resolution, 4);
        scope.Command.DispatchNormalized(computeShader, 2, resolution, resolution, 4);

        // Foam
        scope.Command.SetComputeFloatParam(computeShader, "_FoamStrength", profile.FoamStrength);
        scope.Command.SetComputeFloatParam(computeShader, "_FoamDecay", profile.FoamDecay);
        scope.Command.SetComputeFloatParam(computeShader, "_FoamThreshold", profile.FoamThreshold);
        scope.Command.SetComputeTextureParam(computeShader, 4, "_NormalFoamSmoothness", foamSmoothness);
        scope.Command.SetComputeTextureParam(computeShader, 4, "_NormalMap", normalMap);
        scope.Command.DispatchNormalized(computeShader, 4, resolution, resolution, 4);

        // Release resources
        scope.Command.ReleaseTemporaryRT(tempBufferID4);
        scope.Command.ReleaseTemporaryRT(tempBufferID2);
        scope.Command.ReleaseTemporaryRT(fftNormalBufferId);
        scope.Command.GenerateMips(foamSmoothness);
        scope.Command.GenerateMips(DisplacementMap);
        scope.Command.GenerateMips(normalMap);

        var generateMapsKernel = computeShader.FindKernel("GenerateMaps");
        var mipCount = normalMap.mipmapCount;
        scope.Command.SetComputeIntParam(computeShader, "Resolution", resolution >> 2);
        scope.Command.SetComputeIntParam(computeShader, "Size", resolution >> 2);
        scope.Command.SetGlobalTexture("_LengthToRoughness", lengthToRoughness);
        scope.Command.SetComputeTextureParam(computeShader, generateMapsKernel, "_OceanNormalMap", normalMap);

        for (var j = 0; j < mipCount; j++)
        {
            var smoothnessId = smoothnessMapIds.GetProperty(j);
            scope.Command.SetComputeTextureParam(computeShader, generateMapsKernel, smoothnessId, foamSmoothness, j);
        }

        scope.Command.DispatchNormalized(computeShader, generateMapsKernel, (resolution * 4) >> 2, (resolution) >> 2, 1);

        scope.Command.SetGlobalTexture("_OceanFoamSmoothnessMap", foamSmoothness);
        scope.Command.SetGlobalTexture("_OceanNormalMap", normalMap);
        scope.Command.SetGlobalTexture("_OceanDisplacementMap", DisplacementMap);
    }
}
