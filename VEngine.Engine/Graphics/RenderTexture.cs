using System;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Offscreen render target backed by an OpenGL framebuffer object.
/// Render entities/primitives to a texture, then draw it to the screen.
/// </summary>
public class RenderTexture
{
    private Framebuffer? _fbo;

    public int Width { get; }
    public int Height { get; }

    private float _colorR = 1, _colorG = 1, _colorB = 1, _colorA = 1;

    public RenderTexture(int width, int height)
    {
        Width = width;
        Height = height;
        _fbo = new Framebuffer(Eng.GL.Api, width, height);
    }

    /// <summary>
    /// Begin rendering to this texture. All subsequent draw calls go here until End().
    /// </summary>
    public void Begin()
    {
        Eng.GL.FlushAll();
        _fbo!.Bind();
    }

    /// <summary>
    /// Stop rendering to this texture. Restores the default render target.
    /// </summary>
    public void End()
    {
        Eng.GL.FlushAll();
        _fbo!.Unbind();
        Eng.GL.RestoreViewport();
    }

    /// <summary>Draw this texture at screen coordinates, original size.</summary>
    public void Draw(int x, int y)
    {
        Draw(0, 0, Width, Height, x, y, Width, Height);
    }

    /// <summary>Draw this texture scaled to a destination rect.</summary>
    public void Draw(int x, int y, int w, int h)
    {
        Draw(0, 0, Width, Height, x, y, w, h);
    }

    /// <summary>Draw a sub-region of this texture to a destination rect.</summary>
    public void Draw(int srcX, int srcY, int srcW, int srcH, int dstX, int dstY, int dstW, int dstH)
    {
        if (_fbo == null) return;
        Eng.GL.UseSpriteShader();
        Eng.GL.DrawTexture(
            _fbo.ColorAttachment,
            srcX, srcY, srcW, srcH,
            dstX, dstY, dstW, dstH,
            _colorR, _colorG, _colorB, _colorA);
    }

    /// <summary>Set color and alpha modulation applied when drawing this texture.</summary>
    public void SetColor(byte r, byte g, byte b, byte a = 255)
    {
        _colorR = r / 255f;
        _colorG = g / 255f;
        _colorB = b / 255f;
        _colorA = a / 255f;
    }

    /// <summary>Clear the texture to a color.</summary>
    public void Clear(byte r = 0, byte g = 0, byte b = 0, byte a = 255)
    {
        _fbo?.Clear(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    public void Destroy()
    {
        _fbo?.Dispose();
        _fbo = null;
    }
}
