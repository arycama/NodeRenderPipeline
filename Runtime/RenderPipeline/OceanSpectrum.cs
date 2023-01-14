using System;
using UnityEngine;

[Serializable]
public struct OceanSpectrum
{
    [SerializeField, Range(0, 1)]
    private float scale;

    [SerializeField, Range(0, 64)]
    private float windSpeed;

    [SerializeField, Range(0f, 1f)]
    private float windAngle;

    [SerializeField, Min(0f)]
    private float fetch;

    [SerializeField, Range(0, 1)]
    private float spreadBlend;

    [SerializeField, Range(0, 1)]
    private float swell;

    [SerializeField, Min(1e-6f)]
    private float peakEnhancement;

    [SerializeField, Range(0, 5f)]
    private float shortWavesFade;

    public OceanSpectrum(float scale, float windSpeed, float windAngle, float fetch, float spreadBlend, float swell, float peakEnhancement, float shortWavesFade)
    {
        this.scale = scale;
        this.windSpeed = windSpeed;
        this.windAngle = windAngle;
        this.fetch = fetch;
        this.spreadBlend = spreadBlend;
        this.swell = swell;
        this.peakEnhancement = peakEnhancement;
        this.shortWavesFade = shortWavesFade;
    }
}
