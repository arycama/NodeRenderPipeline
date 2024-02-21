using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

[NodeMenuItem("Camera/Motion Vectors")]
public partial class CameraMotionVectorsNode : RenderPipelineNode
{
    [Input, Output] private NodeConnection connection;

    private Material material;

    public override void Initialize()
    {
        material = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public override void Cleanup()
    {
        DestroyImmediate(material);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Motion Vectors", true);
        scope.Command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
    }
}
