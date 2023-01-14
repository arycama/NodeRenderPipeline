using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Data/Texture Graph")]
public class TextureGraph : NodeGraph.NodeGraph
{
    [SerializeField] private Vector3Int resolution = new(128, 128, 1);

    [SerializeField] private RenderTextureFormat format = RenderTextureFormat.ARGB32;

    [SerializeField] private RenderTextureReadWrite readWrite = RenderTextureReadWrite.Default;

    public RenderTexture Result { get; private set; }

    public override Type NodeType => typeof(TextureNode);

    public void Initialize()
    {
        Result = new RenderTexture(resolution.x, resolution.y, 0, format, readWrite)
        {
            dimension = resolution.z == 1 ? TextureDimension.Tex2D : TextureDimension.Tex3D,
            enableRandomWrite = true,
            hideFlags = HideFlags.HideAndDontSave,
            volumeDepth = resolution.z
        }.Created();

        foreach (var node in Nodes)
        {
            node.Initialize();
        }
    }

    public void Cleanup()
    {
        DestroyImmediate(Result);

        foreach (var node in Nodes)
        {
            node.Cleanup();
        }
    }

    public void Run(CommandBuffer command)
    {
        // Temp workaround for when the active graph is changed, as it doesn't re-initialize
        if (Result == null)
        {
            Result = new RenderTexture(resolution.x, resolution.y, 0, format, readWrite)
            {
                dimension = resolution.z == 1 ? TextureDimension.Tex2D : TextureDimension.Tex3D,
                enableRandomWrite = true,
                hideFlags = HideFlags.HideAndDontSave,
                volumeDepth = resolution.z
            }.Created();
        }

        Result.Resize(resolution.x, resolution.y, resolution.z);

        if (Result.format != format)
        {
            Result.Release();
            Result.format = format;
            Result.Create();
        }

        UpdateNodeOrder();

        foreach (var node in nodesToProcess)
        {
            node.UpdateValues();

            if (node is TextureNode textureNode)
                textureNode.Process(resolution, command);
        }

        foreach (var node in Nodes)
        {
            if (node is TextureOutputNode textureOutputNode)
            {
                textureOutputNode.GetResult(Result, command, resolution);
                break;
            }
        }

        foreach (var node in Nodes)
        {
            if (node is TextureNode textureNode)
                textureNode.FinishProcessing(command);
        }
    }
}