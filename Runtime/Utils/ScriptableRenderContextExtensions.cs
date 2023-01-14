using UnityEngine.Rendering;

public static class ScriptableRenderContextExtensions
{
    public static ScopedCommandBuffer ScopedCommandBuffer(this ScriptableRenderContext context, string name = null, bool useProfiler = false, bool isAsync = false, ComputeQueueType computeQueueType = ComputeQueueType.Default)
    {
        return new ScopedCommandBuffer(context, name, useProfiler, isAsync, computeQueueType);
    }
}