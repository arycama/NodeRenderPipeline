using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Data/Graphics/Render Pipeline Asset")]
public class CustomRenderPipelineAsset : RenderPipelineAsset
{
    [SerializeField] private bool useSRPBatcher = true;
    [SerializeField] private RenderPipelineGraph graph = null;
    [SerializeField] private DefaultPipelineMaterials defaultMaterials = new();
    [SerializeField] private DefaultPipelineShaders defaultShaders = new();
    [SerializeField] private string[] renderingLayerNames = new string[32];

    public bool UseSRPBatcher => useSRPBatcher;
    public RenderPipelineGraph Graph => graph;

    public override Material defaultMaterial => defaultMaterials.DefaultMaterial ?? base.defaultMaterial;
    public override Material defaultUIMaterial => defaultMaterials.DefaultUIMaterial ?? base.defaultUIMaterial;
    public override Material default2DMaterial => defaultMaterials.Default2DMaterial ?? base.default2DMaterial;
    public override Material defaultLineMaterial => defaultMaterials.DefaultLineMaterial ?? base.defaultLineMaterial;
    public override Material defaultParticleMaterial => defaultMaterials.DefaultParticleMaterial ?? base.defaultParticleMaterial;
    public override Material defaultTerrainMaterial => defaultMaterials.DefaultTerrainMaterial ?? base.defaultTerrainMaterial;
    public override Material defaultUIETC1SupportedMaterial => defaultMaterials.DefaultUIETC1SupportedMaterial ?? base.defaultUIETC1SupportedMaterial;
    public override Material defaultUIOverdrawMaterial => defaultMaterials.DefaultUIOverdrawMaterial ?? base.defaultUIOverdrawMaterial;

    public override Shader autodeskInteractiveMaskedShader => defaultShaders.AutodeskInteractiveMaskedShader ?? base.autodeskInteractiveMaskedShader;
    public override Shader autodeskInteractiveShader => defaultShaders.AutodeskInteractiveShader ?? base.autodeskInteractiveShader;
    public override Shader autodeskInteractiveTransparentShader => defaultShaders.AutodeskInteractiveTransparentShader ?? base.autodeskInteractiveTransparentShader;
    public override Shader defaultSpeedTree7Shader => defaultShaders.DefaultSpeedTree7Shader ?? base.defaultSpeedTree7Shader;
    public override Shader defaultSpeedTree8Shader => defaultShaders.DefaultSpeedTree8Shader ?? base.defaultSpeedTree8Shader;
    public override Shader defaultShader => defaultShaders.DefaultShader ?? base.defaultShader;
    public override Shader terrainDetailGrassBillboardShader => defaultShaders.TerrainDetailGrassBillboardShader ?? base.terrainDetailGrassBillboardShader;
    public override Shader terrainDetailGrassShader => defaultShaders.TerrainDetailGrassShader ?? base.terrainDetailGrassShader;
    public override Shader terrainDetailLitShader => defaultShaders.TerrainDetailLitShader ?? base.terrainDetailLitShader;
    public override string[] renderingLayerMaskNames => renderingLayerNames;
    public override string[] prefixedRenderingLayerMaskNames => renderingLayerNames;

    protected override RenderPipeline CreatePipeline()
    {
        return new CustomRenderPipeline(this);
    }
}