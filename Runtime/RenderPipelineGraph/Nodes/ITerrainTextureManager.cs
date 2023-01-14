using UnityEngine;
using UnityEngine.Rendering;

public interface ITerrainTextureManager
{
    void SetShaderProperties(CommandBuffer command, ComputeShader material);
}