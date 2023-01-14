using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class EditorPrefsExtensions
{
    /// <summary>
    /// Saves any public or serialized fields to EditorPrefs. Useful for custom editors, tools, etc.
    /// </summary>
    /// <param name="target"></param>
    public static void SaveToEditorPrefs(this Object target)
    {
        var fields = GetSerializableFields(target);
        foreach (var field in fields)
        {
            var key = target.GetType().Name + "." + field.Name;
            var value = field.GetValue(target);

            if (value is int)
            {
                EditorPrefs.SetInt(key, (int)value);
            }
            else if (value is float)
            {
                EditorPrefs.SetFloat(key, (float)value);
            }
            else if (value is string)
            {
                EditorPrefs.SetString(key, (string)value);
            }
            else if (value is bool)
            {
                EditorPrefs.SetBool(key, (bool)value);
            }
            else if (value is Object)
            {
                EditorPrefs.SetString(key, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value as Object)));
            }
        }
    }

    /// <summary>
    /// Loads any saved values from editor prefs. 
    /// </summary>
    /// <param name="target"></param>
    public static void LoadFromEditorPrefs(this Object target)
    {
        var fields = GetSerializableFields(target);
        foreach (var field in fields)
        {
            var key = target.GetType().Name + "." + field.Name;
            var value = field.GetValue(target);

            var type = field.ReflectedType;

            if (value is int)
            {
                field.SetValue(target, EditorPrefs.GetInt(key));
            }
            else if (value is float)
            {
                field.SetValue(target, EditorPrefs.GetFloat(key));
            }
            else if (value is string)
            {
                field.SetValue(target, EditorPrefs.GetString(key));
            }
            else if (value is bool)
            {
                field.SetValue(target, EditorPrefs.GetBool(key));
            }
            else if (typeof(Object).IsAssignableFrom(type))
            {
                field.SetValue(target, AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(key))));
            }
        }
    }

    /// <summary>
    /// Gets all Serializable fields from an object. (Public, or private with [SerializeField] attribute
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    public static IEnumerable<FieldInfo> GetSerializableFields(this Object target)
    {
        var type = target.GetType();
        var publicFields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        var serializedFields = from field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                               where field.IsDefined(typeof(SerializeField))
                               select field;
        return publicFields.Union(serializedFields);
    }
}