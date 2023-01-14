using System;
using UnityEngine;

[Serializable]
public class DefaultPipelineShaders
{
    [SerializeField]
    private Shader autodeskInteractiveMaskedShader = null;

    [SerializeField]
    private Shader autodeskInteractiveShader = null;

    [SerializeField]
    private Shader autodeskInteractiveTransparentShader = null;

    [SerializeField]
    private Shader defaultSpeedTree7Shader = null;

    [SerializeField]
    private Shader defaultSpeedTree8Shader = null;

    [SerializeField]
    private Shader defaultShader = null;

    [SerializeField]
    private Shader terrainDetailGrassBillboardShader = null;

    [SerializeField]
    private Shader terrainDetailGrassShader = null;

    [SerializeField]
    private Shader terrainDetailLitShader = null;

    public Shader AutodeskInteractiveMaskedShader => autodeskInteractiveMaskedShader;
    public Shader AutodeskInteractiveShader => autodeskInteractiveShader;
    public Shader AutodeskInteractiveTransparentShader => autodeskInteractiveTransparentShader;
    public Shader DefaultSpeedTree7Shader => defaultSpeedTree7Shader;
    public Shader DefaultSpeedTree8Shader => defaultSpeedTree8Shader;
    public Shader DefaultShader => defaultShader;
    public Shader TerrainDetailGrassBillboardShader => terrainDetailGrassBillboardShader;
    public Shader TerrainDetailGrassShader => terrainDetailGrassShader;
    public Shader TerrainDetailLitShader => terrainDetailLitShader;
}