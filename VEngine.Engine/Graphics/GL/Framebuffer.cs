using System;
using Silk.NET.OpenGL;

namespace VEngine.Engine.Graphics.GL;

/// <summary>
/// Wraps an OpenGL Framebuffer Object (FBO) with a color texture attachment.
/// Used for offscreen rendering (render targets, post-processing).
/// </summary>
public class Framebuffer : IDisposable
{
    private readonly Silk.NET.OpenGL.GL _gl;

    public uint Handle { get; }
    public GLTexture ColorAttachment { get; }
    public int Width { get; }
    public int Height { get; }

    public Framebuffer(Silk.NET.OpenGL.GL gl, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException($"Framebuffer dimensions must be positive (got {width}x{height})");

        _gl = gl;
        Width = width;
        Height = height;

        Handle = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);

        ColorAttachment = GLTexture.Empty(gl, width, height);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            ColorAttachment.Handle, 0);

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            throw new Exception($"Framebuffer incomplete: {status}");

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>
    /// Begin rendering to this FBO.
    /// </summary>
    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    /// <summary>
    /// Stop rendering to this FBO. Restores the default framebuffer.
    /// Caller must call GLRenderer.RestoreViewport() to restore screen viewport.
    /// </summary>
    public void Unbind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>
    /// Clear this FBO to a color (0-1 range). Restores previous framebuffer binding after.
    /// </summary>
    public void Clear(float r = 0, float g = 0, float b = 0, float a = 1)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
        _gl.ClearColor(r, g, b, a);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        // Restore screen viewport
        Core.Eng.GL.RestoreViewport();
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(Handle);
        ColorAttachment.Dispose();
    }
}
