using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(Pow2Attribute))]
public class Pow2Drawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var pow2Attribute = attribute as Pow2Attribute;
        var minvalue = pow2Attribute.MinValue;
        var maxValue = pow2Attribute.MaxValue;

        var valueStart = (int)Mathf.Log(minvalue, 2);
        var valueCount = (int)Mathf.Log(maxValue, 2) + 1 - valueStart;
        var values = new int[valueCount];
        var valueNames = new GUIContent[valueCount];

        for (var i = 0; i < valueCount; i++)
        {
            var value = 1 << (valueStart + i);
            values[i] = value;
            valueNames[i] = new GUIContent($"{value}x{value}");
        }

        property.intValue = EditorGUI.IntPopup(position, label, property.intValue, valueNames, values);
    }
}
