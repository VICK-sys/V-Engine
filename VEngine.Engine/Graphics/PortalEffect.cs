using System;
using Silk.NET.OpenGL;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;
using VEngine.Engine.Math;

namespace VEngine.Engine.Graphics;

/// <summary>
/// A swirling portal/mirror visual effect rendered with a custom shader.
/// Procedural animated noise with radial energy rings and rim glow.
///
/// Usage:
///   var portal = new PortalEffect(100, 50, 20, 80);
///   portal.ColorA = Color.Cyan;
///   portal.ColorB = Color.Purple;
///   scene.Add(portal);
/// </summary>
public class PortalEffect : Entity
{
    private static ShaderProgram? _shader;
    private static uint _vao, _vbo;
    private static bool _ready;

    /// <summary>Inner color of the portal.</summary>
    public Color ColorA = new(100, 180, 255);

    /// <summary>Outer/rim color of the portal.</summary>
    public Color ColorB = new(180, 80, 255);

    /// <summary>Animation speed multiplier.</summary>
    public float Speed = 1f;

    public PortalEffect(float x, float y, float width, float height) : base(x, y)
    {
        BaseWidth = width;
        BaseHeight = height;
    }

    public override void Draw()
    {
        EnsureGPU();

        var cam = Eng.Camera;
        var screen = cam.Transform(Position.X, Position.Y, ScrollFactor);
        float w = Width * cam.Zoom;
        float h = Height * cam.Zoom;

        // Build quad at screen position
        Span<float> verts = stackalloc float[]
        {
            screen.X,     screen.Y,     0, 0,
            screen.X + w, screen.Y,     1, 0,
            screen.X + w, screen.Y + h, 1, 1,
            screen.X,     screen.Y,     0, 0,
            screen.X + w, screen.Y + h, 1, 1,
            screen.X,     screen.Y + h, 0, 1,
        };

        var gl = Eng.GL.Api;
        Eng.GL.FlushAll();

        _shader!.Use();
        _shader.SetMatrix4("uProjection", Eng.GL.ActiveProjection);
        _shader.SetFloat("uTime", Eng.Time.Total * Speed);
        _shader.SetVec4("uColorA", ColorA.R / 255f, ColorA.G / 255f, ColorA.B / 255f, ColorA.A / 255f);
        _shader.SetVec4("uColorB", ColorB.R / 255f, ColorB.G / 255f, ColorB.B / 255f, ColorB.A / 255f);
        _shader.SetFloat("uAlpha", System.Math.Min(ColorA.A, ColorB.A) / 255f);

        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* ptr = verts)
                gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(verts.Length * sizeof(float)), ptr);
        }

        gl.BindVertexArray(_vao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        gl.BindVertexArray(0);

        Eng.GL.UseSpriteShader();
    }

    private static unsafe void EnsureGPU()
    {
        if (_ready) return;
        _ready = true;

        var gl = Eng.GL.Api;
        _shader = new ShaderProgram(gl, Vertex, Fragment);

        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, 24 * sizeof(float), null, BufferUsageARB.DynamicDraw);

        uint stride = 4 * sizeof(float);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.BindVertexArray(0);
    }

    /// <summary>Release the shared GPU resources. Called from Game.Shutdown so InitHeadless after
    /// a normal run starts with a clean slate (otherwise _ready stays true with stale handles).</summary>
    internal static void Shutdown()
    {
        if (!_ready) return;
        var gl = Eng.GL.Api;
        _shader?.Dispose();
        _shader = null;
        if (_vbo != 0) { gl.DeleteBuffer(_vbo); _vbo = 0; }
        if (_vao != 0) { gl.DeleteVertexArray(_vao); _vao = 0; }
        _ready = false;
    }

    // ── Shaders ───────────────────────────────────────────────

    private const string Vertex = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aTexCoord;
uniform mat4 uProjection;
out vec2 vUV;
void main()
{
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
    vUV = aTexCoord;
}
";

    private const string Fragment = @"#version 330 core
in vec2 vUV;
uniform float uTime;
uniform vec4 uColorA;
uniform vec4 uColorB;
uniform float uAlpha;
out vec4 FragColor;

float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x),
               mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

void main()
{
    vec2 uv = vUV * 2.0 - 1.0;

    // Ellipse distance
    float dist = length(uv);
    if (dist > 1.0) discard;

    // Polar coords
    float angle = atan(uv.y, uv.x);

    // Swirling noise — multiple layers at different scales and speeds
    float s1 = noise(vec2(angle * 2.0 + uTime * 1.5, dist * 4.0 - uTime * 2.0));
    float s2 = noise(vec2(angle * 3.0 - uTime * 1.0, dist * 6.0 + uTime * 1.5));
    float s3 = noise(vec2(dist * 8.0 - uTime * 3.0, angle * 1.5 + uTime * 0.8));
    float pattern = (s1 + s2 * 0.5 + s3 * 0.25) / 1.75;

    // Radial energy rings
    pattern *= sin(dist * 12.0 - uTime * 4.0) * 0.3 + 0.7;

    // Edge mask
    float edge = smoothstep(1.0, 0.5, dist);

    // Rim glow
    float rim = smoothstep(0.5, 1.0, dist) * smoothstep(1.0, 0.85, dist);

    // Color gradient inner -> outer
    vec4 color = mix(uColorA, uColorB, dist);
    color.rgb *= pattern * edge + rim * 1.5;
    color.a = edge * 0.9;

    // Bright center
    color.rgb += smoothstep(0.3, 0.0, dist) * 0.4;

    color.a *= uAlpha;
    FragColor = color;
}
";
}
