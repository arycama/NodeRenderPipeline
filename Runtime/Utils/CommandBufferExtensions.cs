using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class CommandBufferExtensions
{
    public static void DispatchNormalized(this CommandBuffer commandBuffer, ComputeShader computeShader, int kernelIndex, int threadsX, int threadsY, int threadsZ)
    {
        computeShader.GetKernelThreadGroupSizes(kernelIndex, out var x, out var y, out var z);

        var threadGroupsX = (int)((threadsX - 1) / x) + 1;
        var threadGroupsY = (int)((threadsY - 1) / y) + 1;
        var threadGroupsZ = (int)((threadsZ - 1) / z) + 1;

        commandBuffer.DispatchCompute(computeShader, kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);
    }

    public static void ToggleKeyword(this CommandBuffer commandBuffer, string keyword, bool isEnabled)
    {
        if (isEnabled) commandBuffer.EnableShaderKeyword(keyword);
        else commandBuffer.DisableShaderKeyword(keyword);
    }

    public static CommandBufferProfilerScope ProfilerScope(this CommandBuffer commandBuffer, string name)
    {
        return new CommandBufferProfilerScope(commandBuffer, name);
    }

    public static void EnableShaderKeywordConditional(this CommandBuffer commandBuffer, string keyword, bool enable)
    {
        if (enable)
            commandBuffer.EnableShaderKeyword(keyword);
    }

    public static void DisableShaderKeywordConditional(this CommandBuffer commandBuffer, string keyword, bool disable)
    {
        if (disable)
            commandBuffer.DisableShaderKeyword(keyword);
    }

    public static CommandBufferKeywordScope KeywordScope(this CommandBuffer commandBuffer, string keyword)
    {
        return new CommandBufferKeywordScope(commandBuffer, keyword);
    }

    public static CommandBufferConditionalKeywordScope KeywordScope(this CommandBuffer commandBuffer, string keyword, bool isEnabled)
    {
        return new CommandBufferConditionalKeywordScope(commandBuffer, keyword, isEnabled);
    }

    public static void ExpandAndSetComputeBufferData<T>(this CommandBuffer command, ref ComputeBuffer computeBuffer, List<T> data, ComputeBufferType type = ComputeBufferType.Default) where T : struct
    {
        var size = Mathf.Max(data.Count, 1);

        if (computeBuffer == null || computeBuffer.count < size)
        {
            if (computeBuffer != null)
                computeBuffer.Release();

            var stride = UnsafeUtility.SizeOf<T>();
            computeBuffer = new ComputeBuffer(size, stride, type);
        }

        command.SetBufferData(computeBuffer, data);
    }

    public static void ExpandAndSetComputeBufferData<T>(this CommandBuffer command, ref ComputeBuffer computeBuffer, NativeArray<T> data, ComputeBufferType type = ComputeBufferType.Default) where T : struct
    {
        var size = Mathf.Max(data.Length, 1);

        if (computeBuffer == null || computeBuffer.count < size)
        {
            if (computeBuffer != null)
                computeBuffer.Release();

            var stride = UnsafeUtility.SizeOf<T>();
            computeBuffer = new ComputeBuffer(size, stride, type);
        }

        command.SetBufferData(computeBuffer, data);
    }
}
