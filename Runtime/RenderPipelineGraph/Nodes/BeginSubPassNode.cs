using System;
using NodeGraph;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Setup/Begin Sub Pass")]
public partial class BeginSubPassNode : RenderPipelineNode
{
    [SerializeField] private bool isDepthReadOnly = false;
    [SerializeField] private bool isStencilReadOnly = false;

    [SerializeField] private int[] colors = Array.Empty<int>();
    [SerializeField] private int[] inputs = Array.Empty<int>();
    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var colors = new NativeArray<int>(this.colors, Allocator.Temp);
        using var inputs = new NativeArray<int>(this.inputs, Allocator.Temp);
        context.BeginSubPass(colors, inputs, isDepthReadOnly, isStencilReadOnly);
    }
}
