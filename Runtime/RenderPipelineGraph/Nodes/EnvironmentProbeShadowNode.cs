using System.Collections;
using System.Collections.Generic;
using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

public partial class EnvironmentProbeShadowNode : RenderPipelineNode
{
    [SerializeField, Pow2(4096)] private int resolution = 512;
    [SerializeField] private float directionalBias = 1f;

    [Input] private CullingResults cullingResults;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Reflection Probe Shadows", true);
    }
}
