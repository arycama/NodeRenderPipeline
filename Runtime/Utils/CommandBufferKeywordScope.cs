using System;
using UnityEngine.Rendering;

public readonly struct CommandBufferKeywordScope : IDisposable
{
    private readonly CommandBuffer commandBuffer;
    private readonly string keyword;

    public CommandBufferKeywordScope(CommandBuffer commandBuffer, string keyword)
    {
        this.commandBuffer = commandBuffer;
        this.keyword = keyword;
        commandBuffer.EnableShaderKeyword(keyword);
    }

    void IDisposable.Dispose()
    {
        commandBuffer.DisableShaderKeyword(keyword);
    }
}
