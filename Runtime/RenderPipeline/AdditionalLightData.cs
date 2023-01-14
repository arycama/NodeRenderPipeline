using UnityEngine;

/// <summary>
/// Holds additional light properties that can't be stored on UnityEngine.Light
/// </summary>
[ExecuteAlways]
public class AdditionalLightData : MonoBehaviour
{
    [SerializeField] private AreaLightType areaLightType = AreaLightType.None;
    [SerializeField, Min(0.025f), Tooltip("Size of the actual light. Larger lights have softer specular and shadows")] private float shapeRadius = 0.025f;
    [SerializeField, Tooltip("Width of pyramid, box, tube or area light")] private float shapeWidth = 0.5f;
    [SerializeField, Tooltip("Height of pyramid, box or area light")] private float shapeHeight = 0.5f;

    public float ShapeRadius => shapeRadius;
    public float ShapeWidth { get => shapeWidth; set => shapeWidth = value; }
    public float ShapeHeight { get => shapeHeight; set => shapeHeight = value; }
    public AreaLightType AreaLightType => areaLightType;

#if UNITY_EDITOR
    private Light lightComponent;

    private void OnEnable()
    {
        lightComponent = GetComponent<Light>();
    }

    private void Update()
    {
        // Manually track whether this is an area light, for reasons below
        if (lightComponent.type == LightType.Area)
            areaLightType = AreaLightType.Area;

        // Force area lights to be spot types.. this is because Unity does not render shadows for Area lights, so we must treat them as point lights for the engine..
        if (areaLightType != AreaLightType.None && lightComponent.type != LightType.Spot)
            lightComponent.type = LightType.Spot;

        // Force to realtime, as area lights default to baked
        if (lightComponent.lightmapBakeType != LightmapBakeType.Realtime)
            lightComponent.lightmapBakeType = LightmapBakeType.Realtime;

    }
#endif
}
