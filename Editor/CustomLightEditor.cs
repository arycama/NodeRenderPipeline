using System;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

[CustomEditorForRenderPipeline(typeof(Light), typeof(CustomRenderPipelineAsset))]
public class CustomLightEditor : LightEditor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var typeProperty = serializedObject.FindProperty("m_Type");
        var type = (LightType)typeProperty.enumValueIndex;

        if (type == LightType.Spot)
        {
            var shapeProperty = serializedObject.FindProperty("m_Shape");
            EditorGUILayout.PropertyField(shapeProperty);
            settings.DrawInnerAndOuterSpotAngle();
        }

        serializedObject.ApplyModifiedProperties();

        base.OnInspectorGUI();
    }

    //copy of CoreLightEditorUtilities
    static float SliderLineHandle(Vector3 position, Vector3 direction, float value)
    {
        return SliderLineHandle(GUIUtility.GetControlID(FocusType.Passive), position, direction, value, "");
    }


    //copy of CoreLightEditorUtilities
    static void DrawHandleLabel(Vector3 handlePosition, string labelText, float offsetFromHandle = 0.3f)
    {
        Vector3 labelPosition = Vector3.zero;

        var style = new GUIStyle { normal = { background = Texture2D.whiteTexture } };
        GUI.color = new Color(0.82f, 0.82f, 0.82f, 1);

        labelPosition = handlePosition + Handles.inverseMatrix.MultiplyVector(Vector3.up) * HandleUtility.GetHandleSize(handlePosition) * offsetFromHandle;
        Handles.Label(labelPosition, labelText, style);
    }

    //copy of CoreLightEditorUtilities
    static float SliderLineHandle(int id, Vector3 position, Vector3 direction, float value, string labelText = "")
    {
        Vector3 pos = position + direction * value;
        float sizeHandle = HandleUtility.GetHandleSize(pos);
        bool temp = GUI.changed;
        GUI.changed = false;
        pos = Handles.Slider(id, pos, direction, sizeHandle * 0.03f, Handles.DotHandleCap, 0f);
        if (GUI.changed)
        {
            value = Vector3.Dot(pos - position, direction);
        }

        GUI.changed |= temp;

        if (GUIUtility.hotControl == id && !String.IsNullOrEmpty(labelText))
        {
            labelText += FormattableString.Invariant($"{value:0.00}");
            DrawHandleLabel(pos, labelText);
        }

        return value;
    }

    //TODO: decompose arguments (or tuples) + put back to CoreLightEditorUtilities
    static void DrawOrthoFrustumWireframe(Vector4 widthHeightMaxRangeMinRange, float distanceTruncPlane = 0f)
    {
        float halfWidth = widthHeightMaxRangeMinRange.x * 0.5f;
        float halfHeight = widthHeightMaxRangeMinRange.y * 0.5f;
        float maxRange = widthHeightMaxRangeMinRange.z;
        float minRange = widthHeightMaxRangeMinRange.w;

        Vector3 sizeX = new Vector3(halfWidth, 0, 0);
        Vector3 sizeY = new Vector3(0, halfHeight, 0);
        Vector3 nearEnd = new Vector3(0, 0, minRange);
        Vector3 farEnd = new Vector3(0, 0, maxRange);

        Vector3 s1 = nearEnd + sizeX + sizeY;
        Vector3 s2 = nearEnd - sizeX + sizeY;
        Vector3 s3 = nearEnd - sizeX - sizeY;
        Vector3 s4 = nearEnd + sizeX - sizeY;

        Vector3 e1 = farEnd + sizeX + sizeY;
        Vector3 e2 = farEnd - sizeX + sizeY;
        Vector3 e3 = farEnd - sizeX - sizeY;
        Vector3 e4 = farEnd + sizeX - sizeY;

        Handles.DrawLine(s1, s2);
        Handles.DrawLine(s2, s3);
        Handles.DrawLine(s3, s4);
        Handles.DrawLine(s4, s1);

        Handles.DrawLine(e1, e2);
        Handles.DrawLine(e2, e3);
        Handles.DrawLine(e3, e4);
        Handles.DrawLine(e4, e1);

        Handles.DrawLine(s1, e1);
        Handles.DrawLine(s2, e2);
        Handles.DrawLine(s3, e3);
        Handles.DrawLine(s4, e4);

        if (distanceTruncPlane > 0f)
        {
            Vector3 truncPoint = new Vector3(0, 0, distanceTruncPlane);
            Vector3 t1 = truncPoint + sizeX + sizeY;
            Vector3 t2 = truncPoint - sizeX + sizeY;
            Vector3 t3 = truncPoint - sizeX - sizeY;
            Vector3 t4 = truncPoint + sizeX - sizeY;

            Handles.DrawLine(t1, t2);
            Handles.DrawLine(t2, t3);
            Handles.DrawLine(t3, t4);
            Handles.DrawLine(t4, t1);
        }
    }

    //TODO: decompose arguments (or tuples) + put back to CoreLightEditorUtilities
    static Vector4 DrawOrthoFrustumHandle(Vector4 widthHeightMaxRangeMinRange, bool useNearHandle)
    {
        float halfWidth = widthHeightMaxRangeMinRange.x * 0.5f;
        float halfHeight = widthHeightMaxRangeMinRange.y * 0.5f;
        float maxRange = widthHeightMaxRangeMinRange.z;
        float minRange = widthHeightMaxRangeMinRange.w;
        Vector3 farEnd = new Vector3(0, 0, maxRange);

        if (useNearHandle)
        {
            minRange = SliderLineHandle(Vector3.zero, Vector3.forward, minRange);
        }

        maxRange = SliderLineHandle(Vector3.zero, Vector3.forward, maxRange);

        EditorGUI.BeginChangeCheck();
        halfWidth = SliderLineHandle(farEnd, Vector3.right, halfWidth);
        halfWidth = SliderLineHandle(farEnd, Vector3.left, halfWidth);
        if (EditorGUI.EndChangeCheck())
        {
            halfWidth = Mathf.Max(0f, halfWidth);
        }

        EditorGUI.BeginChangeCheck();
        halfHeight = SliderLineHandle(farEnd, Vector3.up, halfHeight);
        halfHeight = SliderLineHandle(farEnd, Vector3.down, halfHeight);
        if (EditorGUI.EndChangeCheck())
        {
            halfHeight = Mathf.Max(0f, halfHeight);
        }

        return new Vector4(halfWidth * 2f, halfHeight * 2f, maxRange, minRange);
    }

    //copy of CoreLightEditorUtilities
    static Vector3[] GetFrustrumProjectedRectAngles(float distance, float aspect, float tanFOV)
    {
        Vector3 sizeX;
        Vector3 sizeY;
        float minXYTruncSize = distance * tanFOV;
        if (aspect >= 1.0f)
        {
            sizeX = new Vector3(minXYTruncSize * aspect, 0, 0);
            sizeY = new Vector3(0, minXYTruncSize, 0);
        }
        else
        {
            sizeX = new Vector3(minXYTruncSize, 0, 0);
            sizeY = new Vector3(0, minXYTruncSize / aspect, 0);
        }

        Vector3 center = new Vector3(0, 0, distance);
        Vector3[] angles =
        {
            center + sizeX + sizeY,
            center - sizeX + sizeY,
            center - sizeX - sizeY,
            center + sizeX - sizeY
        };

        return angles;
    }

    static Vector3[] GetSphericalProjectedRectAngles(float distance, float aspect, float tanFOV)
    {
        var angles = GetFrustrumProjectedRectAngles(distance, aspect, tanFOV);
        for (int index = 0; index < 4; ++index)
            angles[index] = angles[index].normalized * distance;
        return angles;
    }

    //TODO: decompose arguments (or tuples) + put back to CoreLightEditorUtilities
    // Same as Gizmo.DrawFrustum except that when aspect is below one, fov represent fovX instead of fovY
    // Use to match our light frustum pyramid behavior
    static void DrawSpherePortionWireframe(Vector4 aspectFovMaxRangeMinRange, float distanceTruncPlane = 0f)
    {
        float aspect = aspectFovMaxRangeMinRange.x;
        float fov = aspectFovMaxRangeMinRange.y;
        float maxRange = aspectFovMaxRangeMinRange.z;
        float minRange = aspectFovMaxRangeMinRange.w;
        float tanfov = Mathf.Tan(Mathf.Deg2Rad * fov * 0.5f);

        var startAngles = new Vector3[4];
        if (minRange > 0f)
        {
            startAngles = GetFrustrumProjectedRectAngles(minRange, aspect, tanfov);
            Handles.DrawLine(startAngles[0], startAngles[1]);
            Handles.DrawLine(startAngles[1], startAngles[2]);
            Handles.DrawLine(startAngles[2], startAngles[3]);
            Handles.DrawLine(startAngles[3], startAngles[0]);
        }

        if (distanceTruncPlane > 0f)
        {
            var truncAngles = GetFrustrumProjectedRectAngles(distanceTruncPlane, aspect, tanfov);
            Handles.DrawLine(truncAngles[0], truncAngles[1]);
            Handles.DrawLine(truncAngles[1], truncAngles[2]);
            Handles.DrawLine(truncAngles[2], truncAngles[3]);
            Handles.DrawLine(truncAngles[3], truncAngles[0]);
        }

        var endAngles = GetSphericalProjectedRectAngles(maxRange, aspect, tanfov);
        var planProjectedCrossNormal0 = new Vector3(endAngles[0].y, -endAngles[0].x, 0).normalized;
        var planProjectedCrossNormal1 = new Vector3(endAngles[1].y, -endAngles[1].x, 0).normalized;
        Vector3[] faceNormals = new[]
        {
            Vector3.right - Vector3.Dot((endAngles[3] + endAngles[0]).normalized, Vector3.right) * (endAngles[3] + endAngles[0]).normalized,
            Vector3.up - Vector3.Dot((endAngles[0] + endAngles[1]).normalized, Vector3.up) * (endAngles[0] + endAngles[1]).normalized,
            Vector3.left - Vector3.Dot((endAngles[1] + endAngles[2]).normalized, Vector3.left) * (endAngles[1] + endAngles[2]).normalized,
            Vector3.down - Vector3.Dot((endAngles[2] + endAngles[3]).normalized, Vector3.down) * (endAngles[2] + endAngles[3]).normalized,
            //cross
            planProjectedCrossNormal0 - Vector3.Dot((endAngles[1] + endAngles[3]).normalized, planProjectedCrossNormal0) * (endAngles[1] + endAngles[3]).normalized,
            planProjectedCrossNormal1 - Vector3.Dot((endAngles[0] + endAngles[2]).normalized, planProjectedCrossNormal1) * (endAngles[0] + endAngles[2]).normalized,
        };

        float[] faceAngles = new[]
        {
            Vector3.Angle(endAngles[3], endAngles[0]),
            Vector3.Angle(endAngles[0], endAngles[1]),
            Vector3.Angle(endAngles[1], endAngles[2]),
            Vector3.Angle(endAngles[2], endAngles[3]),
            Vector3.Angle(endAngles[1], endAngles[3]),
            Vector3.Angle(endAngles[0], endAngles[2]),
        };

        Handles.DrawWireArc(Vector3.zero, faceNormals[0], endAngles[0], faceAngles[0], maxRange);
        Handles.DrawWireArc(Vector3.zero, faceNormals[1], endAngles[1], faceAngles[1], maxRange);
        Handles.DrawWireArc(Vector3.zero, faceNormals[2], endAngles[2], faceAngles[2], maxRange);
        Handles.DrawWireArc(Vector3.zero, faceNormals[3], endAngles[3], faceAngles[3], maxRange);
        Handles.DrawWireArc(Vector3.zero, faceNormals[4], endAngles[0], faceAngles[4], maxRange);
        Handles.DrawWireArc(Vector3.zero, faceNormals[5], endAngles[1], faceAngles[5], maxRange);

        Handles.DrawLine(startAngles[0], endAngles[0]);
        Handles.DrawLine(startAngles[1], endAngles[1]);
        Handles.DrawLine(startAngles[2], endAngles[2]);
        Handles.DrawLine(startAngles[3], endAngles[3]);
    }


    //copy of CoreLightEditorUtilities
    static Vector2 SliderPlaneHandle(Vector3 origin, Vector3 axis1, Vector3 axis2, Vector2 position)
    {
        Vector3 pos = origin + position.x * axis1 + position.y * axis2;
        float sizeHandle = HandleUtility.GetHandleSize(pos);
        bool temp = GUI.changed;
        GUI.changed = false;
        pos = Handles.Slider2D(pos, Vector3.forward, axis1, axis2, sizeHandle * 0.03f, Handles.DotHandleCap, 0f);
        if (GUI.changed)
        {
            position = new Vector2(Vector3.Dot(pos, axis1), Vector3.Dot(pos, axis2));
        }

        GUI.changed |= temp;
        return position;
    }


    //TODO: decompose arguments (or tuples) + put back to CoreLightEditorUtilities
    static Vector4 DrawSpherePortionHandle(Vector4 aspectFovMaxRangeMinRange, bool useNearPlane, float minAspect = 0.05f, float maxAspect = 20f, float minFov = 1f)
    {
        float aspect = aspectFovMaxRangeMinRange.x;
        float fov = aspectFovMaxRangeMinRange.y;
        float maxRange = aspectFovMaxRangeMinRange.z;
        float minRange = aspectFovMaxRangeMinRange.w;
        float tanfov = Mathf.Tan(Mathf.Deg2Rad * fov * 0.5f);

        var endAngles = GetSphericalProjectedRectAngles(maxRange, aspect, tanfov);

        if (useNearPlane)
        {
            minRange = SliderLineHandle(Vector3.zero, Vector3.forward, minRange);
        }

        maxRange = SliderLineHandle(Vector3.zero, Vector3.forward, maxRange);

        float distanceRight = HandleUtility.DistanceToLine(endAngles[0], endAngles[3]);
        float distanceLeft = HandleUtility.DistanceToLine(endAngles[1], endAngles[2]);
        float distanceUp = HandleUtility.DistanceToLine(endAngles[0], endAngles[1]);
        float distanceDown = HandleUtility.DistanceToLine(endAngles[2], endAngles[3]);

        int pointIndex = 0;
        if (distanceRight < distanceLeft)
        {
            if (distanceUp < distanceDown)
                pointIndex = 0;
            else
                pointIndex = 3;
        }
        else
        {
            if (distanceUp < distanceDown)
                pointIndex = 1;
            else
                pointIndex = 2;
        }

        Vector2 send = endAngles[pointIndex];
        Vector3 farEnd = new Vector3(0, 0, endAngles[0].z);
        EditorGUI.BeginChangeCheck();
        Vector2 received = SliderPlaneHandle(farEnd, Vector3.right, Vector3.up, send);
        if (EditorGUI.EndChangeCheck())
        {
            bool fixedFov = Event.current.control && !Event.current.shift;
            bool fixedAspect = Event.current.shift && !Event.current.control;

            //work on positive quadrant
            int xSign = send.x < 0f ? -1 : 1;
            int ySign = send.y < 0f ? -1 : 1;
            Vector2 corrected = new Vector2(received.x * xSign, received.y * ySign);

            //fixed aspect correction
            if (fixedAspect)
            {
                corrected.x = corrected.y * aspect;
            }

            //remove aspect deadzone
            if (corrected.x > maxAspect * corrected.y)
            {
                corrected.y = corrected.x * minAspect;
            }

            if (corrected.x < minAspect * corrected.y)
            {
                corrected.x = corrected.y / maxAspect;
            }

            //remove fov deadzone
            float deadThresholdFoV = Mathf.Tan(Mathf.Deg2Rad * minFov * 0.5f) * maxRange;
            corrected.x = Mathf.Max(corrected.x, deadThresholdFoV);
            corrected.y = Mathf.Max(corrected.y, deadThresholdFoV, Mathf.Epsilon * 100); //prevent any division by zero

            if (!fixedAspect)
            {
                aspect = corrected.x / corrected.y;
            }

            float min = Mathf.Min(corrected.x, corrected.y);
            if (!fixedFov && maxRange > Mathf.Epsilon * 100)
            {
                fov = Mathf.Atan(min / maxRange) * 2f * Mathf.Rad2Deg;
            }
        }

        return new Vector4(aspect, fov, maxRange, minRange);
    }

    protected override void OnSceneGUI()
    {
        var light = serializedObject.targetObject as Light;
        var type = light.type;
        var shape = light.shape;

        if (!light.TryGetComponent<AdditionalLightData>(out var additionalLightData))
            additionalLightData = light.gameObject.AddComponent<AdditionalLightData>();

        switch (type)
        {
            case LightType.Directional:
                using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                    CoreLightEditorUtilities.DrawDirectionalLightGizmo(light);
                break;
            case LightType.Point:
                using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, Quaternion.identity, Vector3.one)))
                    CoreLightEditorUtilities.DrawPointLightGizmo(light);
                break;
            case LightType.Spot:
                if (additionalLightData.AreaLightType != AreaLightType.None)
                {
                    using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                    {
                        CoreLightEditorUtilities.DrawPointLightGizmo(light);
                        CoreLightEditorUtilities.DrawRectangleLightGizmo(light);
                        additionalLightData.ShapeWidth = light.areaSize.x;
                        additionalLightData.ShapeHeight = light.areaSize.y;
                    }
                }
                else
                {
                    using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                        switch (shape)
                        {
                            case LightShape.Cone:
                                CoreLightEditorUtilities.DrawSpotLightGizmo(light);
                                break;
                            case LightShape.Pyramid:
                                using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                                {
                                    Vector4 aspectFovMaxRangeMinRange = new Vector4(additionalLightData.ShapeWidth / additionalLightData.ShapeHeight, light.spotAngle, light.range);
                                    Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                                    Handles.color = light.color;
                                    DrawSpherePortionWireframe(aspectFovMaxRangeMinRange, light.shadowNearPlane);
                                    Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                                    Handles.color = light.color;
                                    DrawSpherePortionWireframe(aspectFovMaxRangeMinRange, light.shadowNearPlane);
                                    EditorGUI.BeginChangeCheck();
                                    Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                                    Handles.color = light.color;
                                    aspectFovMaxRangeMinRange = DrawSpherePortionHandle(aspectFovMaxRangeMinRange, false);
                                    Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                                    Handles.color = light.color;
                                    aspectFovMaxRangeMinRange = DrawSpherePortionHandle(aspectFovMaxRangeMinRange, false);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        Undo.RecordObject(light, "Adjust Pyramid Spot Light");
                                        light.spotAngle = aspectFovMaxRangeMinRange.y;
                                        light.range = aspectFovMaxRangeMinRange.z;

                                        var angle = light.spotAngle * Mathf.Deg2Rad * 0.5f;
                                        additionalLightData.ShapeWidth = light.range * Mathf.Sin(angle) / Mathf.Cos(angle) * aspectFovMaxRangeMinRange.x;
                                        additionalLightData.ShapeHeight = light.range * Mathf.Sin(angle) / Mathf.Cos(angle);
                                    }

                                    // Handles.color reseted at end of scope
                                }

                                break;
                            case LightShape.Box:
                                using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                                {
                                    Vector4 widthHeightMaxRangeMinRange = new Vector4(additionalLightData.ShapeWidth, additionalLightData.ShapeHeight, light.range);
                                    Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                                    Handles.color = light.color;
                                    DrawOrthoFrustumWireframe(widthHeightMaxRangeMinRange, light.shadowNearPlane);
                                    Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                                    Handles.color = light.color;
                                    DrawOrthoFrustumWireframe(widthHeightMaxRangeMinRange, light.shadowNearPlane);
                                    EditorGUI.BeginChangeCheck();
                                    Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                                    Handles.color = light.color;
                                    widthHeightMaxRangeMinRange = DrawOrthoFrustumHandle(widthHeightMaxRangeMinRange, false);
                                    Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                                    Handles.color = light.color;
                                    widthHeightMaxRangeMinRange = DrawOrthoFrustumHandle(widthHeightMaxRangeMinRange, false);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        Undo.RecordObject(light, "Adjust Box Spot Light");
                                        additionalLightData.ShapeWidth = widthHeightMaxRangeMinRange.x;
                                        additionalLightData.ShapeHeight = widthHeightMaxRangeMinRange.y;
                                        light.range = widthHeightMaxRangeMinRange.z;
                                    }

                                    // Handles.color reseted at end of scope
                                }

                                break;
                        }
                }

                break;
        }
    }
}