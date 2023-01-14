// Created by Unknown 12/07/19

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class TextureCombiner : ScriptableWizard
{
    [SerializeField]
    private bool isLinear = false;

    [SerializeField]
    private TextureFileFormat textureFormat = TextureFileFormat.Png;

    [SerializeField]
    private Texture2D textureR = null;

    [SerializeField]
    private ColorWriteMask sourceChannelR = ColorWriteMask.Red;

    [SerializeField]
    private bool invertR = false;

    [SerializeField]
    private Texture2D textureG = null;

    [SerializeField]
    private ColorWriteMask sourceChannelG = ColorWriteMask.Green;

    [SerializeField]
    private bool invertG = false;

    [SerializeField]
    private Texture2D textureB = null;

    [SerializeField]
    private ColorWriteMask sourceChannelB = ColorWriteMask.Blue;

    [SerializeField]
    private bool invertB = false;

    [SerializeField]
    private Texture2D textureA = null;

    [SerializeField]
    private ColorWriteMask sourceChannelA = ColorWriteMask.Red;

    [SerializeField]
    private bool invertA = false;

    [SerializeField, HideInInspector]
    private string lastPath = string.Empty;

    [MenuItem("Tools/Textures/Texture Combiner")]
    private static void OnMenuSelect()
    {
        DisplayWizard<TextureCombiner>("Texture Combiner", "Combine and Close", "Combine");
    }

    private void OnEnable()
    {
        this.LoadFromEditorPrefs();
    }

    private void OnWizardCreate()
    {
        CombineTextures();
    }

    private void OnWizardOtherButton()
    {
        CombineTextures();
    }

    private void CombineTextures()
    {
        var lastFileName = string.IsNullOrEmpty(lastPath) ? "Combined Texture" : Path.GetFileNameWithoutExtension(lastPath);
        var extension = textureFormat.ToString().ToLower();
        lastPath = EditorUtility.SaveFilePanelInProject("Title", lastFileName, extension, "message", lastPath);
        if (string.IsNullOrEmpty(lastPath))
        {
            return;
        }

        this.SaveToEditorPrefs();

        // Get the max resolution
        var width = Mathf.Max(textureR ? textureR.width : 0, textureG ? textureG.width : 0, textureB ? textureB.width : 0, textureA ? textureA.width : 0);
        var height = Mathf.Max(textureR ? textureR.height : 0, textureG ? textureG.height : 0, textureB ? textureB.height : 0, textureA ? textureA.height : 0);

        var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        var shader = Shader.Find("Hidden/Blit ColorMask");
        Debug.Assert(shader != null, "Shader (Hidden/Blit ColorMask) was not found or has compile errors.");
        var material = new Material(shader);

        RenderTexture.active = target;
        GL.Clear(false, true, Color.black);

        // blit each texture into the destination, depending on it's channel
        if (textureR != null)
        {
            material.SetFloat("_ColorMask", (int)ColorWriteMask.Red);
            material.SetVector("_ColorSelector", new Vector4(sourceChannelR.HasFlag(ColorWriteMask.Red) ? 1 : 0, sourceChannelR.HasFlag(ColorWriteMask.Green) ? 1 : 0, sourceChannelR.HasFlag(ColorWriteMask.Blue) ? 1 : 0, sourceChannelR.HasFlag(ColorWriteMask.Alpha) ? 1 : 0));
            material.ToggleKeyword("_INVERT_ON", invertR);
            Graphics.Blit(textureR, target, material);
        }

        if (textureG != null)
        {
            material.SetFloat("_ColorMask", (int)ColorWriteMask.Green);
            material.SetVector("_ColorSelector", new Vector4(sourceChannelG.HasFlag(ColorWriteMask.Red) ? 1 : 0, sourceChannelG.HasFlag(ColorWriteMask.Green) ? 1 : 0, sourceChannelG.HasFlag(ColorWriteMask.Blue) ? 1 : 0, sourceChannelG.HasFlag(ColorWriteMask.Alpha) ? 1 : 0));
            material.ToggleKeyword("_INVERT_ON", invertG);
            Graphics.Blit(textureG, target, material);
        }

        if (textureB != null)
        {
            material.SetFloat("_ColorMask", (int)ColorWriteMask.Blue);
            material.SetVector("_ColorSelector", new Vector4(sourceChannelB.HasFlag(ColorWriteMask.Red) ? 1 : 0, sourceChannelB.HasFlag(ColorWriteMask.Green) ? 1 : 0, sourceChannelB.HasFlag(ColorWriteMask.Blue) ? 1 : 0, sourceChannelB.HasFlag(ColorWriteMask.Alpha) ? 1 : 0));
            material.ToggleKeyword("_INVERT_ON", invertB);
            Graphics.Blit(textureB, target, material);
        }

        if (textureA != null)
        {
            material.SetFloat("_ColorMask", (int)ColorWriteMask.Alpha);
            material.SetVector("_ColorSelector", new Vector4(sourceChannelA.HasFlag(ColorWriteMask.Red) ? 1 : 0, sourceChannelA.HasFlag(ColorWriteMask.Green) ? 1 : 0, sourceChannelA.HasFlag(ColorWriteMask.Blue) ? 1 : 0, sourceChannelA.HasFlag(ColorWriteMask.Alpha) ? 1 : 0));
            material.ToggleKeyword("_INVERT_ON", invertA);
            Graphics.Blit(textureA, target, material);
        }

        var texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
        RenderTexture.active = target;
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);

        byte[] textureBytes;
        switch (textureFormat)
        {
            case TextureFileFormat.Png:
                textureBytes = texture.EncodeToPNG();
                break;
            case TextureFileFormat.Tga:
                textureBytes = texture.EncodeToTGA();
                break;
            case TextureFileFormat.Jpg:
                textureBytes = texture.EncodeToJPG();
                break;
            case TextureFileFormat.Exr:
                textureBytes = texture.EncodeToEXR();
                break;
            default:
                throw new NotImplementedException();
        }

        File.WriteAllBytes(lastPath, textureBytes);

        AssetDatabase.Refresh();
        var asset = AssetDatabase.LoadMainAssetAtPath(lastPath);
        EditorGUIUtility.PingObject(asset);
        Selection.activeObject = asset;

        if (isLinear)
        {
            var importer = AssetImporter.GetAtPath(lastPath) as TextureImporter;
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }
    }

    private enum TextureFileFormat
    {
        Png,
        Tga,
        Jpg,
        Exr
    }
}