using System;
using System.Collections.Generic;
using SDL2;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Unified font cache shared across Text entities and UI elements.
/// Fonts are cached by path + size. Freed at engine shutdown.
/// </summary>
internal static class FontCache
{
    private static readonly Dictionary<string, IntPtr> _cache = new();

    /// <summary>Get or load a font by path and size.</summary>
    public static IntPtr Get(string path, int size)
    {
        var key = $"{path}:{size}";
        if (_cache.TryGetValue(key, out var font))
            return font;

        var fullPath = System.IO.Path.IsPathRooted(path) ? path : Core.Eng.Asset(path);
        font = SDL_ttf.TTF_OpenFont(fullPath, size);
        if (font == IntPtr.Zero)
            throw new Exception($"Failed to load font '{path}' at size {size}: {SDL.SDL_GetError()}");

        _cache[key] = font;
        return font;
    }

    /// <summary>Get the default engine font at a given size.</summary>
    public static IntPtr GetDefault(int size) => Get("ui/default-font.ttf", size);

    /// <summary>Free all cached fonts. Called at engine shutdown.</summary>
    public static void Shutdown()
    {
        foreach (var font in _cache.Values)
        {
            try { SDL_ttf.TTF_CloseFont(font); }
            catch (Exception ex) { Console.WriteLine($"[FontCache] Close error: {ex.Message}"); }
        }
        _cache.Clear();
        SDL_ttf.TTF_Quit();
    }
}
