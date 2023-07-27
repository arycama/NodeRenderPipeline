using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[ExecuteAlways]
public class EnvironmentProbe : MonoBehaviour
{
    private static Material previewMaterial;
    private static Mesh previewMesh;
    private static MaterialPropertyBlock propertyBlock;

    public static Dictionary<EnvironmentProbe, int> reflectionProbes = new();

    [SerializeField, Min(0)]
    private float blendDistance = 1f;

    [SerializeField]
    private bool boxProjection = false;

    [SerializeField]
    private Vector3 size = new(10, 5, 10);

    [SerializeField]
    private Vector3 offset = new(0, 0, 0);

    public float BlendDistance => blendDistance;
    public bool BoxProjection => boxProjection;
    public Vector3 Size { get => size; set => size = value; }
    public Vector3 Offset { get => offset; set => offset = value; }

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        SceneView.beforeSceneGui += OnPreSceneGUICallback;
    }

    private void OnEnable()
    {
        reflectionProbes.Add(this, reflectionProbes.Count);
    }

    private void OnDisable()
    {
        reflectionProbes.Remove(this);
    }

    private static void OnPreSceneGUICallback(SceneView sceneView)
    {
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
}
