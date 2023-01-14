using UnityEngine;

public static class MaterialExtensions
{
    public static void ToggleKeyword(this Material material, string keyword, bool isEnabled)
    {
        if (isEnabled) material.EnableKeyword(keyword);
        else material.DisableKeyword(keyword);
    }
}