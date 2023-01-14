using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public class SingleTextureCache : IDisposable
{
    private readonly string name;
    private readonly Dictionary<Camera, RenderTexture> textures = new();
    private bool disposedValue;

    public SingleTextureCache(string name)
    {
        this.name = name;
    }

    public RenderTexture GetTexture(Camera camera, RenderTextureDescriptor descriptor)
    {
        if (!textures.TryGetValue(camera, out var texture))
        {
            texture = new RenderTexture(descriptor)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = name,
            }.Created();
            textures.Add(camera, texture);
        }
        else
        {
            texture.Resize(descriptor.width, descriptor.height, descriptor.volumeDepth);
        }

        return texture;
    }

    protected virtual void Dispose(bool disposing)
    {
        foreach (var data in textures)
        {
            Object.DestroyImmediate(data.Value);
        }

        if (!disposedValue)
        {
            if (disposing)
            {
                textures.Clear();
            }
            else
            {
                Debug.LogError($"GarbageCollector disposing of {nameof(SingleTextureCache)} [{name}]. Please use .Dispose() to manually release.");
            }

            disposedValue = true;
        }
    }

    ~SingleTextureCache()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}