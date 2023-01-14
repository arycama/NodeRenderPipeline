using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Common/Blit")]
public partial class BlitNode : RenderPipelineNode
{
    private static readonly IndexedString noiseIds = new("STBN/Scalar/stbn_scalar_2Dx1Dx1D_128x128x64x1_");

    [Input] private RenderTargetIdentifier uiTarget;
    [Input] private RenderTargetIdentifier renderTarget;
    [Input] private RenderTargetIdentifier depth;
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var material = MaterialPool.Get("Hidden/Gamma Correction");

        var blueNoise1D = Resources.Load<Texture2D>(noiseIds.GetString(FrameCount % 64));

        using var scope = context.ScopedCommandBuffer("Blit to Screen", true);
        scope.Command.SetGlobalTexture("_UITarget", uiTarget);
        scope.Command.SetGlobalTexture("_BlueNoise1D", blueNoise1D);
        scope.Command.SetGlobalTexture("_Depth", depth);
        scope.Command.Blit(renderTarget, BuiltinRenderTextureType.CameraTarget, material, 0);
    }
}