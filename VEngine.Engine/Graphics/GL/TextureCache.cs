using System;
using System.Collections.Generic;
using System.IO;
using SDL2;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace VEngine.Engine.Graphics.GL;

/// <summary>
/// Manages texture loading, caching, and lifecycle. Textures are loaded once
/// and shared across all entities. Disposed at engine shutdown.
/// </summary>
internal class TextureCache : IDisposable
{
    private readonly Silk.NET.OpenGL.GL _gl;
    private readonly Dictionary<string, GLTexture> _cache = new();

    public TextureCache(Silk.NET.OpenGL.GL gl)
    {
        _gl = gl;
    }

    /// <summary>Load a texture from file, or return cached version.</summary>
    public GLTexture GetOrCreate(string path)
    {
        var cacheKey = Path.GetFullPath(path);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        GLTexture tex;
        if (path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            var surface = SDL.SDL_LoadBMP(path);
            if (surface == IntPtr.Zero)
                throw new Exception($"Failed to load BMP '{path}': {SDL.SDL_GetError()}");
            tex = GLTexture.FromSurface(_gl, surface);
        }
        else
        {
            ImageResult image;
            using (var stream = File.OpenRead(path))
                image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            tex = GLTexture.FromRGBA(_gl, image.Width, image.Height, image.Data);
        }

        _cache[cacheKey] = tex;
        TrackSceneTexture(cacheKey);
        return tex;
    }

    /// <summary>Check if a texture is in the cache.</summary>
    public bool Has(string path) => _cache.ContainsKey(Path.GetFullPath(path));

    /// <summary>Insert a pre-created texture into the cache.</summary>
    public void CacheExisting(string fullPath, GLTexture tex) => _cache[fullPath] = tex;

    /// <summary>Unload a specific texture from the cache and free GPU memory.</summary>
    public void Unload(string path)
    {
        var cacheKey = Path.GetFullPath(path);
        if (_cache.Remove(cacheKey, out var tex))
            tex.Dispose();
    }

    /// <summary>Unload all cached textures.</summary>
    public void UnloadAll()
    {
        foreach (var tex in _cache.Values)
            tex.Dispose();
        _cache.Clear();
    }

    /// <summary>Number of textures currently cached.</summary>
    public int Count => _cache.Count;

    // ── Scene-scoped texture tracking ──────────────────────────
    // Scopes are a stack so that pushed scenes (Eng.PushScene) get their own
    // scope layered above the parent's. EndSceneScope unloads only the top layer.

    private readonly Stack<HashSet<string>> _sceneScopes = new();

    /// <summary>Begin tracking textures loaded from this point. Call before scene Create().</summary>
    internal void BeginSceneScope()
    {
        _sceneScopes.Push(new HashSet<string>());
    }

    /// <summary>Unload all textures loaded since the matching BeginSceneScope() and pop the scope.</summary>
    internal void EndSceneScope()
    {
        if (_sceneScopes.Count == 0) return;
        foreach (var key in _sceneScopes.Pop())
        {
            if (_cache.Remove(key, out var tex))
                tex.Dispose();
        }
    }

    /// <summary>Record a texture path in the innermost active scene scope.</summary>
    private void TrackSceneTexture(string cacheKey)
    {
        if (_sceneScopes.Count > 0)
            _sceneScopes.Peek().Add(cacheKey);
    }

    public void Dispose()
    {
        UnloadAll();
    }
}
