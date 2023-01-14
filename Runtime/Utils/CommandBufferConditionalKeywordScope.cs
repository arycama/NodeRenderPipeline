using System;
using UnityEngine.Rendering;

public readonly struct CommandBufferConditionalKeywordScope : IDisposable
{
    private readonly CommandBuffer commandBuffer;
    private readonly string keyword;
    private readonly bool isEnabled;

    public CommandBufferConditionalKeywordScope(CommandBuffer commandBuffer, string keyword, bool isEnabled)
    {
        this.commandBuffer = commandBuffer;
        this.keyword = keyword;
        this.isEnabled = isEnabled;
        commandBuffer.EnableShaderKeywordConditional(keyword, isEnabled);
    }

    void IDisposable.Dispose()
    {
        commandBuffer.DisableShaderKeywordConditional(keyword, isEnabled);
    }
}