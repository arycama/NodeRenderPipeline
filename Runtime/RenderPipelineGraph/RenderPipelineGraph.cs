using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Data/Render Pipeline Graph")]
public class RenderPipelineGraph : NodeGraph.NodeGraph
{
    private readonly Dictionary<Camera, int> cameraFrameCounts = new();

    public override Type NodeType => typeof(RenderPipelineNode);

    public void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        foreach (var camera in cameras)
        {
            // Add camera to framecount if needed
            if (!cameraFrameCounts.TryGetValue(camera, out var frameCount))
            {
                frameCount = 0;
                cameraFrameCounts.Add(camera, frameCount);
            }

            // Update each node
            using (var scope = context.ScopedCommandBuffer("Render Camera", true))
            {
                // Update node order
                UpdateNodeOrder();

                foreach (var node in nodesToProcess)
                {
                    node.UpdateValues();

                    if (!(node is RenderPipelineNode renderNode))
                        continue;

                    renderNode.FrameCount = frameCount;
                    renderNode.Execute(context, camera);
                }

                // Cleanup nodes
                foreach (var node in nodesToProcess)
                {
                    if (node is RenderPipelineNode renderNode)
                    {
                        renderNode.FinishRendering(context, camera);
                    }
                }
            }

            // Increase the framecount for next frame
            cameraFrameCounts[camera]++;
        }

        context.Submit();

        // Cleanup nodes
        foreach (var node in nodesToProcess)
        {
            if (node is RenderPipelineNode renderNode)
            {
                renderNode.FrameRenderComplete();
            }
        }
    }

    public void Initialize()
    {
        foreach (var node in Nodes.OfType<RenderPipelineNode>())
        {
            node.Initialize();
        }
    }

    public void Cleanup()
    {
        foreach (var node in Nodes.OfType<RenderPipelineNode>())
        {
            node.Cleanup();
        }

        ConstantBuffer.ReleaseAll();
    }

    [ContextMenu("Test")]
    public void HideChildComponents()
    {
#if UNITY_EDITOR
        foreach (var node in Nodes)
        {
            node.hideFlags = HideFlags.HideInHierarchy;
            UnityEditor.EditorUtility.SetDirty(node);
        }

        UnityEditor.AssetDatabase.SaveAssets();
#endif
    }
}
