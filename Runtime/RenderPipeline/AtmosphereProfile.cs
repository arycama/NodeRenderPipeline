using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/Graphics/Atmosphere Profile")]
public class AtmosphereProfile : ScriptableObject
{
    public event Action OnProfileChanged;

    [Header("Air Properties")]
    [SerializeField] private Vector3 airScatter = new(5.8e-6f, 1.35e-5f, 3.31e-5f);
    [SerializeField] private Vector3 airAbsorption = new(2.0556e-06f, 4.9788e-06f, 2.136e-07f);
    [SerializeField, Min(0)] private float airAverageHeight = 7994;

    [Header("Aerosol Properties")]
    [SerializeField, Range(0, 0.001f)] private float aerosolScatter = 3.996e-6f;
    [SerializeField, Range(0, 0.001f)] private float aerosolAbsorption = 4.4e-6f;
    [SerializeField, Range(-1, 1)] private float aerosolAnisotropy = 0.73f;
    [SerializeField, Min(0)] private float aerosolAverageHeight = 1.2e+3f;

    [Header("Ozone Properties")]
    [SerializeField] private float ozoneHeight = 25000;
    [SerializeField] private float ozoneWidth = 15000;

    [Header("Planet Properties")]
    [SerializeField, Range(0.01f, 1f)] private float earthScale = 1f;
    [SerializeField, Min(0)] private float planetRadius = 6.36e+6f;
    [SerializeField, Min(0)] private float atmosphereHeight = 6e+4f;
    [SerializeField] private Color groundColor = new(0.4809999f, 0.4554149f, 0.4451807f);

    [Header("Night Sky")]
    [SerializeField] private Cubemap starTexture = null;
    [SerializeField] private float starSpeed = 0.01f;
    [SerializeField, ColorUsage(false, true)] private Color starColor = Color.white;
    [SerializeField] private Vector3 starAxis = Vector3.right;

    public Vector3 AirScatter => airScatter / earthScale;// (Vector4)(albedo.linear * extinction);
    public Vector3 AirAbsorption => airAbsorption / earthScale; //(Vector4)(extinction - albedo.linear * extinction);
    public float AirAverageHeight => airAverageHeight * earthScale;
    public float AerosolScatter => aerosolScatter / earthScale;// (albedo.linear * extinction).a;
    public float AerosolAbsorption => aerosolAbsorption / earthScale;// (extinction - albedo.linear * extinction).a;
    public float AerosolAverageHeight => aerosolAverageHeight * earthScale;
    public float AerosolAnisotropy => aerosolAnisotropy;
    public float AtmosphereHeight => atmosphereHeight * earthScale;
    public float PlanetRadius => planetRadius * earthScale;
    public float OzoneHeight => ozoneHeight * earthScale;
    public float OzoneWidth => ozoneWidth * earthScale;
    public Cubemap StarTexture => starTexture;
    public Color StarColor => starColor;
    public Color GroundColor => groundColor;

    public int Version { get; private set; }

    public void ProfileChanged()
    {
        Version++;
    }
}
