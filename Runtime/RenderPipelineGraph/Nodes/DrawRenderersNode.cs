using System;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

[NodeMenuItem("Core/Draw Renderers")]
public partial class DrawRenderersNode : RenderPipelineNode
{
    [SerializeField] private string[] shaderTagIds = new string[0];

    [SerializeField] private bool excludeMotionVectorObjects = false;
    [SerializeField] private LayerMask layerMask = ~0;

    [SerializeField] private PerObjectData perObjectData = PerObjectData.None;
    [SerializeField] private SortingCriteria sortingCriteria = SortingCriteria.None;
    [SerializeField] private RenderQueue renderQueue = RenderQueue.Opaque;

    [Input] private CullingResults cullingResults;
    [Input] private RenderStateBlock renderStateBlock;
    [Input, Output] private NodeConnection connection;

    private ShaderTagId[] shaderTagIdsInternal = Array.Empty<ShaderTagId>();

    private string profilerText;

    public override void Initialize()
    {
        profilerText = $"Draw Renderers ({string.Join(", ", shaderTagIds)})";
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        Array.Resize(ref shaderTagIdsInternal, shaderTagIds.Length);
        for (var i = 0; i < shaderTagIds.Length; i++)
            shaderTagIdsInternal[i] = new ShaderTagId(shaderTagIds[i]);

        RenderQueueRange renderQueueRange;
        switch (renderQueue)
        {
            case RenderQueue.Opaque:
                renderQueueRange = RenderQueueRange.opaque;
                break;
            case RenderQueue.Transparent:
                renderQueueRange = RenderQueueRange.transparent;
                break;
            default:
                renderQueueRange = RenderQueueRange.all;
                break;
        }

        var desc = new RendererListDesc(shaderTagIdsInternal, cullingResults, camera)
        {
            excludeObjectMotionVectors = excludeMotionVectorObjects,
            layerMask = layerMask,
            rendererConfiguration = perObjectData,
            renderQueueRange = renderQueueRange,
            sortingCriteria = sortingCriteria,
        };

        if (NodeIsConnected("renderStateBlock"))
            desc.stateBlock = renderStateBlock;

        var rendererList = context.CreateRendererList(desc);

        using var scope = context.ScopedCommandBuffer(profilerText, true);
        scope.Command.DrawRendererList(rendererList);
    }
}
