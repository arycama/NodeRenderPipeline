using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Data/Graphics/Cloud Layer")]
public class CloudProfile : ScriptableObject
{
    [Header("Quality")]
    [SerializeField, Range(1, 16)]
    private int lightingSamples = 5;

    [SerializeField]
    private float lightingDistance = 512f;

    [SerializeField]
    private float minSamples = 8;

    [SerializeField, Range(1, 256)]
    private int maxSamples = 64;

    [SerializeField, Min(0f)]
    private float sampleFactor = 1f;

    [SerializeField, Range(0, 1), Tooltip("Stop Raymarching when background visibility falls below this value")]
    private float transmittanceThreshold = 0.05f;

    [Header("Shape Properties")]
    [SerializeField, Min(0)]
    private float weatherMapScale = 65536;

    [SerializeField, Min(0)]
    private float startHeight = 1024;

    [SerializeField, Min(0)]
    private float layerThickness = 512;

    [Header("Appearance")]
    [SerializeField]
    private Color albedo = Color.white;

    [SerializeField]
    private Vector2 windSpeed = new(0, 0);

    [SerializeField, Range(0, 0.1f)]
    private float density = 0.05f;

    [SerializeField, Min(0)]
    private float noiseScale = 16384;

    [SerializeField, Min(0)]
    private float detailScale = 512;

    [SerializeField, Range(0, 1)]
    private float detailStrength = 0.2f;

    [Header("Lighting")]
    [SerializeField, Range(0, 1)]
    private float frontScatter = 0.8f;

    [SerializeField, Range(0, 1)]
    private float backScatter = 0.3f;

    [SerializeField, Range(0, 1)]
    private float scatterBlend = 0.5f;

    [SerializeField, Range(1, 8)]
    private int scatterOctaves = 1;

    [SerializeField, Range(0, 1)]
    private float scatterEccentricity = 0.5f;

    [SerializeField, Range(0, 1)]
    private float scatterContribution = 0.5f;

    [SerializeField, Range(0, 1)]
    private float scatterAttenuation = 0.5f;

    public float Density => density;
    public float StartHeight => startHeight;
    public float LayerThickness => layerThickness;

    public float GetMaxDistance(float planetRadius)
    {
        // Calculate sample counts
        var a = planetRadius + startHeight;
        var c = planetRadius + startHeight + layerThickness;
        return 2f * Mathf.Sqrt(c * c - a * a);
    }

    public void SetMaterialProperties(ComputeShader computeShader, int kernelIndex, CommandBuffer command, float planetRadius)
    {
        // Calculate sample counts
        var longestDistance = GetMaxDistance(planetRadius);
        var sampleDistance = longestDistance / maxSamples;

        command.SetComputeFloatParam(computeShader, "_Density", density);
        command.SetComputeFloatParam(computeShader, "_DetailScale", 1f / detailScale);
        command.SetComputeFloatParam(computeShader, "_MinSamples", this.minSamples);
        command.SetComputeFloatParam(computeShader, "_MaxSamples", maxSamples);
        command.SetComputeFloatParam(computeShader, "_SampleFactor", sampleFactor);
        command.SetComputeFloatParam(computeShader, "_SampleDistance", sampleDistance);
        command.SetComputeFloatParam(computeShader, "_MaxDistance", longestDistance);
        command.SetComputeFloatParam(computeShader, "_NoiseScale", 1f / noiseScale);
        command.SetComputeFloatParam(computeShader, "_WeatherScale", 1f / weatherMapScale);

        command.SetComputeFloatParam(computeShader, "_LightingDistance", lightingDistance);
        command.SetComputeFloatParam(computeShader, "_LightingSamples", lightingSamples);

        command.SetComputeFloatParam(computeShader, "_FrontScatter", frontScatter);
        command.SetComputeFloatParam(computeShader, "_BackScatter", backScatter);
        command.SetComputeFloatParam(computeShader, "_ScatterBlend", scatterBlend);

        command.SetComputeFloatParam(computeShader, "_Thickness", layerThickness);
        command.SetComputeFloatParam(computeShader, "_Height", startHeight);
        command.SetComputeFloatParam(computeShader, "_DetailStrength", detailStrength);
        command.SetComputeFloatParam(computeShader, "_TransmittanceThreshold", transmittanceThreshold);

        command.SetComputeFloatParam(computeShader, "_ScatterOctaves", scatterOctaves);
        command.SetComputeFloatParam(computeShader, "_ScatterEccentricity", scatterEccentricity);
        command.SetComputeFloatParam(computeShader, "_ScatterContribution", scatterContribution);
        command.SetComputeFloatParam(computeShader, "_ScatterAttenuation", scatterAttenuation);
        command.SetComputeVectorParam(computeShader, "_WindSpeed", windSpeed / weatherMapScale);
        command.SetComputeVectorParam(computeShader, "_Albedo", albedo.linear);
    }
}