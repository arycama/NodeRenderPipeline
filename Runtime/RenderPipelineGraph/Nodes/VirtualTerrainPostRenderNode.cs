using System;
using System.Collections.Generic;
using System.Linq;
using NodeGraph;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

[NodeMenuItem("Rendering/Virtual Terrain Post")]
public partial class VirtualTerrainPostRenderNode : RenderPipelineNode
{
    private static List<VirtualTerrainPostRenderNode> activeNodes = new();

    [SerializeField, Pow2(512)] private int tileResolution = 256;
    [SerializeField, Pow2(524288)] private int virtualResolution = 524288;
    [SerializeField, Pow2(2048)] private int virtualTileCount = 512;
    [SerializeField, Range(1, 16)] private int anisoLevel = 4;
    [SerializeField, Pow2(32)] private int updateTileCount = 8;

    [Input] private ComputeBuffer feedbackBuffer;
    [Input, Output] private NodeConnection connection;

    private ComputeBuffer counterBuffer, requestBuffer, tilesToUnmapBuffer, mappedTiles;

    private SmartComputeBuffer<uint> tileRequestsBuffer, destPixelbuffer, dstOffsetsBuffer;
    private SmartComputeBuffer<Vector4> scaleOffsetsBuffer;

    private RenderTexture indirectionTexture, indirectionTextureMapTexture;
    private Texture2DArray albedoSmoothnessTexture, normalTexture, heightTexture;

    // Flattened 2D array storing a bool for each mapped tile
    [NonSerialized]
    private bool[] indirectionTexturePixels;

    // Need to track requests so we don't request the same page multiple times
    private readonly HashSet<int> pendingRequests = new();

    private bool needsClear;

    private readonly LruCache<int, int> lruCache = new();
    private int IndirectionTextureResolution => virtualResolution / tileResolution;
    private Terrain previousTerrain;

    private Action<AsyncGPUReadbackRequest> counterReadbackComplete, requestReadbackComplete;

    public override void Initialize()
    {
        activeNodes.Add(this);

        indirectionTexture = new RenderTexture(IndirectionTextureResolution, IndirectionTextureResolution, 0, GraphicsFormat.R16_UInt)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave,
            name = "Virtual IndirectTexture",
            useMipMap = true,
        }.Created();

        // Contains a simple 0 or 1 indicating if a pixel is mapped.
        indirectionTextureMapTexture = new RenderTexture(IndirectionTextureResolution, IndirectionTextureResolution, 0, GraphicsFormat.R8_UNorm)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave,
            name = "Virtual IndirectTexture",
            useMipMap = true,
        }.Created();

        albedoSmoothnessTexture = new Texture2DArray(tileResolution, tileResolution, virtualTileCount, TextureFormat.DXT5, 2, false)
        {
            anisoLevel = anisoLevel,
            filterMode = FilterMode.Trilinear,
            hideFlags = HideFlags.HideAndDontSave,
            name = "Virtual AlbedoSmoothness Texture",
            wrapMode = TextureWrapMode.Clamp,
        };

        normalTexture = new Texture2DArray(tileResolution, tileResolution, virtualTileCount, TextureFormat.DXT5, 2, true)
        {
            anisoLevel = anisoLevel,
            filterMode = FilterMode.Trilinear,
            hideFlags = HideFlags.HideAndDontSave,
            name = "Virtual Normal Texture",
            wrapMode = TextureWrapMode.Clamp,
        };

        heightTexture = new Texture2DArray(tileResolution, tileResolution, virtualTileCount, TextureFormat.BC4, 2, true)
        {
            anisoLevel = anisoLevel,
            filterMode = FilterMode.Trilinear,
            hideFlags = HideFlags.HideAndDontSave,
            name = "Virtual Height Texture",
            wrapMode = TextureWrapMode.Clamp,
        };

        indirectionTexturePixels = new bool[IndirectionTextureResolution * IndirectionTextureResolution * 4 / 3];

        // Request size is res * res * 1/3rd
        var requestSize = IndirectionTextureResolution * IndirectionTextureResolution * 4 / 3;

        requestBuffer = new ComputeBuffer(requestSize, 4, ComputeBufferType.Append);
        requestBuffer.SetData(new int[requestSize]);
        requestBuffer.SetCounterValue(0);

        counterBuffer = new ComputeBuffer(1, 4, ComputeBufferType.Raw);
        counterBuffer.SetData(new int[1]);

        // Buffer stuff
        mappedTiles = new ComputeBuffer(virtualTileCount, sizeof(int));
        tilesToUnmapBuffer = new ComputeBuffer(updateTileCount, sizeof(int));

        counterReadbackComplete = OnCounterReadbackComplete;
        requestReadbackComplete = OnRequestReadbackComplete;

        tileRequestsBuffer = new();
        destPixelbuffer = new();
        dstOffsetsBuffer = new();
        scaleOffsetsBuffer = new();
    }

    public override void Cleanup()
    {
        activeNodes.Remove(this);

        // Need to wait for requests to complete, or they will complain about not being able to access disposed arrays
        AsyncGPUReadback.WaitAllRequests();

        // Clear some lists. This is mostly for scene-view handling
        lruCache.Clear();

        GraphicsUtilities.SafeDestroy(ref requestBuffer);
        GraphicsUtilities.SafeDestroy(ref counterBuffer);
        GraphicsUtilities.SafeDestroy(ref tilesToUnmapBuffer);
        GraphicsUtilities.SafeDestroy(ref indirectionTexture);
        GraphicsUtilities.SafeDestroy(ref albedoSmoothnessTexture);
        GraphicsUtilities.SafeDestroy(ref normalTexture);
        GraphicsUtilities.SafeDestroy(ref heightTexture);
        GraphicsUtilities.SafeDestroy(ref mappedTiles);
        GraphicsUtilities.SafeDestroy(ref indirectionTextureMapTexture);
    }

    public override void Execute(ScriptableRenderContext context, Camera camera)
    {
        using var scope = context.ScopedCommandBuffer("Virtual Terrain");

        // Set shader globals
        scope.Command.SetGlobalTexture("_VirtualTexture", albedoSmoothnessTexture);
        scope.Command.SetGlobalTexture("_VirtualNormalTexture", normalTexture);
        scope.Command.SetGlobalTexture("_VirtualHeightTexture", heightTexture);
        scope.Command.SetGlobalTexture("_IndirectionTexture", indirectionTexture);

        // Pre-calculate some factors for looking up virtual uv coordinates
        scope.Command.SetGlobalFloat("_AnisoLevel", anisoLevel);
        scope.Command.SetGlobalFloat("_VirtualUvScale", virtualResolution);

        // Set up references
        var terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            // Used by tessellation to calculate lod
            var size = terrain.terrainData.size;
            var indTexSize = new Vector4(1f / size.x, 1f / size.z, size.x, size.z);
            scope.Command.SetGlobalVector("_IndirectionTexelSize", indTexSize);

            // If terrain is different, clear the LRU cache
            if (terrain != previousTerrain || needsClear)
            {
                Array.Clear(indirectionTexturePixels, 0, indirectionTexturePixels.Length);
                lruCache.Clear();

                // Also need to clear the gpu copy
                var virtualTextureUpdateShader = Resources.Load<ComputeShader>("VirtualTextureUpdate");
                scope.Command.SetComputeBufferParam(virtualTextureUpdateShader, 3, "MappedTiles", mappedTiles);
                scope.Command.DispatchNormalized(virtualTextureUpdateShader, 3, mappedTiles.count, 1, 1);

                for (var i = 0; i < indirectionTexture.mipmapCount; i++)
                {
                    var width = Texture2DExtensions.MipResolution(i, indirectionTexture.width);
                    var height = Texture2DExtensions.MipResolution(i, indirectionTexture.height);
                    scope.Command.SetComputeTextureParam(virtualTextureUpdateShader, 4, "DestMip", indirectionTexture);
                    scope.Command.DispatchNormalized(virtualTextureUpdateShader, 4, width, height, 1);

                    scope.Command.SetComputeTextureParam(virtualTextureUpdateShader, 4, "DestMip", indirectionTextureMapTexture);
                    scope.Command.DispatchNormalized(virtualTextureUpdateShader, 4, width, height, 1);
                }

                needsClear = false;
            }

            if (terrain != null && terrain.TryGetComponent<ITerrainTextureManager>(out var terrainTextureManager))
            {
                using var virtualTextureScope = scope.Command.ProfilerScope("Virtual Texturing");
                ProcessRequests(terrain, terrainTextureManager, scope.Command);
            }
        }

        previousTerrain = terrain;

        var reductionComputeShader = Resources.Load<ComputeShader>("VirtualTerrain");

        scope.Command.ClearRandomWriteTargets();
        scope.Command.SetBufferCounterValue(requestBuffer, 0);
        scope.Command.SetComputeBufferParam(reductionComputeShader, 0, "_VirtualFeedbackTexture", feedbackBuffer);
        scope.Command.SetComputeBufferParam(reductionComputeShader, 0, "_VirtualRequests", requestBuffer);
        scope.Command.DispatchNormalized(reductionComputeShader, 0, IndirectionTextureResolution * IndirectionTextureResolution * 4 / 3, 1, 1);
        scope.Command.CopyCounterValue(requestBuffer, counterBuffer, 0);
        scope.Command.RequestAsyncReadback(counterBuffer, counterReadbackComplete);
    }

    private void OnCounterReadbackComplete(AsyncGPUReadbackRequest request)
    {
        var size = request.GetData<int>()[0] * 4;
        if (size > 0)
        {
            AsyncGPUReadback.Request(requestBuffer, size, 0, requestReadbackComplete);
        }
    }

    private void OnRequestReadbackComplete(AsyncGPUReadbackRequest readbackRequest)
    {
        // For each tile request, attempt to queue it if not already cached, and not already pending
        if (readbackRequest.hasError)
        {
            return;
        }

        var mipCount = (int)Math.Log(IndirectionTextureResolution, 2);
        var data = readbackRequest.GetData<int>();
        foreach (var request in data)
        {
            var position = Texture2DExtensions.TextureByteOffsetToCoord(request, IndirectionTextureResolution);

            // We want to request the coarsest mip that is not yet rendered, to ensure there is a gradual transition to the
            // target mip, with 1 mip changing per frame. Do this by starting from current mip, and working to coarsest
            var previousIndex = request;
            for (var i = position.z; i <= mipCount; i++)
            {
                var index = Texture2DExtensions.TextureCoordToOffset(new Vector3Int(position.x, position.y, i), IndirectionTextureResolution);

                var indirectionTexturePixel = indirectionTexturePixels[index];
                if (indirectionTexturePixel)
                {
                    lruCache.Update(index);

                    // If this is not the targetMip, add the next coarsest mip
                    if (index != request)
                    {
                        pendingRequests.Add(previousIndex);
                    }

                    // Found a fallback mip, break
                    break;
                }
                else if (i == mipCount)
                {
                    // Most coarse mip, add
                    pendingRequests.Add(index);
                }
                else
                {
                    previousIndex = index;

                    position.x >>= 1;
                    position.y >>= 1;
                }
            }
        }
    }

    private void ProcessRequests(Terrain terrain, ITerrainTextureManager terrainTextureManager, CommandBuffer command)
    {
        if (pendingRequests.Count < 1)
        {
            return;
        }

        // Sort requests by mip, then distance from camera
        // TODO: Could do this on GPU before reading back.
        var sortedRequests = pendingRequests.OrderByDescending(rq => Texture2DExtensions.TextureByteOffsetToCoord(rq, IndirectionTextureResolution).z);
        var virtualTextureBuild = Resources.Load<ComputeShader>("VirtualTextureBuild");

        terrainTextureManager.SetShaderProperties(command, virtualTextureBuild);

        if (terrain.TryGetComponent<ITerrainRenderer>(out var terrainRenderer))
        {
            command.SetComputeTextureParam(virtualTextureBuild, 0, "_TerrainNormalMap", terrainRenderer.NormalMap);
        }

        // First, figure out which unused tiles we can use
        var updateRect = new RectInt();
        var updateMip = -1;

        // TODO: List Pool
        var scaleOffsets = new List<Vector4>();
        var dstOffsets = new List<uint>();
        var destPixels = new List<uint>();
        var tileRequests = new List<uint>();

        var index = 0;
        foreach (var request in sortedRequests)
        {
            if (lruCache.Contains(request))
                continue;

            var position = Texture2DExtensions.TextureByteOffsetToCoord(request, IndirectionTextureResolution);

            int targetIndex;
            if (updateMip == -1)
            {
                updateMip = position.z;
                updateRect = new RectInt(position.x, position.y, 1, 1);
            }

            // Remove currently-existing VirtualTextureTile in this location
            var nextTileIndex = lruCache.Count;
            if (nextTileIndex < virtualTileCount)
            {
                targetIndex = nextTileIndex;
            }
            else
            {
                var lastTileUsed = lruCache.Remove();
                targetIndex = lastTileUsed.Item2;
                var lastIndex = lastTileUsed.Item1;
                var existingPosition = Texture2DExtensions.TextureByteOffsetToCoord(lastIndex, IndirectionTextureResolution);

                // Invalidate existing position
                indirectionTexturePixels[lastIndex] = false;

                // Set the mip just before the one being removed as the minimum update, so that it can fall back to the tile before it.
                existingPosition.x >>= 1;
                existingPosition.y >>= 1;
                existingPosition.z += 1;

                if (existingPosition.z > updateMip)
                {
                    var delta = 1 << existingPosition.z - updateMip;
                    updateMip = existingPosition.z;

                    updateRect.SetMinMax(updateRect.min / delta, updateRect.max / delta);
                    updateRect = updateRect.Encapsulate(existingPosition.x, existingPosition.y);
                }
                else if (existingPosition.z == updateMip)
                {
                    updateRect = updateRect.Encapsulate(existingPosition.x, existingPosition.y);
                }
            }

            // Track the highest mip, as the update starts at the highest mip and works down
            // We only need to update mips higher than the one that has changed.
            if (position.z > updateMip)
            {
                var delta = 1 << position.z - updateMip;
                updateMip = position.z;

                updateRect.SetMinMax(updateRect.min / delta, updateRect.max / delta);
                updateRect = updateRect.Encapsulate(position.x, position.y);
            }
            else if (position.z == updateMip)
            {
                updateRect = updateRect.Encapsulate(position.x, position.y);
            }

            // Add new tile to cache
            lruCache.Add(request, targetIndex);

            // Mark this pixel as filled in the array
            indirectionTexturePixels[request] = true;

            var mipFactor = 1f / (virtualResolution >> position.z);
            var uvScale = tileResolution * mipFactor;
            var uvOffset = new Vector2(position.x * tileResolution, position.y * tileResolution) * mipFactor;

            // Set some data for the ComputeShader to update the indirectiontexture
            tileRequests.Add((uint)((targetIndex & 0xFFFF) | ((position.z & 0xFFFF) << 16)));
            destPixels.Add((uint)(position.x | (position.y << 16)));
            scaleOffsets.Add(new Vector4(uvScale, uvScale, uvOffset.x, uvOffset.y));
            dstOffsets.Add((uint)targetIndex);

            // Exit if we've reached the max number of tiles for this frame
            if (++index == updateTileCount)
            {
                break;
            }
        }

        // Upload the new positions
        command.SetBufferData(tileRequestsBuffer, tileRequests);
        command.SetBufferData(destPixelbuffer, destPixels);
        command.SetBufferData(scaleOffsetsBuffer, scaleOffsets);
        command.SetBufferData(dstOffsetsBuffer, dstOffsets);

        var dxtCompressCS = Resources.Load<ComputeShader>("DxtCompress");

        // Build compute shader twice, once for base mip, and once for second mip, so we can use HW trilinear filtering
        var length = Mathf.Min(updateTileCount, pendingRequests.Count);

        // Build the virtual texture
        // Albedo
        var virtualAlbedoTemp = Shader.PropertyToID("_VirtualAlbedoTemp");
        var albedoDesc = new RenderTextureDescriptor(tileResolution * updateTileCount, tileResolution, RenderTextureFormat.ARGB32) { enableRandomWrite = true, sRGB = true };
        command.GetTemporaryRT(virtualAlbedoTemp, albedoDesc);

        // Normal
        var virtualNormalTemp = Shader.PropertyToID("_VirtualNormalTemp");
        var normalDesc = new RenderTextureDescriptor(tileResolution * updateTileCount, tileResolution, RenderTextureFormat.ARGB32) { enableRandomWrite = true, sRGB = false };
        command.GetTemporaryRT(virtualNormalTemp, normalDesc);

        // Height
        var virtualHeightTemp = Shader.PropertyToID("_VirtualHeightTemp");
        var heightDesc = new RenderTextureDescriptor(tileResolution * updateTileCount, tileResolution, RenderTextureFormat.R8) { enableRandomWrite = true, sRGB = false };
        command.GetTemporaryRT(virtualHeightTemp, heightDesc);

        terrainTextureManager.SetShaderProperties(command, virtualTextureBuild);

        command.SetComputeTextureParam(virtualTextureBuild, 0, "_AlbedoSmoothness", virtualAlbedoTemp);
        command.SetComputeTextureParam(virtualTextureBuild, 0, "_NormalMetalOcclusion", virtualNormalTemp);
        command.SetComputeTextureParam(virtualTextureBuild, 0, "_Heights", virtualHeightTemp);
        command.SetComputeBufferParam(virtualTextureBuild, 0, "_ScaleOffsets", scaleOffsetsBuffer);
        command.SetComputeBufferParam(virtualTextureBuild, 0, "_DstOffsets", dstOffsetsBuffer);

        command.SetComputeVectorParam(virtualTextureBuild, "_Resolution", new Vector2(tileResolution, tileResolution));
        command.SetComputeIntParam(virtualTextureBuild, "_Width", tileResolution);

        using (var buildScope = command.ProfilerScope("Build"))
            command.DispatchNormalized(virtualTextureBuild, 0, tileResolution * length, tileResolution, 1);

        // Albedo compression target
        var albedoCompressId = Shader.PropertyToID("_VirtualAlbedoCompress");
        var albedoCompressDesc = new RenderTextureDescriptor((tileResolution >> 2) * updateTileCount, tileResolution >> 2, GraphicsFormat.R32G32B32A32_UInt, 0, 2) { enableRandomWrite = true, useMipMap = true, };
        command.GetTemporaryRT(albedoCompressId, albedoCompressDesc);

        // Normal compression target
        var normalCompressId = Shader.PropertyToID("_VirtualNormalCompress");
        command.GetTemporaryRT(normalCompressId, albedoCompressDesc);

        // Height compression target
        var heightCompressId = Shader.PropertyToID("_VirtualHeightCompress");
        var heightCompressDesc = new RenderTextureDescriptor((tileResolution >> 2) * updateTileCount, tileResolution >> 2, GraphicsFormat.R32G32_UInt, 0, 2) { enableRandomWrite = true, useMipMap = true, };
        command.GetTemporaryRT(heightCompressId, heightCompressDesc);

        // Albedo
        command.SetComputeTextureParam(dxtCompressCS, 0, "_AlbedoInput", virtualAlbedoTemp);
        command.SetComputeTextureParam(dxtCompressCS, 0, "_NormalInput", virtualNormalTemp);
        command.SetComputeTextureParam(dxtCompressCS, 0, "_HeightInput", virtualHeightTemp);
        command.SetComputeTextureParam(dxtCompressCS, 0, "_AlbedoResult0", albedoCompressId, 0);
        command.SetComputeTextureParam(dxtCompressCS, 0, "_AlbedoResult1", albedoCompressId, 1);
        command.SetComputeTextureParam(dxtCompressCS, 0, "_NormalResult0", normalCompressId, 0);
        command.SetComputeTextureParam(dxtCompressCS, 0, "_NormalResult1", normalCompressId, 1);
        command.SetComputeTextureParam(dxtCompressCS, 0, "_HeightResult0", heightCompressId, 0);
        command.SetComputeTextureParam(dxtCompressCS, 0, "_HeightResult1", heightCompressId, 1);

        using (var compressScope = command.ProfilerScope("Compress"))
            command.DispatchNormalized(dxtCompressCS, 0, (tileResolution >> 2) * length, tileResolution >> 2, 1);

        command.ReleaseTemporaryRT(virtualAlbedoTemp);
        command.ReleaseTemporaryRT(virtualNormalTemp);
        command.ReleaseTemporaryRT(virtualHeightTemp);

        for (var j = 0; j < dstOffsets.Count; j++)
        {
            for (var i = 0; i < 2; i++)
            {
                var mipResolution = tileResolution >> i;

                // Copy albedo, normal and height
                command.CopyTexture(albedoCompressId, 0, i, j * (mipResolution >> 2), 0, mipResolution >> 2, mipResolution >> 2, albedoSmoothnessTexture, (int)dstOffsets[j], i, 0, 0);
                command.CopyTexture(normalCompressId, 0, i, j * (mipResolution >> 2), 0, mipResolution >> 2, mipResolution >> 2, normalTexture, (int)dstOffsets[j], i, 0, 0);
                command.CopyTexture(heightCompressId, 0, i, j * (mipResolution >> 2), 0, mipResolution >> 2, mipResolution >> 2, heightTexture, (int)dstOffsets[j], i, 0, 0);
            }
        }

        command.ReleaseTemporaryRT(albedoCompressId);
        command.ReleaseTemporaryRT(normalCompressId);
        command.ReleaseTemporaryRT(heightCompressId);

        pendingRequests.Clear();

        // Dispatch threads to update the indirection texture
        var virtualTextureUpdateShader = Resources.Load<ComputeShader>("VirtualTextureUpdate");
        var mipCount = (int)Math.Log(IndirectionTextureResolution, 2);

        // Only update required mips (And extents?)
        // Max(0) because highest mip might request it's parent mip too, this is easier (and fast enough)
        var start = Math.Max(0, mipCount - updateMip);

        using (var updateScope = command.ProfilerScope("Update"))
        {
            // This pass copies any data to be unmapped into a temporary buffer so it can be checked while iterating over each mip in kernel #1
            command.SetComputeBufferParam(virtualTextureUpdateShader, 0, "TileRequests", tileRequestsBuffer);
            command.SetComputeBufferParam(virtualTextureUpdateShader, 0, "DestPixels", destPixelbuffer);
            command.SetComputeBufferParam(virtualTextureUpdateShader, 0, "MappedTiles", mappedTiles);
            command.SetComputeBufferParam(virtualTextureUpdateShader, 0, "TilesToUnmap", tilesToUnmapBuffer);
            command.SetComputeIntParam(virtualTextureUpdateShader, "_MaxIndex", destPixels.Count);
            command.DispatchNormalized(virtualTextureUpdateShader, 0, destPixels.Count, 1, 1);

            // Set some buffers..
            command.SetComputeBufferParam(virtualTextureUpdateShader, 1, "TileRequests", tileRequestsBuffer);
            command.SetComputeBufferParam(virtualTextureUpdateShader, 1, "DestPixels", destPixelbuffer);
            command.SetComputeBufferParam(virtualTextureUpdateShader, 1, "MappedTiles", mappedTiles);
            command.SetComputeBufferParam(virtualTextureUpdateShader, 1, "TilesToUnmap", tilesToUnmapBuffer);

            // dispatch mip updates
            //for (var z = start; z < mipCount; z++)
            for (var z = 0; z <= mipCount; z++)
            {
                command.SetComputeTextureParam(virtualTextureUpdateShader, 1, "_IndirectionTextureMap", indirectionTextureMapTexture, z);
                command.SetComputeIntParam(virtualTextureUpdateShader, "CurrentMip", z);
                command.SetComputeTextureParam(virtualTextureUpdateShader, 1, "DestMip", indirectionTexture, z);
                command.SetComputeIntParam(virtualTextureUpdateShader, "_MaxIndex", tileRequests.Count);
                command.DispatchNormalized(virtualTextureUpdateShader, 1, tileRequests.Count, 1, 1);
            }

            // Update Page Table

            for (var z = mipCount - 1; z >= 0; z--)
            {
                command.SetComputeTextureParam(virtualTextureUpdateShader, 2, "_IndirectionTextureMap", indirectionTextureMapTexture, z);
                command.SetComputeTextureParam(virtualTextureUpdateShader, 2, "DestMip", indirectionTexture, z);
                command.SetComputeTextureParam(virtualTextureUpdateShader, 2, "SourceMip", indirectionTexture, z + 1);

                // dispatch entire mip for now.
                var mipSize = IndirectionTextureResolution >> z;
                command.SetComputeIntParam(virtualTextureUpdateShader, "_MaxIndex", mipSize);
                command.DispatchNormalized(virtualTextureUpdateShader, 2, mipSize, mipSize, 1);
            }
        }
    }

    public static void OnTerrainTextureChanged(Terrain terrain, RectInt texelRegion)
    {
        foreach (var node in activeNodes)
        {
            var start = Vector2Int.FloorToInt((Vector2)texelRegion.min / terrain.terrainData.alphamapResolution * node.IndirectionTextureResolution);
            var end = Vector2Int.CeilToInt((Vector2)texelRegion.max / terrain.terrainData.alphamapResolution * node.IndirectionTextureResolution);

            // TODO: We also need to clear these pixels from the GPU copy. However, that would require filling a big buffer with pixels to clear, so just clear all for now
            node.needsClear = true;

            for (var mip = 0; mip < node.indirectionTexture.mipmapCount; mip++)
            {
                var mipStart = Vector2Int.FloorToInt((Vector2)start / Mathf.Pow(2, mip));
                var mipEnd = Vector2Int.CeilToInt((Vector2)end / Mathf.Pow(2, mip));

                // Offset in bytes for this mip in the array
                var targetOffset = Texture2DExtensions.MipOffset(mip, node.IndirectionTextureResolution);
                var mipSize = node.IndirectionTextureResolution / (int)Mathf.Pow(2, mip);

                var width = mipEnd.x - mipStart.x;
                var height = mipEnd.y - mipStart.y;

                // Set all the cells to false, this will make them get requested again next update
                for (var y = mipStart.y; y < mipStart.y + height; y++)
                {
                    for (var x = mipStart.x; x < mipStart.x + width; x++)
                    {
                        var coord = x + y * mipSize;
                        var target = targetOffset + coord;
                        node.indirectionTexturePixels[target] = false;
                    }
                }
            }
        }
    }
}
