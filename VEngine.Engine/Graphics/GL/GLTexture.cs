using System;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using SDL2;

namespace VEngine.Engine.Graphics.GL;

/// <summary>
/// Wraps a single OpenGL texture object. Handles creation, uploading, binding, and disposal.
/// </summary>
public class GLTexture : IDisposable
{
    private readonly Silk.NET.OpenGL.GL _gl;

    public uint Handle { get; }
    public int Width { get; }
    public int Height { get; }

    private GLTexture(Silk.NET.OpenGL.GL gl, uint handle, int width, int height)
    {
        _gl = gl;
        Handle = handle;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Create a texture from RGBA pixel data (top-left origin, 4 bytes per pixel).
    /// </summary>
    public static unsafe GLTexture FromRGBA(Silk.NET.OpenGL.GL gl, int width, int height, ReadOnlySpan<byte> pixels, bool nearest = true)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);

        fixed (byte* ptr = pixels)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                (uint)width, (uint)height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
        }

        SetFilterAndWrap(gl, nearest);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        return new GLTexture(gl, tex, width, height);
    }

    /// <summary>
    /// Create a texture directly from a native RGBA pointer (zero managed copy).
    /// </summary>
    public static unsafe GLTexture FromNativeRGBA(Silk.NET.OpenGL.GL gl, int width, int height, IntPtr nativePixels, bool nearest = true)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);

        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
            (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, (void*)nativePixels);

        SetFilterAndWrap(gl, nearest);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        return new GLTexture(gl, tex, width, height);
    }

    /// <summary>
    /// Update an existing texture's pixel data in-place (no allocation).
    /// Must be same width/height as original. Uses glTexSubImage2D.
    /// </summary>
    public unsafe void UpdateRGBA(ReadOnlySpan<byte> pixels)
    {
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
        fixed (byte* ptr = pixels)
        {
            _gl.TexSubImage2D(TextureTarget.Texture2D, 0,
                0, 0, (uint)Width, (uint)Height,
                PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
        }
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>
    /// Update an existing texture from a native pointer (no managed copy needed).
    /// </summary>
    public unsafe void UpdateRGBA(IntPtr nativePixels)
    {
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
        _gl.TexSubImage2D(TextureTarget.Texture2D, 0,
            0, 0, (uint)Width, (uint)Height,
            PixelFormat.Rgba, PixelType.UnsignedByte, (void*)nativePixels);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>
    /// Create a texture from BGRA pixel data (SDL_ttf Blended surfaces use this).
    /// </summary>
    public static unsafe GLTexture FromBGRA(Silk.NET.OpenGL.GL gl, int width, int height, ReadOnlySpan<byte> pixels, bool nearest = true)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);

        fixed (byte* ptr = pixels)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                (uint)width, (uint)height, 0,
                PixelFormat.Bgra, PixelType.UnsignedByte, ptr);
        }

        SetFilterAndWrap(gl, nearest);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        return new GLTexture(gl, tex, width, height);
    }

    /// <summary>
    /// Create a texture from an SDL_Surface (auto-detects RGBA vs BGRA from surface format).
    /// Disposes the surface after uploading.
    /// </summary>
    public static unsafe GLTexture FromSurface(Silk.NET.OpenGL.GL gl, IntPtr sdlSurface, bool nearest = true)
    {
        if (sdlSurface == IntPtr.Zero)
            throw new ArgumentException("Surface is null");

        // Read surface info
        var surface = Marshal.PtrToStructure<SDL.SDL_Surface>(sdlSurface);
        int w = surface.w;
        int h = surface.h;
        var format = Marshal.PtrToStructure<SDL.SDL_PixelFormat>(surface.format);

        // Determine pixel format from surface masks
        bool isBgra = format.Rmask == 0x00FF0000u; // BGRA byte order (common on Windows)

        var pixels = new ReadOnlySpan<byte>((void*)surface.pixels, surface.pitch * h);

        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);

        // Handle row padding: SDL surfaces may have pitch > width * bytesPerPixel
        int bytesPerPixel = format.BytesPerPixel;
        int rowLength = surface.pitch / bytesPerPixel;
        if (rowLength != w)
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, rowLength);

        fixed (byte* ptr = pixels)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                (uint)w, (uint)h, 0,
                isBgra ? PixelFormat.Bgra : PixelFormat.Rgba,
                PixelType.UnsignedByte, ptr);
        }

        // Reset row length to default
        if (rowLength != w)
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);

        SetFilterAndWrap(gl, nearest);
        gl.BindTexture(TextureTarget.Texture2D, 0);

        SDL.SDL_FreeSurface(sdlSurface);
        return new GLTexture(gl, tex, w, h);
    }

    /// <summary>
    /// Create a 1x1 solid color texture. Used for MakeGraphic (scaled via vertex positions).
    /// </summary>
    public static GLTexture SolidColor(Silk.NET.OpenGL.GL gl, byte r, byte g, byte b, byte a = 255)
    {
        ReadOnlySpan<byte> pixel = stackalloc byte[] { r, g, b, a };
        return FromRGBA(gl, 1, 1, pixel);
    }

    /// <summary>
    /// Create an empty texture (for FBO color attachments). Pixels are uninitialized.
    /// </summary>
    public static unsafe GLTexture Empty(Silk.NET.OpenGL.GL gl, int width, int height, bool nearest = true)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);

        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
            (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);

        SetFilterAndWrap(gl, nearest);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        return new GLTexture(gl, tex, width, height);
    }

    /// <summary>
    /// Update a sub-region of the texture with new RGBA pixel data.
    /// Useful for text that changes content but not dimensions.
    /// </summary>
    public unsafe void UpdateRGBA(int x, int y, int width, int height, ReadOnlySpan<byte> pixels)
    {
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
        fixed (byte* ptr = pixels)
        {
            _gl.TexSubImage2D(TextureTarget.Texture2D, 0,
                x, y, (uint)width, (uint)height,
                PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
        }
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Bind(uint slot = 0)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + (int)slot);
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
    }

    public void SetFilter(bool nearest)
    {
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
        SetFilterAndWrap(_gl, nearest);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    private static void SetFilterAndWrap(Silk.NET.OpenGL.GL gl, bool nearest)
    {
        var filter = nearest ? (int)TextureMinFilter.Nearest : (int)TextureMinFilter.Linear;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, filter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, filter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    public void Dispose()
    {
        _gl.DeleteTexture(Handle);
    }
}
