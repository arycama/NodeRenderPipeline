using System;
using UnityEngine.Rendering;

public struct ScopedCommandBuffer : IDisposable
{
    private ScriptableRenderContext context;
    private readonly bool useProfiler;
    private readonly bool isAsync;
    private readonly string name;
    private readonly ComputeQueueType computeQueueType;

    public CommandBuffer Command { get; }

    public ScopedCommandBuffer(ScriptableRenderContext context, string name = null, bool useProfiler = false, bool isAsync = false, ComputeQueueType computeQueueType = ComputeQueueType.Default)
    {
        this.context = context;
        this.name = name;
        this.useProfiler = useProfiler;
        this.isAsync = isAsync;
        this.computeQueueType = computeQueueType;

        // Only use name if we're not profiling, otherwise it doubles up in the FrameDebugger
        Command = CommandBufferPool.Get(name);

        if (useProfiler)
        {
            Command.BeginSample(name);
            context.ExecuteCommandBuffer(Command);
            Command.Clear();
        }

        if (isAsync)
            Command.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
    }

    void IDisposable.Dispose()
    {
        if (useProfiler)
            Command.EndSample(name);

        if (isAsync)
            context.ExecuteCommandBufferAsync(Command, computeQueueType);
        else
            context.ExecuteCommandBuffer(Command);

        CommandBufferPool.Release(Command);
    }
}
