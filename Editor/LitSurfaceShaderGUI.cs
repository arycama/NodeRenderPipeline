using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class LitSurfaceShaderGUI : ShaderGUI
{
    public enum Mode
    {
        Opaque,
        Cutout,
        Fade,
        Transparent
    }

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        base.OnGUI(materialEditor, properties);

        var parallaxMapProperty = FindProperty("_ParallaxMap", properties);
        var hasParallaxMap = parallaxMapProperty.textureValue != null;
        var material = materialEditor.target as Material;
        material.ToggleKeyword("_PARALLAXMAP", hasParallaxMap);

        material.SetFloat("Anisotropy", material.GetTexture("_AnisotropyMap") == null ? 0f : 1f);
        material.SetFloat("Bent_Normal", material.GetTexture("_BentNormal") == null ? 0f : 1f);

        var hasBlurryRefractions = FindProperty("Blurry_Refractions", properties).floatValue > 0f;

        var mode = (Mode)FindProperty("Mode", properties).floatValue;
        switch (mode)
        {
            case Mode.Opaque:
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                break;
            case Mode.Cutout:
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                break;
            case Mode.Fade:
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)(hasBlurryRefractions ? BlendMode.Zero : BlendMode.OneMinusSrcAlpha));
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                break;
            case Mode.Transparent:
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)(hasBlurryRefractions ? BlendMode.Zero : BlendMode.OneMinusSrcAlpha));
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                break;
        }
    }
}