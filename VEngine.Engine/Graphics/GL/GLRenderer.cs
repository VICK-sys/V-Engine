using System;
using System.Diagnostics;
using System.Numerics;
using SDL2;
using Silk.NET.OpenGL;

namespace VEngine.Engine.Graphics.GL;

/// <summary>
/// Core OpenGL renderer. Owns the GL context, shader programs, batchers, texture cache,
/// and manages the frame lifecycle and viewport/letterboxing.
/// </summary>
public class GLRenderer : IDisposable
{
    public Silk.NET.OpenGL.GL Api { get; }
    internal SpriteBatch Sprites { get; }
    internal PrimitiveBatch Primitives { get; }
    public ShaderProgram SpriteShader { get; }
    public ShaderProgram PrimitiveShader { get; }
    public ShaderProgram PostProcessShader { get; }

    /// <summary>Shared 1x1 white texture. Used by MakeGraphic — color comes from vertex data.</summary>
    public GLTexture WhitePixelTexture { get; }

    private readonly TextureCache _textures;

    // Viewport/letterboxing
    private readonly IntPtr _window;
    private readonly int _logicalWidth;
    private readonly int _logicalHeight;
    private int _viewportX, _viewportY, _viewportW, _viewportH;

    // Base projection matrix (screen pixels -> clip space, Y-down)
    public Matrix4x4 Projection { get; private set; }

    /// <summary>Active projection including camera rotation. Used by batch flush.</summary>
    internal Matrix4x4 ActiveProjection { get; set; }

    // GL context handle
    private readonly IntPtr _glContext;

    // Background clear color
    public float ClearR = 20f / 255f, ClearG = 20f / 255f, ClearB = 40f / 255f;

    public GLRenderer(IntPtr window, int logicalWidth, int logicalHeight)
    {
        _window = window;
        _logicalWidth = logicalWidth;
        _logicalHeight = logicalHeight;

        // GL attributes (profile, version, depth, double-buffer) are set by Game.Start
        // before SDL_CreateWindow — they must be set before the window chooses its pixel
        // format, so setting them here (after the window exists) would be a silent no-op
        // on most Windows drivers.
        _glContext = SDL.SDL_GL_CreateContext(window);
        if (_glContext == IntPtr.Zero)
            throw new Exception($"Failed to create GL context: {SDL.SDL_GetError()}");

        SDL.SDL_GL_MakeCurrent(window, _glContext);
        SDL.SDL_GL_SetSwapInterval(1);

        // Load GL functions via SDL's getProcAddress
        try { Api = Silk.NET.OpenGL.GL.GetApi(SDL.SDL_GL_GetProcAddress); }
        catch (Exception ex) { throw new Exception($"Failed to load OpenGL API: {ex.Message}", ex); }

        Console.WriteLine($"[GLRenderer] OpenGL {Api.GetStringS(StringName.Version)}");
        Console.WriteLine($"[GLRenderer] Renderer: {Api.GetStringS(StringName.Renderer)}");

        // Enable blending by default
        Api.Enable(EnableCap.Blend);
        Api.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Compile built-in shaders
        SpriteShader = new ShaderProgram(Api, DefaultShaders.SpriteVertex, DefaultShaders.SpriteFragment);
        PrimitiveShader = new ShaderProgram(Api, DefaultShaders.PrimitiveVertex, DefaultShaders.PrimitiveFragment);
        PostProcessShader = new ShaderProgram(Api, DefaultShaders.PostProcessVertex, DefaultShaders.PostProcessFragment);

        // Create batchers
        Sprites = new SpriteBatch(Api);
        Primitives = new PrimitiveBatch(Api);
        Sprites.OtherBatch = Primitives;
        Sprites.Shader = SpriteShader;
        Primitives.OtherBatch = Sprites;
        Primitives.Shader = PrimitiveShader;

        // Create white pixel texture for MakeGraphic
        _textures = new TextureCache(Api);
        WhitePixelTexture = GLTexture.SolidColor(Api, 255, 255, 255, 255);

        // Set initial viewport
        SDL.SDL_GetWindowSize(window, out int winW, out int winH);
        UpdateViewport(winW, winH);
    }

    /// <summary>
    /// Recalculate viewport for letterboxing when the window is resized.
    /// </summary>
    public void UpdateViewport(int windowW, int windowH)
    {
        float scaleX = (float)windowW / _logicalWidth;
        float scaleY = (float)windowH / _logicalHeight;
        float scale = MathF.Min(scaleX, scaleY);

        _viewportW = (int)(_logicalWidth * scale);
        _viewportH = (int)(_logicalHeight * scale);
        _viewportX = (windowW - _viewportW) / 2;
        // GL Y is bottom-up, SDL Y is top-down
        _viewportY = (windowH - _viewportH) / 2;

        // Orthographic projection: (0,0) = top-left, (logicalW, logicalH) = bottom-right
        Projection = Matrix4x4.CreateOrthographicOffCenter(
            0, _logicalWidth,
            _logicalHeight, 0, // bottom > top for Y-down
            -1, 1);
    }

    /// <summary>
    /// Restore the viewport (after FBO rendering changed it).
    /// </summary>
    public void RestoreViewport()
    {
        Api.Viewport(_viewportX, _viewportY, (uint)_viewportW, (uint)_viewportH);
        Api.Enable(EnableCap.ScissorTest);
        Api.Scissor(_viewportX, _viewportY, (uint)_viewportW, (uint)_viewportH);
    }

    /// <summary>
    /// Begin a new frame: clear screen, set up projection, start batchers.
    /// </summary>
    public void BeginFrame()
    {
        // Clear letterbox bars to black
        Api.Disable(EnableCap.ScissorTest);
        Api.ClearColor(0, 0, 0, 1);
        Api.Clear(ClearBufferMask.ColorBufferBit);

        // Set viewport and scissor for game area
        RestoreViewport();

        // Clear game area to background color
        Api.ClearColor(ClearR, ClearG, ClearB, 1);
        Api.Clear(ClearBufferMask.ColorBufferBit);

        // Apply camera rotation to projection if non-zero
        var proj = Projection;
        float camRot = Core.Eng.Camera.Rotation;
        if (camRot != 0)
        {
            float cx = _logicalWidth * 0.5f;
            float cy = _logicalHeight * 0.5f;
            proj = Matrix4x4.CreateTranslation(-cx, -cy, 0)
                 * Matrix4x4.CreateRotationZ(-camRot * MathF.PI / 180f)
                 * Matrix4x4.CreateTranslation(cx, cy, 0)
                 * Projection;
        }

        ActiveProjection = proj;

        // Start batchers with default shaders
        SpriteShader.Use();
        SpriteShader.SetMatrix4("uProjection", ActiveProjection);
        SpriteShader.SetFloat("uEffect", 0f);
        SpriteShader.SetFloat("uClipX", -1f);
        for (int i = 0; i < DefaultShaders.MaxTextureSlots; i++)
            SpriteShader.SetInt($"uTextures[{i}]", i);
        Sprites.Begin();

        PrimitiveShader.Use();
        PrimitiveShader.SetMatrix4("uProjection", ActiveProjection);
        Primitives.Begin();

        // Leave sprite shader active (most common)
        SpriteShader.Use();
    }

    /// <summary>
    /// End the frame: flush all pending draws and swap buffers.
    /// </summary>
    public void EndFrame()
    {
        Sprites.End();
        Primitives.End();
        Api.Disable(EnableCap.ScissorTest);
        CheckGLError("EndFrame");
        SDL.SDL_GL_SwapWindow(_window);
    }

    /// <summary>
    /// Debug-only check for OpenGL errors. Calls vanish in Release builds via
    /// <see cref="ConditionalAttribute"/>. Use at frame boundaries, scene transitions,
    /// and wherever a subsystem hands GL state off to another (2D ↔ 3D, pre/post FBO binds).
    /// </summary>
    [Conditional("DEBUG")]
    public void CheckGLError(string context = "")
    {
        var err = Api.GetError();
        while (err != GLEnum.NoError)
        {
            Console.WriteLine($"[GL Error] {err}{(context.Length > 0 ? $" in {context}" : "")}");
            err = Api.GetError();
        }
    }

    /// <summary>
    /// Flush both batchers. Call before render target switches.
    /// </summary>
    public void FlushAll()
    {
        Sprites.Flush();
        Primitives.Flush();
    }

    /// <summary>
    /// Ensure sprite shader is active and projection is set.
    /// Call after switching from another shader (e.g., post-process).
    /// </summary>
    public void UseSpriteShader()
    {
        SpriteShader.Use();
        SpriteShader.SetMatrix4("uProjection", ActiveProjection);
        for (int i = 0; i < DefaultShaders.MaxTextureSlots; i++)
            SpriteShader.SetInt($"uTextures[{i}]", i);
    }

    /// <summary>
    /// Ensure primitive shader is active and projection is set.
    /// </summary>
    public void UsePrimitiveShader()
    {
        PrimitiveShader.Use();
        PrimitiveShader.SetMatrix4("uProjection", ActiveProjection);
    }

    // ── Drawing Facade ────────────────────────────────────────
    // These methods are the public drawing API. Entity code should use these
    // instead of accessing Sprites/Primitives directly.

    /// <summary>Draw a textured quad. Source rect is in texture pixels, destination in screen pixels.</summary>
    public void DrawTexture(GLTexture tex, float srcX, float srcY, float srcW, float srcH,
        float dstX, float dstY, float dstW, float dstH,
        float r, float g, float b, float a,
        float angle = 0, bool flipX = false, bool flipY = false, BlendMode blend = BlendMode.Alpha)
        => Sprites.DrawQuad(tex, srcX, srcY, srcW, srcH, dstX, dstY, dstW, dstH, r, g, b, a, angle, flipX, flipY, blend);

    /// <summary>Draw a 1px line in screen space.</summary>
    public void DrawLine(float x1, float y1, float x2, float y2, float r, float g, float b, float a)
        => Primitives.DrawLine(x1, y1, x2, y2, r, g, b, a);

    /// <summary>Draw a filled rectangle in screen space.</summary>
    public void FillRect(float x, float y, float w, float h, float r, float g, float b, float a)
        => Primitives.DrawFilledRect(x, y, w, h, r, g, b, a);

    /// <summary>Draw a rectangle outline in screen space.</summary>
    public void DrawRect(float x, float y, float w, float h, float r, float g, float b, float a)
        => Primitives.DrawRect(x, y, w, h, r, g, b, a);

    /// <summary>Draw a circle outline in screen space.</summary>
    public void DrawCircle(float cx, float cy, float radius, float r, float g, float b, float a)
        => Primitives.DrawCircle(cx, cy, radius, r, g, b, a);

    /// <summary>Draw a filled circle in screen space.</summary>
    public void FillCircle(float cx, float cy, float radius, float r, float g, float b, float a)
        => Primitives.DrawFilledCircle(cx, cy, radius, r, g, b, a);

    /// <summary>Flush all pending draws to the GPU.</summary>
    public void Flush() => FlushAll();

    // ── World-Space Drawing ─────────────────────────────────────
    // These handle scroll factor, camera transform, and zoom automatically.

    /// <summary>Draw a textured quad in world space. Handles scroll factor, camera, and zoom.</summary>
    /// <param name="pivotX">Rotation pivot X in world pixels relative to entity origin (default -1 = center).</param>
    /// <param name="pivotY">Rotation pivot Y in world pixels relative to entity origin (default -1 = center).</param>
    public void DrawTextureWorld(GLTexture tex, float srcX, float srcY, float srcW, float srcH,
        float worldX, float worldY, float width, float height,
        Math.Vec2 scrollFactor, float r, float g, float b, float a,
        float angle = 0, bool flipX = false, bool flipY = false, BlendMode blend = BlendMode.Alpha,
        float? pivotX = null, float? pivotY = null)
    {
        var cam = Core.Eng.Camera;
        var screen = cam.Transform(worldX, worldY, scrollFactor);
        Sprites.DrawQuad(tex, srcX, srcY, srcW, srcH,
            screen.X, screen.Y, width * cam.Zoom, height * cam.Zoom,
            r, g, b, a, angle, flipX, flipY, blend,
            pivotX.HasValue ? pivotX.Value * cam.Zoom : null,
            pivotY.HasValue ? pivotY.Value * cam.Zoom : null);
    }

    /// <summary>Draw a filled rectangle in world space.</summary>
    public void FillRectWorld(float worldX, float worldY, float w, float h,
        Math.Vec2 scrollFactor, float r, float g, float b, float a)
    {
        var cam = Core.Eng.Camera;
        var screen = cam.Transform(worldX, worldY, scrollFactor);
        Primitives.DrawFilledRect(screen.X, screen.Y, w * cam.Zoom, h * cam.Zoom, r, g, b, a);
    }

    /// <summary>Draw a rectangle outline in world space.</summary>
    public void DrawRectWorld(float worldX, float worldY, float w, float h,
        Math.Vec2 scrollFactor, float r, float g, float b, float a)
    {
        var cam = Core.Eng.Camera;
        var screen = cam.Transform(worldX, worldY, scrollFactor);
        Primitives.DrawRect(screen.X, screen.Y, w * cam.Zoom, h * cam.Zoom, r, g, b, a);
    }

    // ── Texture Cache ───────────────────────────────────────────

    /// <summary>Load a texture from file, or return cached version.</summary>
    public GLTexture GetOrCreateTexture(string path) => _textures.GetOrCreate(path);

    /// <summary>Create a texture from pre-decoded RGBA data and cache it. Used by AssetLoader for background-loaded images.</summary>
    public GLTexture GetOrCreateTextureFromData(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        var key = System.IO.Path.GetFullPath(path);
        if (_textures.Has(key)) return GetOrCreateTexture(path);
        var tex = GLTexture.FromRGBA(Api, width, height, rgba);
        _textures.CacheExisting(key, tex);
        return tex;
    }

    /// <summary>Check if a texture is in the cache.</summary>
    public bool HasTexture(string path) => _textures.Has(path);

    /// <summary>Unload a specific texture from the cache and free its GPU memory.</summary>
    public void UnloadTexture(string path) => _textures.Unload(path);

    /// <summary>Unload all cached textures.</summary>
    public void UnloadAllTextures() => _textures.UnloadAll();

    /// <summary>Number of textures currently in the cache.</summary>
    public int TextureCacheCount => _textures.Count;

    /// <summary>Begin tracking textures for scene-scoped unloading.</summary>
    internal void BeginTextureScope() => _textures.BeginSceneScope();

    /// <summary>Unload all textures loaded since BeginTextureScope().</summary>
    internal void EndTextureScope() => _textures.EndSceneScope();

    // ── Viewport info for mouse coordinate transform ────────────

    public int ViewportX => _viewportX;
    public int ViewportY => _viewportY;
    public int ViewportW => _viewportW;
    public int ViewportH => _viewportH;
    public int LogicalWidth => _logicalWidth;
    public int LogicalHeight => _logicalHeight;

    private bool _vsync = true;

    /// <summary>Enable or disable vertical sync. True = vsync on (default), false = uncapped.</summary>
    public bool VSync
    {
        get => _vsync;
        set { _vsync = value; SDL.SDL_GL_SetSwapInterval(value ? 1 : 0); }
    }

    public void Dispose()
    {
        WhitePixelTexture.Dispose();
        _textures.Dispose();

        Sprites.Dispose();
        Primitives.Dispose();
        SpriteShader.Dispose();
        PrimitiveShader.Dispose();
        PostProcessShader.Dispose();

        SDL.SDL_GL_DeleteContext(_glContext);
    }
}
