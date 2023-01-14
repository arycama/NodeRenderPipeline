using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/GPU Instanced Rendering")]
public partial class GpuInstancedRenderingNode : RenderPipelineNode
{
    [SerializeField] private string passName;
    [SerializeField] private int renderQueueMin = 0;
    [SerializeField] private int renderQueueMax = 2500;
    [SerializeField] private bool excludeMotionVectorObjects = false;

    [Input] private GpuInstanceBuffers gpuInstanceBuffers;
    [Input, Output] private NodeConnection nodeConnection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        if (gpuInstanceBuffers.readyInstanceData == null)
            return;

        if (gpuInstanceBuffers.rendererDrawCallData.TryGetValue(passName, out var drawList))
        {
            // Render instances
            using var propertyBlock = ScopedPooledObject<MaterialPropertyBlock>.Get();
            propertyBlock.Value.Clear();

            propertyBlock.Value.SetBuffer("_RendererInstanceIndexOffsets", gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer);
            propertyBlock.Value.SetBuffer("_VisibleRendererInstanceIndices", gpuInstanceBuffers.visibleRendererInstanceIndicesBuffer);

            propertyBlock.Value.SetBuffer("_InstancePositions", gpuInstanceBuffers.positionsBuffer);
            propertyBlock.Value.SetBuffer("_InstanceLodFades", gpuInstanceBuffers.lodFadesBuffer);

            using var scope = context.ScopedCommandBuffer("GPU Instanced Rendering", true);

            using var indirectRenderingScope = scope.Command.KeywordScope("INDIRECT_RENDERING");

            foreach (var draw in drawList)
            {
                if (draw.renderQueue < renderQueueMin || draw.renderQueue > renderQueueMax)
                    continue;

                propertyBlock.Value.SetInt("RendererOffset", draw.rendererOffset);
                propertyBlock.Value.SetVector("unity_WorldTransformParams", Vector4.one);
                propertyBlock.Value.SetMatrix("_LocalToWorld", draw.localToWorld);
                scope.Command.DrawMeshInstancedIndirect(draw.mesh, draw.submeshIndex, draw.material, draw.passIndex, gpuInstanceBuffers.drawCallArgsBuffer, draw.indirectArgsOffset, propertyBlock);
            }
        }
    }
}
