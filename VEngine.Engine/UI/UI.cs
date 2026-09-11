using System;
using System.Collections.Generic;
using SDL2;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;
using VEngine.Engine.Input;
using Color = VEngine.Engine.Math.Color;

namespace VEngine.Engine.UI;

/// <summary>
/// Base class for UI elements. Renders in screen space (bypasses camera).
/// Add to a scene like any entity — uses high Layer so it draws on top.
/// </summary>
public abstract class UIElement : Entity
{
    /// <summary>Font size in points for text rendering.</summary>
    public int FontSize = 16;

    protected UIElement(float x, float y, float w = 0, float h = 0) : base(x, y)
    {
        BaseWidth = w;
        BaseHeight = h;
        Layer = 10;
    }

    /// <summary>No physics for UI elements.</summary>
    public override void Update(float dt) { }

    protected static IntPtr GetFont(int size)
    {
        try { return Graphics.FontCache.GetDefault(size); }
        catch (Exception ex) { Console.WriteLine($"[UI] Font load failed: {ex.Message}"); return IntPtr.Zero; }
    }

    protected static void RenderText(IntPtr font, string text, Color color,
        ref GLTexture? texture, ref string cached, ref uint cachedColor, ref int w, ref int h)
    {
        uint colorKey = (uint)(color.R << 24 | color.G << 16 | color.B << 8 | color.A);
        if (text == cached && colorKey == cachedColor && texture != null) return;
        cachedColor = colorKey;

        texture?.Dispose();

        var surface = SDL_ttf.TTF_RenderUTF8_Blended(font, text, color);
        if (surface == IntPtr.Zero) { texture = null; cached = text; return; }

        texture = GLTexture.FromSurface(Eng.GL.Api, surface);
        w = texture.Width;
        h = texture.Height;
        cached = text;
    }

    internal static void ShutdownFonts() { /* Fonts freed by FontCache.Shutdown() */ }
}

/// <summary>
/// Text label rendered in screen space.
/// </summary>
public class UILabel : UIElement
{
    private GLTexture? _texture;
    private string _cached = "";
    private uint _cachedColor;
    private int _texW, _texH;

    private string _text;

    public string Text
    {
        get => _text;
        set => _text = value;
    }

    public Color Color = Color.White;

    /// <summary>Max width for text wrapping (0 = no wrapping).</summary>
    public float WrapWidth { get; set; }

    public UILabel(string text, float x, float y) : base(x, y)
    {
        _text = text;
    }

    public override void Draw()
    {
        if (string.IsNullOrEmpty(_text)) return;

        // Wrapped text uses glyph atlas for per-character rendering
        if (WrapWidth > 0)
        {
            DrawWrapped();
            return;
        }

        var font = GetFont(FontSize);
        if (font == IntPtr.Zero) return;

        RenderText(font, _text, Color, ref _texture, ref _cached, ref _cachedColor, ref _texW, ref _texH);

        if (_texture != null)
        {
            Eng.GL.DrawTexture(_texture,
                0, 0, _texW, _texH,
                Position.X, Position.Y, _texW, _texH,
                1, 1, 1, 1);
        }
    }

    private void DrawWrapped()
    {
        try
        {
            var atlas = Graphics.GlyphAtlas.Get("ui/default-font.ttf", FontSize);
            float x = Position.X;
            float y = Position.Y;
            float lineStart = x;
            float cr = Color.R / 255f, cg = Color.G / 255f, cb = Color.B / 255f, ca = Color.A / 255f;

            var words = _text.Split(' ');
            foreach (var word in words)
            {
                int wordWidth = atlas.MeasureWidth(word);
                if (x - lineStart + wordWidth > WrapWidth && x > lineStart)
                {
                    x = lineStart;
                    y += atlas.LineHeight;
                }
                atlas.DrawText(word, x, y, cr, cg, cb, ca);
                x += wordWidth;
                // Space
                atlas.DrawText(" ", x, y, cr, cg, cb, ca);
                x += atlas.MeasureWidth(" ");
            }
            BaseHeight = y - Position.Y + atlas.LineHeight;
        }
        catch (Exception ex) { Console.WriteLine($"[UILabel] DrawWrapped error: {ex.Message}"); }
    }

    protected override void OnDestroy()
    {
        _texture?.Dispose();
        _texture = null;
    }
}

/// <summary>
/// Colored rectangle rendered in screen space.
/// </summary>
public class UIPanel : UIElement
{
    public Color Color = new(40, 40, 40, 200);
    public Color BorderColor = new(100, 100, 100, 200);

    public UIPanel(float x, float y, float w, float h) : base(x, y, w, h) { }

    public override void Draw()
    {
        // Fill
        Eng.GL.FillRect(Position.X, Position.Y, BaseWidth, BaseHeight,
            Color.R / 255f, Color.G / 255f, Color.B / 255f, Color.A / 255f);

        // Border
        if (BorderColor.A > 0)
        {
            Eng.GL.DrawRect(Position.X, Position.Y, BaseWidth, BaseHeight,
                BorderColor.R / 255f, BorderColor.G / 255f, BorderColor.B / 255f, BorderColor.A / 255f);
        }
    }
}

/// <summary>
/// Clickable button with text, hover/pressed visual states, and click callback.
/// </summary>
public class UIButton : UIElement
{
    private GLTexture? _texture;
    private string _cached = "";
    private uint _cachedColor;
    private int _texW, _texH;
    private bool _wasPressed;

    private string _text;

    public string Text
    {
        get => _text;
        set => _text = value;
    }

    public Action? OnClick;
    public bool Hovered { get; private set; }
    public bool Pressed { get; private set; }

    public Color NormalColor = new(60, 60, 60, 220);
    public Color HoverColor = new(80, 80, 80, 240);
    public Color PressedColor = new(35, 35, 35, 255);
    public Color TextColor = Color.White;
    public Color BorderColor = new(120, 120, 120, 200);

    public UIButton(string text, float x, float y, float w, float h) : base(x, y, w, h)
    {
        _text = text;
    }

    public override void Update(float dt)
    {
        var mouse = Eng.Mouse;
        Hovered = mouse.X >= Position.X && mouse.X < Position.X + BaseWidth &&
                  mouse.Y >= Position.Y && mouse.Y < Position.Y + BaseHeight;

        if (Hovered) Eng.MarkCursorHoverable();

        if (Hovered && mouse.IsPressed(MouseButton.Left))
            _wasPressed = true;

        Pressed = _wasPressed && mouse.IsDown(MouseButton.Left);

        if (mouse.IsReleased(MouseButton.Left))
        {
            if (_wasPressed && Hovered)
                OnClick?.Invoke();
            _wasPressed = false;
        }
    }

    public override void Draw()
    {
        // Background
        var bg = Pressed ? PressedColor : Hovered ? HoverColor : NormalColor;
        Eng.GL.FillRect(Position.X, Position.Y, BaseWidth, BaseHeight,
            bg.R / 255f, bg.G / 255f, bg.B / 255f, bg.A / 255f);

        // Border
        if (BorderColor.A > 0)
        {
            Eng.GL.DrawRect(Position.X, Position.Y, BaseWidth, BaseHeight,
                BorderColor.R / 255f, BorderColor.G / 255f, BorderColor.B / 255f, BorderColor.A / 255f);
        }

        // Text — centered
        if (!string.IsNullOrEmpty(_text))
        {
            var font = GetFont(FontSize);
            if (font != IntPtr.Zero)
            {
                RenderText(font, _text, TextColor, ref _texture, ref _cached, ref _cachedColor, ref _texW, ref _texH);
                if (_texture != null)
                {
                    float tx = Position.X + (BaseWidth - _texW) / 2;
                    float ty = Position.Y + (BaseHeight - _texH) / 2;
                    Eng.GL.DrawTexture(_texture,
                        0, 0, _texW, _texH,
                        tx, ty, _texW, _texH,
                        1, 1, 1, 1);
                }
            }
        }
    }

    protected override void OnDestroy()
    {
        _texture?.Dispose();
        _texture = null;
    }
}
