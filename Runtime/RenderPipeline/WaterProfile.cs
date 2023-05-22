using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Data/Graphics/Water Profile")]
public class WaterProfile : ScriptableObject
{
    [SerializeField, Tooltip("Gravity, affects total size and height of waves")]
    private float gravity = 9.81f;

    [SerializeField, Tooltip("The size in world units of the simulated patch. Larger values spread waves out, and create bigger waves")]
    private float patchSize = 2048;

    [SerializeField] private float cascadeScale = 5.23f;

    [SerializeField, Range(0f, 2f)] private float foamThreshold = 0.5f;

    [SerializeField, Range(0f, 1f)] private float foamStrength = 0.5f;

    [SerializeField, Range(0f, 1f)] private float foamDecay = 0.85f;

    [SerializeField] private float maxWaterHeight = 32f;

    [SerializeField] private OceanSpectrum localSpectrum = new(1f, 12f, 0f, 1e+5f, 1f, 0.2f, 3.3f, 0.01f);

    [SerializeField] private OceanSpectrum distantSpectrum = new(0f, 12f, 0f, 1e+5f, 1f, 0.2f, 3.3f, 0.01f);

    public float CascadeScale => cascadeScale;
    public float PatchSize => patchSize;
    public float MaxWaveNumber => cascadeScale * 10f;
    public float FoamThreshold => foamThreshold;
    public float FoamStrength => foamStrength;
    public float FoamDecay => foamDecay;
    public float MaxWaterHeight => maxWaterHeight;

    public void SetShaderProperties(CommandBuffer command)
    {
        command.SetGlobalFloat("_OceanGravity", gravity);

        var oceanData = new OceanData(localSpectrum, distantSpectrum);
        ConstantBuffer.PushGlobal(command, oceanData, Shader.PropertyToID("OceanData"));
    }
}
