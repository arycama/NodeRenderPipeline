using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Setup/Shadow Drawing Settings")]
public partial class ShadowDrawingSettingsNode : RenderPipelineNode
{
    [SerializeField] private ShadowObjectsFilter objectsFilter = ShadowObjectsFilter.AllObjects;
    [SerializeField] private bool useRenderingLayerMaksTest = false;

    [Input] private CullingResults cullingResults;
    [Input] private int lightIndex;
    [Input] private ShadowSplitData splitData;

    [Output] private ShadowDrawingSettings shadowDrawingSettings;

    [Input, Output] private NodeConnection connection;

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, lightIndex)
        {
            objectsFilter = objectsFilter,
            splitData = splitData,
            useRenderingLayerMaskTest = useRenderingLayerMaksTest,
        };
    }
}

