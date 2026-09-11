using System;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Independent render layer with its own camera and optional render target.
/// Use for minimaps, split-screen, HUD at native resolution, or any rendering
/// that needs a different camera/projection than the main scene.
///
/// Usage:
///   // Minimap layer
///   var minimap = new RenderLayer(Eng.Width - 80, 8, 72, 72);
///   minimap.Camera.FocusOn(player.Position.X, player.Position.Y);
///   minimap.Camera.Zoom = 0.2f;
///
///   // In scene Draw():
///   minimap.Begin();
///   tilemap.Draw();  // draws with minimap's camera
///   minimap.End();   // composites to screen at (Eng.Width-80, 8)
/// </summary>
public class RenderLayer : IDisposable
{
    private Framebuffer? _fbo;
    private Camera? _savedCamera;

    /// <summary>Independent camera for this layer.</summary>
    public Camera Camera { get; } = new();

    /// <summary>Screen position where this layer renders.</summary>
    public float ScreenX { get; set; }
    public float ScreenY { get; set; }

    /// <summary>Render dimensions (pixels).</summary>
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Tint/alpha applied when compositing to screen.</summary>
    public float ColorR = 1, ColorG = 1, ColorB = 1, ColorA = 1;

    /// <summary>Resize the render layer. Recreates the FBO at the new dimensions.</summary>
    public void Resize(int width, int height)
    {
        if (width == Width && height == Height) return;
        Width = width;
        Height = height;
        _fbo?.Dispose();
        _fbo = null; // Recreated lazily in EnsureFBO
    }

    public RenderLayer(float screenX, float screenY, int width, int height)
    {
        ScreenX = screenX;
        ScreenY = screenY;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Begin rendering to this layer. Swaps the active camera and redirects
    /// drawing to this layer's FBO. All Draw calls between Begin/End use this layer's camera.
    /// </summary>
    public void Begin()
    {
        EnsureFBO();

        // Save the current camera and swap in ours
        _savedCamera = Eng.Camera;
        Eng.Context.Camera = Camera;

        // Flush any pending draws from the main camera
        Eng.GL.FlushAll();

        // Bind our FBO
        _fbo!.Bind();
        Eng.GL.Api.ClearColor(0, 0, 0, 0);
        Eng.GL.Api.Clear(Silk.NET.OpenGL.ClearBufferMask.ColorBufferBit);

        // Set projection for this layer's dimensions
        var proj = System.Numerics.Matrix4x4.CreateOrthographicOffCenter(0, Width, Height, 0, -1, 1);

        // Apply camera rotation if any
        if (Camera.Rotation != 0)
        {
            float cx = Width * 0.5f;
            float cy = Height * 0.5f;
            proj = System.Numerics.Matrix4x4.CreateTranslation(-cx, -cy, 0)
                 * System.Numerics.Matrix4x4.CreateRotationZ(-Camera.Rotation * MathF.PI / 180f)
                 * System.Numerics.Matrix4x4.CreateTranslation(cx, cy, 0)
                 * proj;
        }

        // Update shader projection
        Eng.GL.SpriteShader.Use();
        Eng.GL.SpriteShader.SetMatrix4("uProjection", proj);
        for (int i = 0; i < DefaultShaders.MaxTextureSlots; i++)
            Eng.GL.SpriteShader.SetInt($"uTextures[{i}]", i);

        Eng.GL.PrimitiveShader.Use();
        Eng.GL.PrimitiveShader.SetMatrix4("uProjection", proj);
        Eng.GL.SpriteShader.Use();
    }

    /// <summary>
    /// End rendering to this layer. Restores the main camera and composites
    /// this layer's FBO to the screen at (ScreenX, ScreenY).
    /// </summary>
    public void End()
    {
        // Flush all draws to the FBO
        Eng.GL.FlushAll();

        // Unbind FBO, restore main camera and viewport
        _fbo!.Unbind();
        Eng.GL.RestoreViewport();

        if (_savedCamera != null)
        {
            Eng.Context.Camera = _savedCamera;
            _savedCamera = null;
        }

        // Restore main projection
        Eng.GL.UseSpriteShader();

        // Composite the layer's texture to the screen
        Eng.GL.DrawTexture(
            _fbo.ColorAttachment,
            0, 0, Width, Height,
            ScreenX, ScreenY, Width, Height,
            ColorR, ColorG, ColorB, ColorA);
    }

    private void EnsureFBO()
    {
        if (_fbo != null) return;
        _fbo = new Framebuffer(Eng.GL.Api, Width, Height);
    }

    public void Dispose()
    {
        _fbo?.Dispose();
        _fbo = null;
    }
}
