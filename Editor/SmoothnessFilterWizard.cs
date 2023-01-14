using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class TextureFilterData
{
    public bool isNormal;
    public string textureGuid;

    public TextureFilterData(bool isNormal, string textureGuid)
    {
        this.isNormal = isNormal;
        this.textureGuid = textureGuid;
    }
}

public class SmoothnessFilterWizard : ScriptableWizard
{
    [SerializeField]
    private bool filterSmoothness = true;

    [SerializeField]
    private bool filterNormal = true;

    [SerializeField]
    private Texture2D smoothnessMap = null;

    [SerializeField]
    private Texture2D normalMap = null;

    [MenuItem("Tools/Textures/Smoothness Map Filter")]
    public static void OnMenuSelect()
    {
        DisplayWizard<SmoothnessFilterWizard>("Smoothness Map Filter", "Filter and Close", "Filter");
    }

    private void Run()
    {
        var normalPath = AssetDatabase.GetAssetPath(normalMap);
        var smoothnessPath = AssetDatabase.GetAssetPath(smoothnessMap);

        var normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
        var smoothnessImporter = AssetImporter.GetAtPath(smoothnessPath) as TextureImporter;

        var normalGuid = AssetDatabase.AssetPathToGUID(normalPath);
        var smoothnessGuid = AssetDatabase.AssetPathToGUID(smoothnessPath);

        smoothnessImporter.userData = filterSmoothness ? JsonUtility.ToJson(new TextureFilterData(false, normalGuid)) : null;
        smoothnessImporter.SaveAndReimport();

        normalImporter.userData = filterNormal ? JsonUtility.ToJson(new TextureFilterData(true, smoothnessGuid)) : null;
        normalImporter.SaveAndReimport();
    }

    private void OnWizardCreate()
    {
        Run();
    }

    private void OnWizardOtherButton()
    {
        Run();
    }
}

public class SmoothnessFilterImporter : AssetPostprocessor
{
    public void OnPostprocessTexture(Texture2D texture)
    {
        if (string.IsNullOrEmpty(assetImporter.userData))
            return;

        // Need to Apply the texture first so it is available for rendering
        texture.Apply();

        var data = JsonUtility.FromJson<TextureFilterData>(assetImporter.userData);
        var texturePath = AssetDatabase.GUIDToAssetPath(data.textureGuid);

        Texture2D normal, smoothness;
        if (data.isNormal)
        {
            normal = texture;
            smoothness = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        }
        else
        {
            smoothness = texture;
            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        }

        var width = Mathf.Max(normal.width, smoothness.width);
        var height = Mathf.Max(normal.height, smoothness.height);

        // First pass will shorten normal based on the average normal length from the smoothness
        var lengthToSmoothness = new RenderTexture(256, 1, 0, RenderTextureFormat.R16)
        {
            enableRandomWrite = true,
            hideFlags = HideFlags.HideAndDontSave,
            name = "Length to Smoothness",
        }.Created();

        var computeShader = Resources.Load<ComputeShader>("Utility/SmoothnessFilter");
        var generateLengthToSmoothnessKernel = computeShader.FindKernel("GenerateLengthToSmoothness");
        computeShader.SetFloat("_MaxIterations", 32);
        computeShader.SetFloat("_Resolution", 256);
        computeShader.SetTexture(generateLengthToSmoothnessKernel, "_LengthToRoughnessResult", lengthToSmoothness);
        computeShader.DispatchNormalized(generateLengthToSmoothnessKernel, 256, 1, 1);

        // Intermediate texture for normals, use full float precision
        var normalInput = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            useMipMap = true
        }.Created();

        // First pass will shorten normal based on the average normal length from the smoothness
        var shortenNormalKernel = computeShader.FindKernel("ShortenNormal");
        computeShader.SetFloat("_IsSrgb", 1f);
        computeShader.SetTexture(shortenNormalKernel, "_NormalInput", normal);
        computeShader.SetTexture(shortenNormalKernel, "_SmoothnessInput", smoothness);
        computeShader.SetTexture(shortenNormalKernel, "_NormalResult", normalInput);
        computeShader.DispatchNormalized(shortenNormalKernel, width, height, 1);

        // Generate mips for the intermediate normal texture, these will be weighted by the normal lengths
        normalInput.GenerateMips();

        // Textures to store the results, these will be copied into the final Texture2Ds
        var normalResult = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            useMipMap = true
        }.Created();

        // Smoothness texture might be sRGB
        var smoothnessResult = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            useMipMap = true
        }.Created();

        // For each mip (Except mip0 which is unchanged), convert the shortenedNormal (from normalInput) back to smoothness
        // Then normalize and re-pack the normal, and output the final smoothness value
        var mipNormalAndSmoothnessKernel = computeShader.FindKernel("MipNormalAndSmoothness");
        computeShader.SetTexture(mipNormalAndSmoothnessKernel, "_LengthToRoughness", lengthToSmoothness);

        var mipCount = texture.mipmapCount;
        for (var i = 1; i < mipCount; i++)
        {
            var mipWidth = width >> i;
            var mipHeight = height >> i;

            computeShader.SetInt("_Mip", i);
            computeShader.SetTexture(mipNormalAndSmoothnessKernel, "_NormalInput", normalInput);
            computeShader.SetTexture(mipNormalAndSmoothnessKernel, "_SmoothnessInput", smoothness);
            computeShader.SetTexture(mipNormalAndSmoothnessKernel, "_NormalResult", normalResult, i);
            computeShader.SetTexture(mipNormalAndSmoothnessKernel, "_SmoothnessResult", smoothnessResult, i);
            computeShader.DispatchNormalized(mipNormalAndSmoothnessKernel, mipWidth, mipHeight, 1);
        }

        var mips = normal.mipmapCount;
        for (var j = 1; j < mips; j++)
        {
            if (data.isNormal)
            {
                var normalRequest = AsyncGPUReadback.Request(normalResult, j);
                normalRequest.WaitForCompletion();
                var normalData = normalRequest.GetData<Color32>();
                texture.SetPixelData(normalData, j);
            }
            else
            {
                var smoothnessRequest = AsyncGPUReadback.Request(smoothnessResult, j);
                smoothnessRequest.WaitForCompletion();
                var smoothnessData = smoothnessRequest.GetData<Color32>();
                texture.SetPixelData(smoothnessData, j);
            }
        }

        Object.DestroyImmediate(normalInput);
        Object.DestroyImmediate(normalResult);
        Object.DestroyImmediate(smoothnessResult);
        Object.DestroyImmediate(lengthToSmoothness);
    }

    private void FilterSmoothness(string normalPath)
    {

    }

    private void FilterNormal(string smoothnessPath)
    {

    }
}