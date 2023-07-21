using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
public class GalaxyGenerator : MonoBehaviour
{
    [SerializeField]
    private int resolution = 512;

	[SerializeField]
	private int seed;

	[SerializeField]
	private bool randomSeed;

    [SerializeField]
    private Texture2D starTexture = null;

    [SerializeField]
    private Texture2D gradientTexture = null;

    [SerializeField]
    private int count = 10000;

    [SerializeField]
    private float minDistance = 1000;

    [SerializeField]
    private float maxDistance = 1000;

    [SerializeField]
    private float minRadius = 1;

    [SerializeField]
    private float maxRadius = 1;

    [SerializeField]
    private float minBrightness = 1f;

    [SerializeField]
    private float maxBrightness = 1f;

    [SerializeField]
    private Material previewMaterial;

    private RenderTexture target;

    private void OnEnable()
    {
        if (randomSeed)
        {
            seed = Random.Range(0, int.MaxValue);
        }

        var material = new Material(Shader.Find("Hidden/StarGPU"));
        material.mainTexture = starTexture;
        material.SetTexture("_Gradient", gradientTexture);
        material.SetInt("_Count", count);
        material.SetFloat("_MinDistance", minDistance);
        material.SetFloat("_MaxDistance", maxDistance);
        material.SetFloat("_MinBrightness", minBrightness);
        material.SetFloat("_MaxBrightness", maxBrightness);
        material.SetInt("_Seed", seed);
        material.SetFloat("_MinRadius", minRadius);
        material.SetFloat("_MaxRadius", maxRadius);
        material.SetPass(0);

        var projectionMatrix = Matrix4x4.Perspective(90, 1, 0.1f, Mathf.Sqrt(2) * maxDistance);

        target = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat)
        {
            dimension = TextureDimension.Cube,
            name = "Galaxy Generator Temp"
        };

        GL.PushMatrix();
        for (var face = CubemapFace.PositiveX; (int)face < 6; face++)
        {
            var up = CoreUtils.upVectorList[(int)face];
            var fwd = CoreUtils.lookAtList[(int)face];

            var viewToWorld = Matrix4x4.LookAt(Vector3.zero, fwd, up);
            viewToWorld.SetColumn(2, -viewToWorld.GetColumn(2));

            GL.LoadProjectionMatrix(projectionMatrix * viewToWorld);
            Graphics.SetRenderTarget(target, 0, face);
            GL.Clear(false, true, Color.clear);
            Graphics.DrawProceduralNow(MeshTopology.Points, count);
        }
        GL.PopMatrix();

        if (previewMaterial != null)
            previewMaterial.mainTexture = target;
    }

    private void OnDisable()
    {
        DestroyImmediate(target);
    }

    [ContextMenu("Save")]
	private void Save()
	{
		if (randomSeed)
		{
			seed = Random.Range(0, int.MaxValue);
		}

        var material = new Material(Shader.Find("Hidden/StarGPU"));
        material.mainTexture = starTexture;
        material.SetTexture("_Gradient", gradientTexture);
        material.SetInt("_Count", count);
        material.SetFloat("_MinDistance", minDistance);
        material.SetFloat("_MaxDistance", maxDistance);
        material.SetFloat("_MinBrightness", minBrightness);
        material.SetFloat("_MaxBrightness", maxBrightness);
        material.SetInt("_Seed", seed);
        material.SetFloat("_MinRadius", minRadius);
        material.SetFloat("_MaxRadius", maxRadius);
        material.SetPass(0);

        var projectionMatrix = Matrix4x4.Perspective(90, 1, 0.1f, Mathf.Sqrt(2) * maxDistance);

        var target = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat)
        {
            dimension = TextureDimension.Cube,
            name = "Galaxy Generator Temp"
        };

		GL.PushMatrix();
		for (var face = CubemapFace.PositiveX; (int)face < 6; face++)
		{
            var up = CoreUtils.upVectorList[(int)face];
            var fwd = CoreUtils.lookAtList[(int)face];

            var viewToWorld = Matrix4x4.LookAt(Vector3.zero, fwd, up);
            viewToWorld.SetColumn(2, -viewToWorld.GetColumn(2));

            GL.LoadProjectionMatrix(projectionMatrix * viewToWorld);
			Graphics.SetRenderTarget(target, 0, face);
			GL.Clear(false, true, Color.clear);
			Graphics.DrawProceduralNow(MeshTopology.Points, count);
		}
		GL.PopMatrix();

        var result = new Texture2D(resolution * 6, resolution, TextureFormat.RGBAFloat, false);

        for (var face = CubemapFace.PositiveX; (int)face < 6; face++)
        {
            Graphics.SetRenderTarget(target, 0, face);
            result.ReadPixels(new Rect(0, 0, resolution, resolution), resolution * (int)face, 0);
        }

        DestroyImmediate(target);

        var exrBytes = result.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat | Texture2D.EXRFlags.CompressZIP);
        File.WriteAllBytes($"{Application.dataPath}/Stars.exr", exrBytes);

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }
}