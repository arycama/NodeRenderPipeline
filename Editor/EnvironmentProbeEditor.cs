using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[CustomEditor(typeof(EnvironmentProbe)), CanEditMultipleObjects]
public class EnvironmentProbeEditor : Editor
{
    private BoxBoundsHandle influenceBoxHandle = new(), projectionBoxHandle = new();

    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        base.OnInspectorGUI();
        if(EditorGUI.EndChangeCheck())
        {
            (target as EnvironmentProbe).IsDirty = true;
        }
    }

    private void OnSceneGUI()
    {
        var p = target as EnvironmentProbe;
        Undo.RecordObject(p, "Modify Reflection Probe");

        {
            Handles.matrix = Matrix4x4.TRS(p.transform.position, p.transform.rotation, Vector3.one);
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            influenceBoxHandle.center = p.InfluenceOffset;
            influenceBoxHandle.size = p.InfluenceSize;

            influenceBoxHandle.DrawHandle();

            p.InfluenceOffset = influenceBoxHandle.center;
            p.InfluenceSize = influenceBoxHandle.size;

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

            var color = new Color(0.75f, 0.0f, 0.75f, 0.1f);

            Handles.matrix = Matrix4x4.TRS(p.transform.position + p.transform.rotation *  p.InfluenceOffset, p.transform.rotation, 0.5f * p.InfluenceSize);
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            // Draw all the transparent faces...
            Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[0], corners[2], corners[3], corners[1] }, color, color);
            Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[1], corners[3], corners[7], corners[5] }, color, color);
            Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[0], corners[2], corners[6], corners[4] }, color, color);
            Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[4], corners[6], corners[7], corners[5] }, color, color);
            Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[0], corners[4], corners[5], corners[1] }, color, color);
            Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[2], corners[6], corners[7], corners[3] }, color, color);

        }

        {
            Handles.matrix = Matrix4x4.TRS(p.transform.position, p.transform.rotation, Vector3.one);
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            projectionBoxHandle.center = p.ProjectionOffset;
            projectionBoxHandle.size = p.ProjectionSize;

            projectionBoxHandle.DrawHandle();

            p.ProjectionOffset = projectionBoxHandle.center;
            p.ProjectionSize = projectionBoxHandle.size;

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

            Handles.matrix = Matrix4x4.TRS(p.transform.position + p.transform.rotation * p.ProjectionOffset, p.transform.rotation, 0.5f * p.ProjectionSize);
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            // Draw all the transparent faces...
            Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[0], corners[2], corners[3], corners[1] }, color, color);
            Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[1], corners[3], corners[7], corners[5] }, color, color);
            Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[0], corners[2], corners[6], corners[4] }, color, color);
            Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[4], corners[6], corners[7], corners[5] }, color, color);
            Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[0], corners[4], corners[5], corners[1] }, color, color);
            Handles.DrawSolidRectangleWithOutline(new Vector3[] { corners[2], corners[6], corners[7], corners[3] }, color, color);


        }
    }
}