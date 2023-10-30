using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

[ExecuteAlways]
public class EnvironmentProbe : MonoBehaviour
{
    private static Material previewMaterial;
    private static Mesh previewMesh;
    private static MaterialPropertyBlock propertyBlock;

    public static Dictionary<EnvironmentProbe, int> reflectionProbes = new();

    [SerializeField, Min(0)] private float blendDistance = 1f;
    [SerializeField] private bool boxProjection = false;

    [SerializeField, FormerlySerializedAs("size")] private Vector3 projectionSize = new(10, 5, 10);
    [SerializeField, FormerlySerializedAs("offset")] private Vector3 projectionOffset = new(0, 0, 0);

    [SerializeField] private Vector3 influenceSize = new Vector3(10, 5, 10);
    [SerializeField] private Vector3 influenceOffset = new Vector3(0, 0, 0);

    public float BlendDistance => blendDistance;
    public bool BoxProjection => boxProjection;
    public Vector3 ProjectionSize { get => projectionSize; set => projectionSize = value; }
    public Vector3 ProjectionOffset { get => projectionOffset; set => projectionOffset = value; }
    public Vector3 InfluenceSize { get => influenceSize; set => influenceSize = value; }
    public Vector3 InfluenceOffset { get => influenceOffset; set => influenceOffset = value; }

    public bool IsDirty { get; set; }

    public void ClearDirty()
    {
        IsDirty = false;
    }

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        SceneView.beforeSceneGui += OnPreSceneGUICallback;
    }

    private void OnEnable()
    {
        reflectionProbes.Add(this, reflectionProbes.Count);
        IsDirty = true;
    }

    private void OnDisable()
    {
        reflectionProbes.Remove(this);
    }

    private void Update()
    {
        // Todo: Editor only
        if(transform.hasChanged)
        {
            IsDirty = true;
            transform.hasChanged = false;
        }
    }

    private static void OnPreSceneGUICallback(SceneView sceneView)
    {
        if (!UnityEditor.Handles.ShouldRenderGizmos())
            return;

        if (previewMaterial == null)
        {
            var shader = Shader.Find("Hidden/Reflection Probe Preview");
            previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        if (previewMesh == null)
            previewMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        foreach (var probe in reflectionProbes)
        {
            propertyBlock.SetFloat("_Layer", probe.Value);

            // draw a preview sphere that scales with overall GO scale, but always uniformly
            var scale = probe.Key.transform.lossyScale.magnitude * 0.5f;

            var objectToWorld = Matrix4x4.TRS(probe.Key.transform.position, Quaternion.identity, Vector3.one * scale);
            Graphics.DrawMesh(previewMesh, objectToWorld, previewMaterial, 0, SceneView.currentDrawingSceneView.camera, 0, propertyBlock);
        }
    }

    [ContextMenu("Set Influence Bounds to Projection Bounds")]
    private void SetInfluenceBoundsToProjectionBounds()
    {
        InfluenceSize = projectionSize;
        influenceOffset = projectionOffset;
    }    
}
