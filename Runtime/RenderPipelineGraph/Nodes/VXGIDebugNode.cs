using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Lighting/VXGI Debug")]
public partial class VXGIDebugNode : RenderPipelineNode
{
    [SerializeField] private bool enabled;
    [SerializeField] private bool raymarch = false;
    [SerializeField] private bool showOpacity = false;
    [SerializeField] private float distance = 1024f;
    [SerializeField, Min(0)] private float opacity = 0f;
    [SerializeField] private int steps = 64;
    [SerializeField] private Material debugMaterial = null;
    [SerializeField] private Mesh debugMesh = null;

    [Input] private RenderTargetIdentifier opacityVolume;
    [Input] private int resolution;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        //if (!enabled)
        //    return;

        //using var scope = context.ScopedCommandBuffer("VXGI Process");

        //if (raymarch)
        //{
        //    var computeShader = Resources.Load<ComputeShader>("Lighting/VoxelGI");
        //    var debugKernel = computeShader.FindKernel("Debug");

        //    //scope.Command.SetComputeTextureParam(computeShader, debugKernel, "_Input", opacityVolume);
        //    scope.Command.SetComputeTextureParam(computeShader, debugKernel, "_DebugResult", attachment.array[0].loadStoreTarget);
        //    scope.Command.SetComputeFloatParam(computeShader, "_Opacity", opacity / distance);
        //    scope.Command.SetComputeFloatParam(computeShader, "_Steps", steps);
        //    scope.Command.SetComputeFloatParam(computeShader, "_Range", distance);
        //    scope.Command.DispatchNormalized(computeShader, debugKernel, camera.pixelWidth, camera.pixelHeight, 1);
        //}
        //else
        //{
        //    scope.Command.SetGlobalFloat("_VoxelShowOpacity", showOpacity ? 1f : 0f);
        //    //scope.Command.SetGlobalTexture("_VoxelOpacity", opacityVolume);
        //    scope.Command.SetRenderTarget(attachment.array[0].loadStoreTarget);
        //    scope.Command.DrawMeshInstancedProcedural(debugMesh, 0, debugMaterial, 0, resolution * resolution * resolution);
        //}
    }
}