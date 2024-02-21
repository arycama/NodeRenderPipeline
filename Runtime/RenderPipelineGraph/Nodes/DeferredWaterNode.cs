using NodeGraph;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Deferred Water")]
public partial class DeferredWaterNode : RenderPipelineNode
{
    private static readonly IndexedString noiseIds = new("STBN/Vec2/stbn_vec2_2Dx1D_128x128x64_");

    [SerializeField] private Material material;
    [SerializeField] private AtmosphereProfile atmosphere;

    [Input] private CullingResults cullingResults;
    [Input] private RenderTargetIdentifier underwaterLighting;

    [Input] private RenderTargetIdentifier waterNormalMask;
    [Input] private RenderTargetIdentifier waterRoughnessAlbedo;
    [Input] private RenderTargetIdentifier depth;
    [Input] private RenderTargetIdentifier underwaterDepth;
    [Input] private RenderTargetIdentifier waterShadow;
    [Input] private RenderTargetIdentifier exposure;

    [Input] private RenderTargetIdentifier gBuffer0;
    [Input] private RenderTargetIdentifier gBuffer1;
    [Input] private RenderTargetIdentifier gBuffer2;
    [Input] private RenderTargetIdentifier gBuffer3;
    [Input] private RenderTargetIdentifier gBuffer4;
    [Input, Output] private NodeConnection connection;

    private Material renderMaterial;

    public override void Initialize()
    {
        renderMaterial = new Material(Shader.Find("Hidden/Deferred Water")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public override void Cleanup()
    {
        DestroyImmediate(renderMaterial);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        var propertyBlock = GenericPool<MaterialPropertyBlock>.Get();
        propertyBlock.Clear();

        propertyBlock.SetVector("_Color", material.GetColor("_Color").linear);
        propertyBlock.SetVector("_Extinction", material.GetColor("_Extinction").linear);

        propertyBlock.SetFloat("_RefractOffset", material.GetFloat("_RefractOffset"));
        propertyBlock.SetFloat("_Steps", material.GetFloat("_Steps"));

        var radius = atmosphere == null ? 0f : atmosphere.PlanetRadius;
        propertyBlock.SetVector("_PlanetOffset", new Vector3(0f, radius + camera.transform.position.y, 0f));

        var gBuffer0 = new AttachmentDescriptor(GraphicsFormat.R8G8B8A8_UNorm);
        var gBuffer1 = new AttachmentDescriptor(GraphicsFormat.R8G8B8A8_UNorm);
        var gBuffer2 = new AttachmentDescriptor(GraphicsFormat.R8G8B8A8_UNorm);
        var gBuffer3 = new AttachmentDescriptor(GraphicsFormat.R8G8B8A8_UNorm);
        var gBuffer4 = new AttachmentDescriptor(GraphicsFormat.B10G11R11_UFloatPack32);
        var depth = new AttachmentDescriptor(GraphicsFormat.D32_SFloat_S8_UInt);

        gBuffer0.ConfigureTarget(this.gBuffer0, true, true);
        gBuffer1.ConfigureTarget(this.gBuffer1, true, true);
        gBuffer2.ConfigureTarget(this.gBuffer2, true, true);
        gBuffer3.ConfigureTarget(this.gBuffer3, true, true);
        gBuffer4.ConfigureTarget(this.gBuffer4, true, true);
        depth.ConfigureTarget(this.depth, true, true);

        var attachments = new NativeArray<AttachmentDescriptor>(6, Allocator.Temp);
        attachments[0] = depth;
        attachments[1] = gBuffer0;
        attachments[2] = gBuffer1;
        attachments[3] = gBuffer2;
        attachments[4] = gBuffer3;
        attachments[5] = gBuffer4;

        using (var renderPassScope = context.BeginScopedRenderPass(camera.pixelWidth, camera.pixelHeight, 1, attachments, 0))
        {
            attachments.Dispose();

            // Start the first subpass, GBuffer creation: render to albedo, specRough, normal and emission, no need to read any input attachments
            var gbufferColors = new NativeArray<int>(5, Allocator.Temp);
            gbufferColors[0] = 1;
            gbufferColors[1] = 2;
            gbufferColors[2] = 3;
            gbufferColors[3] = 4;
            gbufferColors[4] = 5;

            var inputs = new NativeArray<int>(1, Allocator.Temp);
            inputs[0] = 0;

            using (var subPassScope = context.BeginScopedSubPass(gbufferColors, inputs, true))
            {
                gbufferColors.Dispose();
                inputs.Dispose();

                using var scope = context.ScopedCommandBuffer("Deferred Water", true);

                scope.Command.SetGlobalTexture("_UnderwaterResult", underwaterLighting);
                scope.Command.SetGlobalTexture("_WaterNormalFoam", waterNormalMask);
                scope.Command.SetGlobalTexture("_WaterRoughnessMask", waterRoughnessAlbedo);
                scope.Command.SetGlobalTexture("_UnderwaterDepth", underwaterDepth);
                scope.Command.SetGlobalTexture("_WaterShadows", waterShadow);
                scope.Command.SetGlobalTexture("_Exposure", exposure);

                var blueNoise1D = Resources.Load<Texture2D>(noiseIds.GetString(FrameCount % 64));
                scope.Command.SetGlobalTexture("_BlueNoise2D", blueNoise1D);

                // Find first 2 directional lights
                var dirLightCount = 0;
                for (var i = 0; i < cullingResults.visibleLights.Length; i++)
                {
                    var light = cullingResults.visibleLights[i];
                    if (light.lightType != LightType.Directional)
                        continue;

                    dirLightCount++;

                    if (dirLightCount == 1)
                    {
                        propertyBlock.SetVector("_LightDirection0", -light.localToWorldMatrix.Forward());
                        propertyBlock.SetVector("_LightColor0", light.finalColor);
                    }
                    else if (dirLightCount == 2)
                    {
                        propertyBlock.SetVector("_LightDirection1", -light.localToWorldMatrix.Forward());
                        propertyBlock.SetVector("_LightColor1", light.finalColor);
                    }
                    else
                    {
                        // Only 2 lights supported
                        break;
                    }
                }

                var keyword = dirLightCount == 2 ? "LIGHT_COUNT_TWO" : (dirLightCount == 1 ? "LIGHT_COUNT_ONE" : string.Empty);
                using var keywordScope = scope.Command.KeywordScope(keyword);
                scope.Command.DrawProcedural(Matrix4x4.identity, renderMaterial, 0, MeshTopology.Triangles, 3, 1, propertyBlock);
            }
        }

        GenericPool<MaterialPropertyBlock>.Release(propertyBlock);
    }
}
