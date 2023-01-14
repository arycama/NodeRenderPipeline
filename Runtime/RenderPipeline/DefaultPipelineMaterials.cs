using System;
using UnityEngine;

[Serializable]
public class DefaultPipelineMaterials
{
    [SerializeField]
    private Material defaultMaterial = null;

    [SerializeField]
    private Material defaultUIMaterial = null;

    [SerializeField]
    private Material default2DMaterial = null;

    [SerializeField]
    private Material defaultLineMaterial = null;

    [SerializeField]
    private Material defaultParticleMaterial = null;

    [SerializeField]
    private Material defaultTerrainMaterial = null;

    [SerializeField]
    private Material defaultUIETC1SupportedMaterial = null;

    [SerializeField]
    private Material defaultUIOverdrawMaterial = null;

    public Material DefaultMaterial => defaultMaterial;
    public Material DefaultUIMaterial => defaultUIMaterial;
    public Material Default2DMaterial => default2DMaterial;
    public Material DefaultLineMaterial => defaultLineMaterial;
    public Material DefaultParticleMaterial => defaultParticleMaterial;
    public Material DefaultTerrainMaterial => defaultTerrainMaterial;
    public Material DefaultUIETC1SupportedMaterial => defaultUIETC1SupportedMaterial;
    public Material DefaultUIOverdrawMaterial => defaultUIOverdrawMaterial;
}