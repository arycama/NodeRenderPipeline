using System;
using UnityEditor;
using Object = UnityEngine.Object;

public static class AssetDatabaseUtils
{
    public static T LoadOrCreateAssetAtPath<T>(string path, Func<T> createDelegate) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
        {
            asset = createDelegate();
            AssetDatabase.CreateAsset(asset, path);
        }

        return asset;
    }
}
