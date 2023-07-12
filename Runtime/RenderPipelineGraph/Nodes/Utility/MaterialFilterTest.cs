using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class MaterialFilterTest : MonoBehaviour
{
    [SerializeField]
    private int maxIterations = 32;

    [SerializeField, Pow2(256)]
    private int resolution = 256;

    public AnimationCurve curve = new();

    private RenderTexture table;

    void OnEnable()
    {
        table = new RenderTexture(resolution, 1, 0, RenderTextureFormat.RFloat)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point
        }.Created();
    }

    private void OnDisable()
    {
        DestroyImmediate(table);
    }

    void Update()
    {
        // First pass will shorten normal based on the average normal length from the smoothness
        var computeShader = Resources.Load<ComputeShader>("Utility/SmoothnessFilter");
        var generateLengthToSmoothnessKernel = computeShader.FindKernel("GenerateLengthToSmoothness");
        computeShader.SetFloat("_MaxIterations", maxIterations);
        computeShader.SetFloat("_Resolution", resolution);
        computeShader.SetTexture(generateLengthToSmoothnessKernel, "_LengthToRoughnessResult", table);
        computeShader.DispatchNormalized(generateLengthToSmoothnessKernel, resolution, 1, 1);

        GetComponent<Renderer>().sharedMaterial.mainTexture = table;

        var normalRequest = AsyncGPUReadback.Request(table);
        normalRequest.WaitForCompletion();
        var normalData = normalRequest.GetData<float>();

        var keys = new Keyframe[resolution];
        for (var i = 0; i < resolution; i++)
        {
            var data = normalData[i];
            keys[i] = new Keyframe(i / (resolution - 1f), data);
        }

        for (var i = 0; i < keys.Length; i++)
        {
            var key = keys[i];

            if (i == 0)
            {
                key.inTangent = 0f;
            }
            else
            {
                var previous = keys[i - 1];
                key.inTangent = (key.value - previous.value) / (key.time - previous.time);
            }

            if (i == keys.Length - 1)
            {
                key.outTangent = 0f;
            }
            else
            {
                var next = keys[i + 1];
                key.outTangent = (next.value - key.value) / (next.time - key.time);
            }

            keys[i] = key;
        }

        curve.keys = keys;
    }
}