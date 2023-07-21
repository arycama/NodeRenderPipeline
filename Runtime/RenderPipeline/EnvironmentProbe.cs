using System.Collections.Generic;
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

    private void OnEnable()
    {
        reflectionProbes.Add(this, reflectionProbes.Count);
    }

    private void OnDisable()
    {
        reflectionProbes.Remove(this);
    }

    private void Update()
    {
        //if (Application.isPlaying)
        //    return;

        if (previewMaterial == null)
        {
            var shader = Shader.Find("Hidden/Reflection Probe Preview");
            previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        if (previewMesh == null)
            previewMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        propertyBlock.SetFloat("_Layer", reflectionProbes[this]);

        var rp = new RenderParams(previewMaterial) { matProps = propertyBlock };
        var objectToWorld = Matrix4x4.TRS(transform.position, Quaternion.identity, new Vector3(0.5f, 0.5f, 0.5f));
        Graphics.RenderMesh(rp, previewMesh, 0, objectToWorld);
    }

    private void OnDrawGizmos()
    {

    }
}
