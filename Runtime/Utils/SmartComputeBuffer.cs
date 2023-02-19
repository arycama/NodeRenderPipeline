using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

public abstract class SmartComputeBuffer
{
    private readonly ComputeBufferType type;
    public ComputeBuffer ComputeBuffer { get; private set; }

    /// <summary>
    /// Stores how many elements are currently contained
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Stores the max amount of elements that can be added without resizing
    /// </summary>
    public int Capacity { get; private set; } = 1;

    /// <summary>
    /// Size of an element in the buffer
    /// </summary>
    public int Stride { get; }

    public SmartComputeBuffer(int stride, ComputeBufferType type = ComputeBufferType.Default)
    {
        this.Stride = stride;
        this.type = type;
    }

    ~SmartComputeBuffer()
    {
        ComputeBuffer?.Dispose();
    }

    public void EnsureCapcity(int length)
    {
        Count = length;
        Capacity = Mathf.Max(Capacity, Count);

        if (ComputeBuffer == null || ComputeBuffer.count < Capacity)
        {
            if (ComputeBuffer != null)
                ComputeBuffer.Release();

            ComputeBuffer = new ComputeBuffer(Capacity, Stride, type);
        }
    }

    public void SetGlobalBuffer(CommandBuffer command, string name) => command.SetGlobalBuffer(name, ComputeBuffer);

    public void SetGlobalBuffer(CommandBuffer command, int nameId) => command.SetGlobalBuffer(nameId, ComputeBuffer);

    public void SetComputeBufferParam(CommandBuffer command, ComputeShader computeShader, int kernelIndex, string name)
    {
        command.SetComputeBufferParam(computeShader, kernelIndex, name, ComputeBuffer);
    }

    public void SetBuffer(MaterialPropertyBlock propertyBlock, string name)
    {
        propertyBlock.SetBuffer(name, ComputeBuffer);
    }

    // TODO: Could make a specialized indirectArgsBuffer type instead?
    public void DrawMeshInstancedIndirect(CommandBuffer command, Mesh mesh, int submeshIndex, Material material, int shaderPass, int argsOffset, MaterialPropertyBlock properties)
    {
        command.DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, ComputeBuffer, argsOffset, properties);
    }

    // TODO: Could make a specialized indirectArgsBuffer type instead?
    public void DrawProceduralIndirect(CommandBuffer command, GraphicsBuffer indexBuffer, Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int argsOffset, MaterialPropertyBlock properties)
    {
        command.DrawProceduralIndirect(indexBuffer, matrix, material, shaderPass, topology, ComputeBuffer, argsOffset, properties);
    }

    // TODO: Could make a specialized indirectArgsBuffer type instead?
    public void DispatchCompute(CommandBuffer command, ComputeShader computeShader, int kernelIndex, uint argsOffset)
    {
        command.DispatchCompute(computeShader, kernelIndex, ComputeBuffer, argsOffset);
    }
}

/// <summary>
/// Wrapper for a ComputeBuffer that is strongly typed, and has an internal ComputeBuffer that can be dynamically resized if needed
/// </summary>
/// <typeparam name="T"></typeparam>
public class SmartComputeBuffer<T> : SmartComputeBuffer where T : struct
{
    public SmartComputeBuffer(ComputeBufferType type = ComputeBufferType.Default) : base(UnsafeUtility.SizeOf<T>(), type) { }

    public void SetData(CommandBuffer command, List<T> data)
    {
        EnsureCapcity(data.Count);
        command.SetBufferData(ComputeBuffer, data);
    }

    public void SetData(CommandBuffer command, NativeArray<T> data)
    {
        EnsureCapcity(data.Length);
        command.SetBufferData(ComputeBuffer, data);
    }

    public void SetData(CommandBuffer command, T[] data)
    {
        EnsureCapcity(data.Length);
        command.SetBufferData(ComputeBuffer, data);
    }
}

public static partial class CommandBufferExtensions
{
    public static void SetBufferData<T>(this CommandBuffer command, SmartComputeBuffer<T> dynamicComputeBuffer, T[] data) where T : struct
    {
        dynamicComputeBuffer.SetData(command, data);
    }

    public static void SetBufferData<T>(this CommandBuffer command, SmartComputeBuffer<T> dynamicComputeBuffer, List<T> data) where T : struct
    {
        dynamicComputeBuffer.SetData(command, data);
    }

    public static void SetBufferData<T>(this CommandBuffer command, SmartComputeBuffer<T> dynamicComputeBuffer, NativeArray<T> data) where T : struct
    {
        dynamicComputeBuffer.SetData(command, data);
    }

    public static void SetGlobalBuffer(this CommandBuffer command, string name, SmartComputeBuffer dynamicComputeBuffer)
    {
        dynamicComputeBuffer.SetGlobalBuffer(command, name);
    }

    public static void SetGlobalBuffer(this CommandBuffer command, int nameId, SmartComputeBuffer dynamicComputeBuffer)
    {
        dynamicComputeBuffer.SetGlobalBuffer(command, nameId);
    }

    public static void SetComputeBufferParam(this CommandBuffer command, ComputeShader computeShader, int kernelIndex, string name, SmartComputeBuffer dynamicComputeBuffer)
    {
        dynamicComputeBuffer.SetComputeBufferParam(command, computeShader, kernelIndex, name);
    }

    public static void DrawMeshInstancedIndirect(this CommandBuffer command, Mesh mesh, int submeshIndex, Material material, int shaderPass, SmartComputeBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties)
    {
        bufferWithArgs.DrawMeshInstancedIndirect(command, mesh, submeshIndex, material, shaderPass, argsOffset, properties);
    }

    public static void DrawProceduralIndirect(this CommandBuffer command, GraphicsBuffer indexBuffer, Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, SmartComputeBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties)
    {
        bufferWithArgs.DrawProceduralIndirect(command, indexBuffer, matrix, material, shaderPass, topology, argsOffset, properties);
    }

    public static void DispatchCompute(this CommandBuffer command, ComputeShader computeShader, int kernelIndex, SmartComputeBuffer bufferWithArgs, uint argsOffset)
    {
        bufferWithArgs.DispatchCompute(command, computeShader, kernelIndex, argsOffset);
    }
}

public static partial class MaterialPropertyBlockExtensions
{
    public static void SetBuffer(this MaterialPropertyBlock materialPropertyBlock, string name, SmartComputeBuffer smartComputeBuffer)
    {
        smartComputeBuffer.SetBuffer(materialPropertyBlock, name);
    }
}