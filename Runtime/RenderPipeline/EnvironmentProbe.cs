using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class EnvironmentProbe : MonoBehaviour
{
    private static Material previewMaterial;
    private static Mesh previewMesh;

    public static List<EnvironmentProbe> reflectionProbes = new();

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

    private void OnEnable()
    {
        reflectionProbes.Add(this);
    }

    private void OnDisable()
    {
        reflectionProbes.Remove(this);
    }

    private void OnDrawGizmosSelected()
    {
        if (previewMaterial == null)
            previewMaterial = Resources.Load<Material>("Reflection Probe Material");

        if (previewMesh == null)
            previewMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");

        Graphics.DrawMesh(previewMesh, Matrix4x4.TRS(transform.position, transform.rotation, new Vector3(0.5f, 0.5f, 0.5f)), previewMaterial, gameObject.layer);
    }
}
