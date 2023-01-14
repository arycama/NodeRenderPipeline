using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Data/Render Pipeline Sub Graph")]
public class RenderPipelineSubGraph : NodeGraph.NodeGraph
{
    public override Type NodeType => typeof(RenderPipelineNode);

    private Dictionary<string, RelayWrapper> relayWrappers = new();

    public void Initialize()
    {
        foreach (var node in Nodes.OfType<RenderPipelineNode>())
            node.Initialize();
    }

    public void Cleanup()
    {
        foreach (var node in Nodes.OfType<RenderPipelineNode>())
            node.Cleanup();
    }

    public void AddRelayInput<T>(string name, T value)
    {
        RelayWrapper<T> typedWrapper;
        if (relayWrappers.TryGetValue(name, out var wrapper))
        {
            typedWrapper = wrapper as RelayWrapper<T>;
            typedWrapper.Value = value;
        }
        else
        {
            wrapper = new RelayWrapper<T>(value);
            relayWrappers.Add(name, wrapper);
            typedWrapper = wrapper as RelayWrapper<T>;
        }

        RelayNodes[name] = typedWrapper;
    }

    public void Render(ScriptableRenderContext context, Camera camera, int frameCount)
    {
        // Update node order
        UpdateNodeOrder();

        foreach (var node in nodesToProcess)
        {
            node.UpdateValues();

            if (node is RenderPipelineNode renderNode)
            {
                renderNode.FrameCount = frameCount;
                renderNode.Execute(context, camera);
            }
        }

        foreach (var node in nodesToProcess)
            if (node is RenderPipelineNode renderNode)
                renderNode.FinishRendering(context, camera);
    }

    public void FrameRenderComplete()
    {
        // Cleanup nodes
        foreach (var node in nodesToProcess)
            if (node is RenderPipelineNode renderNode)
                renderNode.FrameRenderComplete();
    }
}