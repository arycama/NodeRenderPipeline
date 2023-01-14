using NodeGraph;
using UnityEngine;
using UnityEngine.Rendering;

public abstract class RenderPipelineNode : BaseNode
{
    /// <summary>
    /// Count of current frame
    /// </summary>
    public int FrameCount { get; set; }

    /// <summary>
    /// Called once per frame, per camera. Execute rendering commands by using context.ExecuteCommandBuffer
    /// </summary>
    /// <param name="context"></param>
    /// <param name="camera"></param>
    public virtual void Execute(ScriptableRenderContext context, Camera camera) { }

    /// <summary>
    /// Called once per frame, per camera after all rendering is completed. Use this to release temporary resources
    /// (Eg renderTextures) that you only wanted to persist for the current frame.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="camera"></param>
    public virtual void FinishRendering(ScriptableRenderContext context, Camera camera) { }

    /// <summary>
    /// Called once per frame, after the entire frame has rendered and been submitted.
    /// </summary>
    public virtual void FrameRenderComplete() { }

    protected int GetShaderPropertyId(string id = "")
    {
        return string.IsNullOrEmpty(id)
            ? Shader.PropertyToID($"{GetType()}_{GetInstanceID()}")
            : Shader.PropertyToID($"{GetType()}_{id}_{GetInstanceID()}");
    }
}