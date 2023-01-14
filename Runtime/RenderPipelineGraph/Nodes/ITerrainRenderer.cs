using UnityEngine.Rendering;

interface ITerrainRenderer
{
    RenderTargetIdentifier NormalMap { get; }
}