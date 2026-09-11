using System;
using System.Collections.Generic;
using Silk.NET.OpenGL;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Composable post-processing pipeline. Renders the scene to an FBO, then chains
/// shader passes via ping-pong FBOs. Each pass reads one FBO and writes to the other.
/// </summary>
public class PostProcess
{
    private GL.Framebuffer? _pingFBO, _pongFBO;
    private readonly List<PostProcessPass> _passes = new();
    private int _width, _height;
    private bool _ready;
    private bool _active;

    // Persistent fullscreen quad geometry
    private uint _quadVao;
    private uint _quadVbo;
    private bool _quadReady;

    /// <summary>Master enable. When false, Begin/End are no-ops and scene renders directly to screen.</summary>
    public bool Enabled { get; set; }

    /// <summary>The built-in CRT pass, or null if not enabled.</summary>
    public PostProcessPass? CRT { get; private set; }

    /// <summary>All registered passes (read-only).</summary>
    public IReadOnlyList<PostProcessPass> Passes => _passes;

    public bool Init(int width, int height)
    {
        _width = width;
        _height = height;
        _ready = true;
        Console.WriteLine("[PostProcess] Initialized (GLSL pipeline).");
        return true;
    }

    // ── Pass Management ─────────────────────────────────────────

    /// <summary>Add a pass with an existing compiled shader.</summary>
    public PostProcessPass AddPass(ShaderProgram shader)
    {
        var pass = new PostProcessPass(shader);
        _passes.Add(pass);
        return pass;
    }

    /// <summary>Add a pass from fragment shader source. Uses the default post-process vertex shader.</summary>
    public PostProcessPass AddPass(string fragmentSource)
    {
        var shader = new ShaderProgram(Eng.GL.Api, DefaultShaders.PostProcessVertex, fragmentSource);
        var pass = new PostProcessPass(shader, ownsShader: true);
        _passes.Add(pass);
        return pass;
    }

    /// <summary>Remove a pass from the pipeline.</summary>
    public void RemovePass(PostProcessPass pass)
    {
        _passes.Remove(pass);
        pass.Dispose();
    }

    /// <summary>Remove all passes.</summary>
    public void ClearPasses()
    {
        foreach (var p in _passes) p.Dispose();
        _passes.Clear();
        CRT = null;
    }

    /// <summary>Enable the built-in CRT effect (chromatic aberration + scanlines + vignette).</summary>
    public void EnableCRT(float chromaIntensity = 2f, float scanlineAlpha = 25f, float vignetteStrength = 0.5f)
    {
        if (CRT != null) return; // already enabled

        CRT = AddPass(Eng.GL.PostProcessShader);
        CRT.SetUniforms = (shader, time) =>
        {
            shader.SetFloat("uTime", time);
            shader.SetVec2("uResolution", _width, _height);
            shader.SetFloat("uChromaIntensity", chromaIntensity);
            shader.SetFloat("uScanlineAlpha", scanlineAlpha / 255f);
            shader.SetFloat("uVignetteStrength", vignetteStrength);
        };
    }

    /// <summary>Disable the built-in CRT effect.</summary>
    public void DisableCRT()
    {
        if (CRT == null) return;
        _passes.Remove(CRT);
        CRT = null; // don't dispose — shader is owned by GLRenderer
    }

    /// <summary>The built-in Video Glitch pass, or null if not enabled.</summary>
    public PostProcessPass? Glitch { get; private set; }

    /// <summary>Enable the built-in video glitch effect (noise displacement, channel shift, interference).</summary>
    public void EnableGlitch(float intensity = 1f)
    {
        if (Glitch != null) return;
        Glitch = AddPass(GL.DefaultShaders.VideoGlitchFragment);
        Glitch.SetUniforms = (shader, time) =>
        {
            shader.SetFloat("uTime", time);
            shader.SetVec2("uResolution", _width, _height);
            shader.SetFloat("uIntensity", intensity);
        };
    }

    /// <summary>Disable the built-in video glitch effect.</summary>
    public void DisableGlitch()
    {
        if (Glitch == null) return;
        _passes.Remove(Glitch);
        Glitch.Dispose();
        Glitch = null;
    }

    /// <summary>The built-in Coldberg TV pass, or null if not enabled.</summary>
    public PostProcessPass? ColdbergTV { get; private set; }

    /// <summary>Enable the Coldberg TV shader (full CRT: curvature, scan shift, frame roll, color drift, noise).</summary>
    public void EnableColdbergTV(float intensity = 1f)
    {
        if (ColdbergTV != null) return;
        ColdbergTV = AddPass(GL.DefaultShaders.ColdbergTVFragment);
        ColdbergTV.SetUniforms = (shader, time) =>
        {
            shader.SetFloat("uTime", time);
            shader.SetVec2("uResolution", _width, _height);
            shader.SetFloat("uIntensity", intensity);
        };
    }

    /// <summary>Disable the Coldberg TV shader.</summary>
    public void DisableColdbergTV()
    {
        if (ColdbergTV == null) return;
        _passes.Remove(ColdbergTV);
        ColdbergTV.Dispose();
        ColdbergTV = null;
    }

    // ── Frame Lifecycle ─────────────────────────────────────────

    /// <summary>Begin post-processing. Scene draws go to the ping FBO.</summary>
    public void Begin()
    {
        if (!_ready || !Enabled || _passes.Count == 0) return;
        EnsureFBOs();
        Eng.GL.FlushAll();
        _pingFBO!.Bind();
        Eng.GL.Api.ClearColor(Eng.GL.ClearR, Eng.GL.ClearG, Eng.GL.ClearB, 1);
        Eng.GL.Api.Clear(ClearBufferMask.ColorBufferBit);
        _active = true;
    }

    /// <summary>End post-processing. Chains all enabled passes, then draws result to screen.</summary>
    public void End(float time)
    {
        if (!_active) return;
        _active = false;

        Eng.GL.FlushAll();
        _pingFBO!.Unbind();
        Eng.GL.RestoreViewport();
        EnsureQuad();

        // Count enabled passes (no allocation — iterate _passes directly)
        int enabledCount = 0;
        foreach (var p in _passes)
            if (p.Enabled) enabledCount++;

        if (enabledCount == 0)
        {
            // No passes enabled — draw ping directly to screen
            DrawFBOToScreen(_pingFBO!.ColorAttachment);
            Eng.GL.UseSpriteShader();
            return;
        }

        // Chain passes via ping-pong (iterate _passes directly, skip disabled — zero allocation)
        var readFBO = _pingFBO!;
        var writeFBO = _pongFBO!;
        int remaining = enabledCount;

        foreach (var pass in _passes)
        {
            if (!pass.Enabled) continue;
            remaining--;
            bool isLast = remaining == 0;

            if (isLast)
            {
                Eng.GL.RestoreViewport();
                pass.Shader.Use();
                pass.Shader.SetInt("uTexture", 0);
                pass.SetUniforms?.Invoke(pass.Shader, time);
                readFBO.ColorAttachment.Bind(0);
                DrawFullscreenQuad();
            }
            else
            {
                writeFBO.Bind();
                Eng.GL.Api.ClearColor(0, 0, 0, 1);
                Eng.GL.Api.Clear(ClearBufferMask.ColorBufferBit);

                pass.Shader.Use();
                pass.Shader.SetInt("uTexture", 0);
                pass.SetUniforms?.Invoke(pass.Shader, time);
                readFBO.ColorAttachment.Bind(0);
                DrawFullscreenQuad();

                writeFBO.Unbind();
                (readFBO, writeFBO) = (writeFBO, readFBO);
            }
        }

        Eng.GL.RestoreViewport();
        Eng.GL.UseSpriteShader();
    }

    // ── Internal ────────────────────────────────────────────────

    private void DrawFBOToScreen(GLTexture texture)
    {
        var gl = Eng.GL.Api;
        // Use a simple passthrough — no shader needed, just blit
        // Actually use the sprite batch for simplicity
        Eng.GL.UseSpriteShader();
        Eng.GL.DrawTexture(texture, 0, 0, _width, _height, 0, 0, _width, _height, 1, 1, 1, 1);
        Eng.GL.Flush();
    }

    private void DrawFullscreenQuad()
    {
        var gl = Eng.GL.Api;
        gl.Disable(EnableCap.Blend);
        gl.BindVertexArray(_quadVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        gl.BindVertexArray(0);
        gl.Enable(EnableCap.Blend);
    }

    private void EnsureFBOs()
    {
        // Recreate if dimensions changed (window resize)
        if (_pingFBO != null && (_pingFBO.Width != _width || _pingFBO.Height != _height))
        {
            _pingFBO.Dispose();
            _pongFBO?.Dispose();
            _pingFBO = null;
            _pongFBO = null;
        }
        if (_pingFBO != null) return;
        _pingFBO = new GL.Framebuffer(Eng.GL.Api, _width, _height);
        _pongFBO = new GL.Framebuffer(Eng.GL.Api, _width, _height);
    }

    /// <summary>Update internal dimensions. Called when the logical resolution changes.</summary>
    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
    }

    private unsafe void EnsureQuad()
    {
        if (_quadReady) return;

        var gl = Eng.GL.Api;
        float[] vertices =
        {
            -1f, -1f,  0f, 0f,
             1f, -1f,  1f, 0f,
             1f,  1f,  1f, 1f,
            -1f, -1f,  0f, 0f,
             1f,  1f,  1f, 1f,
            -1f,  1f,  0f, 1f,
        };

        _quadVao = gl.GenVertexArray();
        _quadVbo = gl.GenBuffer();
        gl.BindVertexArray(_quadVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
        fixed (float* ptr = vertices)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(float)), ptr,
                BufferUsageARB.StaticDraw);
        }
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.BindVertexArray(0);
        _quadReady = true;
    }

    public void Destroy()
    {
        foreach (var p in _passes) p.Dispose();
        _passes.Clear();
        CRT = null;

        _pingFBO?.Dispose();
        _pongFBO?.Dispose();
        _pingFBO = null;
        _pongFBO = null;

        if (_quadReady)
        {
            var gl = Eng.GL.Api;
            gl.DeleteBuffer(_quadVbo);
            gl.DeleteVertexArray(_quadVao);
            _quadReady = false;
        }
    }
}

/// <summary>
/// A single post-processing pass. Holds a shader and optional uniform setup callback.
/// </summary>
public class PostProcessPass
{
    public ShaderProgram Shader { get; }
    public bool Enabled { get; set; } = true;

    /// <summary>Called each frame to set shader uniforms. Receives (shader, time).</summary>
    public Action<ShaderProgram, float>? SetUniforms;

    private readonly bool _ownsShader;

    internal PostProcessPass(ShaderProgram shader, bool ownsShader = false)
    {
        Shader = shader;
        _ownsShader = ownsShader;
    }

    internal void Dispose()
    {
        if (_ownsShader)
            Shader.Dispose();
    }
}
