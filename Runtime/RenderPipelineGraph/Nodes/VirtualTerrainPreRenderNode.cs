using NodeGraph;
using UnityEngine;

[NodeMenuItem("Rendering/Terrain/Virtual Terrain Pre")]
public partial class VirtualTerrainPreRenderNode : RenderPipelineNode
{
    [SerializeField] private int tileSize = 256;
    [SerializeField] private int virtualResolution = 524288;

    [Output] private ComputeBuffer feedbackBuffer;

    private int IndirectionTextureResolution => virtualResolution / tileSize;

    public override void Initialize()
    {
        // Request size is res * res * 1/3rd
        var requestSize = IndirectionTextureResolution * IndirectionTextureResolution * 4 / 3;
        feedbackBuffer = new ComputeBuffer(requestSize, 4);
        feedbackBuffer.SetData(new int[requestSize]);
    }

    public override void Cleanup()
    {
        feedbackBuffer?.Dispose();
    }
}
