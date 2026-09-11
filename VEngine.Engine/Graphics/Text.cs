using System;
using SDL2;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Renders text using TTF fonts. Extends Entity so it can be positioned,
/// layered, and added to scenes like any other game object.
/// </summary>
public class Text : Entity
{
    private IntPtr _font;
    private GLTexture? _texture;
    private int _textureW;
    private int _textureH;

    private string _text = "";
    private string _fontPath = "";
    private int _fontSize;
    private Math.Color _color = Math.Color.White;
    private bool _dirty = true;

    /// <summary>The displayed text string.</summary>
    public string Content
    {
        get => _text;
        set { if (_text != value) { _text = value; _dirty = true; } }
    }

    /// <summary>Text color and alpha.</summary>
    public Math.Color Color
    {
        get => _color;
        set { _color = value; _dirty = true; }
    }

    /// <summary>Font size in points.</summary>
    public int FontSize
    {
        get => _fontSize;
        set
        {
            if (_fontSize != value)
            {
                _fontSize = value;
                LoadFont(_fontPath, value);
                _dirty = true;
            }
        }
    }

    public Text(string text = "", float x = 0, float y = 0) : base(x, y)
    {
        _text = text;
    }

    /// <summary>
    /// Set the font from a TTF file in Assets. Caches fonts by path+size.
    /// </summary>
    public Text SetFont(string path, int size)
    {
        LoadFont(path, size);
        _dirty = true;
        return this;
    }

    /// <summary>
    /// Use the engine's default font (Assets/ui/default-font.ttf).
    /// </summary>
    public Text SetDefaultFont(int size = 16)
    {
        return SetFont("ui/default-font.ttf", size);
    }

    /// <summary>
    /// Measure the text dimensions without rendering.
    /// Returns (width, height) in pixels.
    /// </summary>
    public (int W, int H) Measure()
    {
        if (_font == IntPtr.Zero) return (0, 0);
        if (string.IsNullOrEmpty(_text)) return (0, 0);
        if (SDL_ttf.TTF_SizeUTF8(_font, _text, out int w, out int h) < 0)
            return (0, 0);
        return (w, h);
    }

    public override void Update(float dt)
    {
        base.Update(dt);
    }

    /// <summary>When true, uses glyph atlas for rendering (zero-allocation, better for dynamic text).</summary>
    public bool UseAtlas { get; set; }

    public override void Draw()
    {
        if (string.IsNullOrEmpty(_text) || string.IsNullOrEmpty(_fontPath)) return;

        var cam = Eng.Camera;
        var screen = cam.Transform(Position.X, Position.Y, ScrollFactor);

        if (UseAtlas)
        {
            var atlas = GlyphAtlas.Get(_fontPath, _fontSize);
            atlas.DrawText(_text, screen.X, screen.Y,
                _color.R / 255f, _color.G / 255f, _color.B / 255f, _color.A / 255f,
                ScaleX * cam.Zoom, ScaleY * cam.Zoom);
            return;
        }

        // Fallback: per-string texture (better for static text, supports full Unicode)
        if (_font == IntPtr.Zero) return;

        if (_dirty)
        {
            RebuildTexture();
            _dirty = false;
        }

        if (_texture == null) return;

        Eng.GL.DrawTexture(
            _texture,
            0, 0, _textureW, _textureH,
            screen.X, screen.Y,
            _textureW * ScaleX * cam.Zoom,
            _textureH * ScaleY * cam.Zoom,
            1, 1, 1, 1);
    }

    protected override void OnDestroy()
    {
        DestroyTexture();
    }

    private void LoadFont(string path, int size)
    {
        if (size <= 0)
            throw new ArgumentException($"Font size must be positive (got {size})");
        _fontPath = path;
        _fontSize = size;
        _font = FontCache.Get(path, size);
    }

    private void RebuildTexture()
    {
        DestroyTexture();

        if (string.IsNullOrEmpty(_text) || _font == IntPtr.Zero) return;

        var surface = SDL_ttf.TTF_RenderUTF8_Blended(_font, _text, _color);
        if (surface == IntPtr.Zero) return;

        // FromSurface reads pixels and frees the surface
        _texture = GLTexture.FromSurface(Eng.GL.Api, surface);
        _textureW = _texture.Width;
        _textureH = _texture.Height;
        BaseWidth = _textureW;
        BaseHeight = _textureH;
    }

    private void DestroyTexture()
    {
        _texture?.Dispose();
        _texture = null;
    }

    /// <summary>
    /// Call once at engine shutdown to free all cached fonts.
    /// </summary>
    internal static void ShutdownFonts()
    {
        GlyphAtlas.Shutdown();
        FontCache.Shutdown();
    }
}
