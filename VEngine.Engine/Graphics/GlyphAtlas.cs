using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SDL2;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Pre-rendered glyph atlas for a font at a specific size. Supports the full Unicode range
/// by rendering glyphs on demand and packing them into a dynamically growing texture.
/// Cached per font+size. Used by Text entities when UseAtlas = true.
/// </summary>
public class GlyphAtlas
{
    private static readonly Dictionary<string, GlyphAtlas> _cache = new();

    public GLTexture Texture { get; private set; }
    public int LineHeight { get; }

    private readonly Dictionary<char, GlyphInfo> _glyphs = new();
    private readonly IntPtr _font;
    private readonly string _fontPath;
    private readonly int _fontSize;
    private int _atlasWidth, _atlasHeight;
    private int _penX, _penY, _rowHeight;
    private byte[] _pixels;
    private bool _dirty;
    private const int Padding = 1;
    private const int InitialSize = 512;

    private struct GlyphInfo
    {
        public int X, Y, W, H;
        public int Advance;
    }

    private GlyphAtlas(IntPtr font, string fontPath, int fontSize)
    {
        _font = font;
        _fontPath = fontPath;
        _fontSize = fontSize;
        LineHeight = SDL_ttf.TTF_FontLineSkip(font);
        _atlasWidth = InitialSize;
        _atlasHeight = InitialSize;
        _pixels = new byte[_atlasWidth * _atlasHeight * 4];
        Texture = GLTexture.FromRGBA(Eng.GL.Api, _atlasWidth, _atlasHeight, _pixels, nearest: true);

        // Pre-render printable ASCII for immediate use
        for (int c = 32; c <= 126; c++)
            EnsureGlyph((char)c);
        FlushToGPU();
    }

    /// <summary>Get or create a glyph atlas for the given font path and size.</summary>
    public static GlyphAtlas Get(string fontPath, int size)
    {
        var key = $"{fontPath}:{size}";
        if (_cache.TryGetValue(key, out var atlas))
            return atlas;

        var fullPath = System.IO.Path.IsPathRooted(fontPath) ? fontPath : Eng.Asset(fontPath);
        var font = SDL_ttf.TTF_OpenFont(fullPath, size);
        if (font == IntPtr.Zero)
            throw new Exception($"Failed to load font for glyph atlas: {fontPath} at size {size}");

        atlas = new GlyphAtlas(font, fontPath, size);
        _cache[key] = atlas;
        return atlas;
    }

    /// <summary>
    /// Draw text using this atlas. Submits one quad per character to the renderer.
    /// Unknown characters are rendered on demand (may cause a one-frame atlas rebuild).
    /// Returns the total width in pixels.
    /// </summary>
    public int DrawText(string text, float x, float y, float r, float g, float b, float a,
        float scaleX = 1, float scaleY = 1)
    {
        bool needsFlush = false;
        foreach (char c in text)
        {
            if (!_glyphs.ContainsKey(c) && c >= 32)
            {
                EnsureGlyph(c);
                needsFlush = true;
            }
        }
        if (needsFlush) FlushToGPU();

        float cursorX = x;
        foreach (char c in text)
        {
            if (!_glyphs.TryGetValue(c, out var glyph)) continue;

            if (glyph.W > 0 && glyph.H > 0)
            {
                Eng.GL.DrawTexture(Texture,
                    glyph.X, glyph.Y, glyph.W, glyph.H,
                    cursorX, y, glyph.W * scaleX, glyph.H * scaleY,
                    r, g, b, a);
            }
            cursorX += glyph.Advance * scaleX;
        }
        return (int)(cursorX - x);
    }

    /// <summary>Measure text width without drawing.</summary>
    public int MeasureWidth(string text)
    {
        int width = 0;
        foreach (char c in text)
        {
            if (_glyphs.TryGetValue(c, out var glyph))
                width += glyph.Advance;
            else if (c >= 32)
            {
                // Measure without rendering to atlas
                SDL_ttf.TTF_SizeUTF8(_font, c.ToString(), out int w, out _);
                width += w;
            }
        }
        return width;
    }

    /// <summary>Free all cached atlases.</summary>
    internal static void Shutdown()
    {
        foreach (var atlas in _cache.Values)
        {
            atlas.Texture.Dispose();
            SDL_ttf.TTF_CloseFont(atlas._font);
        }
        _cache.Clear();
    }

    private unsafe void EnsureGlyph(char c)
    {
        if (_glyphs.ContainsKey(c)) return;

        var str = c.ToString();
        SDL_ttf.TTF_SizeUTF8(_font, str, out int w, out int h);

        if (SDL_ttf.TTF_GlyphMetrics(_font, (ushort)c,
            out _, out _, out _, out _, out int advance) < 0)
        {
            advance = w;
        }

        // Check if we need to wrap to next row
        if (_penX + w + Padding > _atlasWidth)
        {
            _penX = 0;
            _penY += _rowHeight + Padding;
            _rowHeight = 0;
        }

        // Check if we need to grow the atlas
        if (_penY + h > _atlasHeight)
        {
            GrowAtlas();
        }

        // Render glyph to surface and copy to pixel buffer
        if (w > 0 && h > 0)
        {
            var white = new SDL.SDL_Color { r = 255, g = 255, b = 255, a = 255 };
            var surface = SDL_ttf.TTF_RenderUTF8_Blended(_font, str, white);
            if (surface != IntPtr.Zero)
            {
                var surf = Marshal.PtrToStructure<SDL.SDL_Surface>(surface);
                var srcPixels = new ReadOnlySpan<byte>((void*)surf.pixels, surf.pitch * surf.h);
                var fmt = Marshal.PtrToStructure<SDL.SDL_PixelFormat>(surf.format);
                bool isBgra = fmt.Rmask == 0x00FF0000u;

                for (int row = 0; row < surf.h && _penY + row < _atlasHeight; row++)
                {
                    for (int col = 0; col < surf.w && _penX + col < _atlasWidth; col++)
                    {
                        int srcIdx = row * surf.pitch + col * 4;
                        int dstIdx = ((_penY + row) * _atlasWidth + (_penX + col)) * 4;
                        if (isBgra)
                        {
                            _pixels[dstIdx + 0] = srcPixels[srcIdx + 2];
                            _pixels[dstIdx + 1] = srcPixels[srcIdx + 1];
                            _pixels[dstIdx + 2] = srcPixels[srcIdx + 0];
                            _pixels[dstIdx + 3] = srcPixels[srcIdx + 3];
                        }
                        else
                        {
                            _pixels[dstIdx + 0] = srcPixels[srcIdx + 0];
                            _pixels[dstIdx + 1] = srcPixels[srcIdx + 1];
                            _pixels[dstIdx + 2] = srcPixels[srcIdx + 2];
                            _pixels[dstIdx + 3] = srcPixels[srcIdx + 3];
                        }
                    }
                }
                SDL.SDL_FreeSurface(surface);
            }
        }

        _glyphs[c] = new GlyphInfo { X = _penX, Y = _penY, W = w, H = h, Advance = advance };
        _penX += w + Padding;
        _rowHeight = System.Math.Max(_rowHeight, h);
        _dirty = true;
    }

    private void GrowAtlas()
    {
        // Flush any queued quads referencing the old texture before destroying it
        Eng.GL.Flush();

        int newHeight = _atlasHeight * 2;
        var newPixels = new byte[_atlasWidth * newHeight * 4];
        Array.Copy(_pixels, newPixels, _pixels.Length);
        _pixels = newPixels;
        _atlasHeight = newHeight;

        Texture.Dispose();
        Texture = GLTexture.FromRGBA(Eng.GL.Api, _atlasWidth, _atlasHeight, _pixels, nearest: true);
        _dirty = false;
    }

    private void FlushToGPU()
    {
        if (!_dirty) return;
        Texture.UpdateRGBA(0, 0, _atlasWidth, _atlasHeight, _pixels);
        _dirty = false;
    }
}
