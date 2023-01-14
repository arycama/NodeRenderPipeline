using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[CustomEditor(typeof(CustomReflectionProbe)), CanEditMultipleObjects]
public class CustomReflectionProbeEditor : Editor
{
    private BoxBoundsHandle boxHandle = new BoxBoundsHandle();

    private void OnSceneGUI()
    {
        var p = target as CustomReflectionProbe;
        Undo.RecordObject(p, "Modify Reflection Probe");

        var t = p.transform;
        Handles.matrix = t.localToWorldMatrix;

        boxHandle.center = Vector3.zero;
        boxHandle.size = p.Size;

        boxHandle.DrawHandle();

        var corners = new Vector3[8]
        {
            new Vector3(-1f, -1f, -1f),
            new Vector3(1f, -1f, -1f),
            new Vector3(-1f, 1f, -1f),
            new Vector3(1f, 1f, -1f),
            new Vector3(-1f, -1f, 1f),
            new Vector3(1f, -1f, 1f),
            new Vector3(-1f, 1f, 1f),
            new Vector3(1f, 1f, 1f),
        };

        var color = new Color(0.75f, 0.75f, 0f, 0.1f);

        var min = p.transform.position - p.Size * 0.5f;
        var max = p.transform.position + p.Size * 0.5f;

        Handles.matrix = Matrix4x4.TRS(t.position, t.rotation, 0.5f * (max - min));
        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

        // Draw all the transparent faces...
        Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[0], corners[2], corners[3], corners[1] }, color, color);
        Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[1], corners[3], corners[7], corners[5] }, color, color);
        Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[0], corners[2], corners[6], corners[4] }, color, color);
        Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[4], corners[6], corners[7], corners[5] }, color, color);
        Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[0], corners[4], corners[5], corners[1] }, color, color);
        Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[2], corners[6], corners[7], corners[3] }, color, color);

        p.transform.position = p.transform.localToWorldMatrix.MultiplyPoint3x4(boxHandle.center);
        p.Size = boxHandle.size;
    }
}