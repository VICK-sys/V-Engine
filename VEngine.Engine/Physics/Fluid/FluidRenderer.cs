using System;
using Silk.NET.OpenGL;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Shapes;
using GDraw = VEngine.Engine.Graphics.Draw;

namespace VEngine.Engine.Physics.Fluid;

/// <summary>
/// Renders SPH fluid particles. Circles mode draws individual droplets.
/// Metaball mode blends particles into a cohesive liquid surface via
/// density accumulation in an FBO and a threshold shader.
/// </summary>
public class FluidRenderer : IDisposable
{
    public enum RenderMode { Circles, Metaball }

    // ── Shared ──
    public RenderMode Mode = RenderMode.Metaball;
    public float ParticleRadius = 2.5f;

    // ── Circle Mode ──
    public Color ParticleColor = new(60, 140, 255, 180);
    public Color ParticleHighlight = new(150, 200, 255, 220);

    // ── Metaball Mode ──
    /// <summary>Blob visual size multiplier. Visual radius = ParticleRadius * MetaballScale.</summary>
    public float MetaballScale = 3.0f;
    /// <summary>Per-particle density contribution at center (0-1). Higher = blobs merge more easily.</summary>
    public float MetaballIntensity = 0.4f;
    /// <summary>Density threshold for the liquid surface (0-1). Lower = larger blobs.</summary>
    public float Threshold = 0.2f;
    /// <summary>Liquid surface/shallow color.</summary>
    public Color WaterColor = new(40, 120, 255, 220);
    /// <summary>Liquid interior/deep color (where many particles overlap).</summary>
    public Color WaterColorDeep = new(15, 50, 160, 240);
    /// <summary>Bright edge highlight at the liquid boundary.</summary>
    public Color EdgeColor = new(160, 210, 255, 255);
    /// <summary>Width of the edge highlight (0-1, fraction of density range above threshold).</summary>
    public float EdgeWidth = 0.15f;

    // ── GL Resources (lazy-initialized on first metaball draw) ──
    private VEngine.Engine.Graphics.GL.Framebuffer? _fbo;
    private GLTexture? _softTex;
    private ShaderProgram? _shader;
    private uint _quadVao, _quadVbo;
    private bool _quadReady;
    private int _fboW, _fboH;

    public void Draw(FluidParticle[] particles, int length)
    {
        if (Mode == RenderMode.Circles)
            DrawCircles(particles, length);
        else
            DrawMetaball(particles, length);
    }

    // ── Circle Mode ──

    private void DrawCircles(FluidParticle[] particles, int length)
    {
        for (int i = 0; i < length; i++)
        {
            if (!particles[i].Active) continue;

            float speed = particles[i].Velocity.Length();
            float t = System.Math.Clamp(speed / 300f, 0f, 1f);

            byte r = (byte)(ParticleColor.R + (ParticleHighlight.R - ParticleColor.R) * t);
            byte g = (byte)(ParticleColor.G + (ParticleHighlight.G - ParticleColor.G) * t);
            byte b = (byte)(ParticleColor.B + (ParticleHighlight.B - ParticleColor.B) * t);
            byte a = ParticleColor.A;

            GDraw.FillCircle(particles[i].Position.X, particles[i].Position.Y,
                ParticleRadius, r, g, b, a);
        }
    }

    // ── Metaball Mode ──

    private void DrawMetaball(FluidParticle[] particles, int length)
    {
        if (Eng.GL == null) return;

        var gl = Eng.GL.Api;
        int w = Eng.Width;
        int h = Eng.Height;
        if (w <= 0 || h <= 0) return;

        EnsureResources(gl, w, h);

        // 1. Flush pending scene draws before switching FBO
        Eng.GL.FlushAll();

        // 2. Render particle density field into FBO with additive blending
        _fbo!.Bind();
        gl.ClearColor(0, 0, 0, 0);
        gl.Clear(ClearBufferMask.ColorBufferBit);

        Eng.GL.UseSpriteShader();

        float baseBlobSize = ParticleRadius * MetaballScale * 2f * Eng.Camera.Zoom;
        float ci = MetaballIntensity;
        int texW = _softTex!.Width, texH = _softTex.Height;

        for (int i = 0; i < length; i++)
        {
            if (!particles[i].Active) continue;

            var screen = Eng.Camera.WorldToScreen(
                particles[i].Position.X, particles[i].Position.Y);

            // Merged particles render as larger, brighter blobs
            float pw = particles[i].Weight;
            if (pw <= 0) pw = 1f;
            float intensity = ci * MathF.Sqrt(pw);
            float half = baseBlobSize * 0.5f;

            Eng.GL.DrawTexture(_softTex!,
                0, 0, texW, texH,
                screen.X - half, screen.Y - half, baseBlobSize, baseBlobSize,
                intensity, intensity, intensity, intensity,
                blend: BlendMode.Additive);
        }

        // 3. Flush additive draws into the FBO
        Eng.GL.FlushAll();

        // 4. Erase density where rigid bodies are — water wraps around surfaces
        MaskBodies(gl);

        // 5. Restore default framebuffer and viewport
        _fbo.Unbind();
        Eng.GL.RestoreViewport();

        // 6. Composite the density FBO onto the scene with the threshold shader
        _shader!.Use();
        _shader.SetInt("uTexture", 0);
        _shader.SetFloat("uThreshold", Threshold);
        _shader.SetFloat("uEdgeWidth", EdgeWidth);
        _shader.SetVec2("uTexelSize", 1f / w, 1f / h);
        _shader.SetFloat("uTime", Eng.Time.Total);
        _shader.SetVec4("uColor",
            WaterColor.R / 255f, WaterColor.G / 255f,
            WaterColor.B / 255f, WaterColor.A / 255f);
        _shader.SetVec4("uColorDeep",
            WaterColorDeep.R / 255f, WaterColorDeep.G / 255f,
            WaterColorDeep.B / 255f, WaterColorDeep.A / 255f);
        _shader.SetVec4("uEdgeColor",
            EdgeColor.R / 255f, EdgeColor.G / 255f,
            EdgeColor.B / 255f, EdgeColor.A / 255f);

        _fbo.ColorAttachment.Bind(0);

        // Alpha blend so water composites over whatever is behind it
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        gl.BindVertexArray(_quadVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        gl.BindVertexArray(0);

        // 7. Restore engine shader state for subsequent draws
        Eng.GL.UseSpriteShader();
    }

    // ── Body Masking ──
    // Draw rigid body shapes as black into the density FBO (blending disabled)
    // so that the water surface wraps around objects instead of rendering over them.

    /// <summary>Padding in world pixels around each body mask. Prevents water from touching the body edge.</summary>
    public float MaskPadding = 1.5f;

    private void MaskBodies(Silk.NET.OpenGL.GL gl)
    {
        var physics = Eng.Physics;
        if (physics == null || physics.BodyCount == 0) return;

        // Disable blending: writes (0,0,0,0) directly to FBO, zeroing density
        gl.Disable(EnableCap.Blend);

        Eng.GL.UsePrimitiveShader();
        var cam = Eng.Camera;
        float zoom = cam.Zoom;
        var prims = Eng.GL.Primitives;

        for (int b = 0; b < physics.Bodies.Count; b++)
        {
            var body = physics.Bodies[b];
            if (body.Shape == null) continue;

            if (body.Shape is CircleShape circle)
            {
                var center = body.Position + circle.Center.Rotate(body.Angle);
                var sc = cam.WorldToScreen(center.X, center.Y);
                float r = (circle.Radius + MaskPadding) * zoom;
                prims.DrawFilledCircle(sc.X, sc.Y, r, 0, 0, 0, 0);
            }
            else if (body.Shape is PolygonShape poly)
            {
                // Compute centroid of world-space polygon for fan center
                // (body.Position can be at the edge, creating degenerate triangles)
                var centroid = Vec2.Zero;
                for (int v = 0; v < poly.Count; v++)
                    centroid += poly.GetWorldVertex(v, body.Position, body.Angle);
                centroid /= poly.Count;
                var sc = cam.WorldToScreen(centroid.X, centroid.Y);

                for (int i = 0; i < poly.Count; i++)
                {
                    int next = (i + 1) % poly.Count;
                    var v0 = poly.GetWorldVertex(i, body.Position, body.Angle);
                    var v1 = poly.GetWorldVertex(next, body.Position, body.Angle);

                    // Expand outward from centroid by MaskPadding
                    if (MaskPadding > 0)
                    {
                        var out0 = (v0 - centroid);
                        float len0 = out0.Length();
                        if (len0 > 0.01f) v0 += out0 / len0 * MaskPadding;

                        var out1 = (v1 - centroid);
                        float len1 = out1.Length();
                        if (len1 > 0.01f) v1 += out1 / len1 * MaskPadding;
                    }

                    var s0 = cam.WorldToScreen(v0.X, v0.Y);
                    var s1 = cam.WorldToScreen(v1.X, v1.Y);

                    prims.DrawTriangle(
                        sc.X, sc.Y, 0, 0, 0, 0,
                        s0.X, s0.Y, 0, 0, 0, 0,
                        s1.X, s1.Y, 0, 0, 0, 0);
                }
            }
        }

        prims.Flush();

        // Restore blending for subsequent draws
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    // ── Resource Management ──

    private void EnsureResources(Silk.NET.OpenGL.GL gl, int w, int h)
    {
        // Recreate FBO if resolution changed
        if (_fbo != null && (_fboW != w || _fboH != h))
        {
            _fbo.Dispose();
            _fbo = null;
        }

        if (_fbo == null)
        {
            _fbo = new VEngine.Engine.Graphics.GL.Framebuffer(gl, w, h);
            _fboW = w;
            _fboH = h;
        }

        _softTex ??= CreateSoftCircleTexture(gl);
        _shader ??= new ShaderProgram(gl, ThresholdVertex, ThresholdFragment);

        if (!_quadReady)
            CreateFullscreenQuad(gl);
    }

    /// <summary>
    /// Generate a 64x64 radial gradient texture: bright at center, smooth falloff to zero.
    /// Used as the "blob" shape for each particle in metaball rendering.
    /// </summary>
    private static GLTexture CreateSoftCircleTexture(Silk.NET.OpenGL.GL gl, int size = 64)
    {
        var pixels = new byte[size * size * 4];
        float center = size * 0.5f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f - center) / center;
            float dy = (y + 0.5f - center) / center;
            float d2 = dx * dx + dy * dy;

            // Quartic falloff: (1 - d²)² — smooth, reaches zero at d=1
            float a = d2 >= 1f ? 0f : (1f - d2) * (1f - d2);
            byte val = (byte)(a * 255f);

            int idx = (y * size + x) * 4;
            pixels[idx] = val;     // R
            pixels[idx + 1] = val; // G
            pixels[idx + 2] = val; // B
            pixels[idx + 3] = val; // A
        }

        // Linear filtering for smooth blob edges
        return GLTexture.FromRGBA(gl, size, size, pixels, nearest: false);
    }

    private unsafe void CreateFullscreenQuad(Silk.NET.OpenGL.GL gl)
    {
        // Clip-space quad: covers entire viewport, UV maps to FBO texture
        float[] verts =
        {
            // pos(x,y)  uv(s,t)
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
        fixed (float* ptr = verts)
            gl.BufferData(BufferTargetARB.ArrayBuffer,
                (nuint)(verts.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
        // layout(0) = vec2 position
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false,
            4 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(0);
        // layout(1) = vec2 texcoord
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false,
            4 * sizeof(float), (void*)(2 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.BindVertexArray(0);
        _quadReady = true;
    }

    public void Dispose()
    {
        _fbo?.Dispose();
        _fbo = null;
        _softTex?.Dispose();
        _softTex = null;
        _shader?.Dispose();
        _shader = null;

        if (_quadReady && Eng.GL != null)
        {
            var gl = Eng.GL.Api;
            gl.DeleteBuffer(_quadVbo);
            gl.DeleteVertexArray(_quadVao);
            _quadReady = false;
        }
    }

    // ── Threshold Shader ──
    // Vertex: pass-through clip-space quad (same as PostProcess)
    // Fragment: reads accumulated density from FBO, thresholds into water surface

    private const string ThresholdVertex = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aTexCoord;
out vec2 vTexCoord;
void main() {
    gl_Position = vec4(aPos, 0.0, 1.0);
    vTexCoord = aTexCoord;
}";

    private const string ThresholdFragment = @"#version 330 core
in vec2 vTexCoord;

uniform sampler2D uTexture;
uniform vec4 uColor;
uniform vec4 uColorDeep;
uniform vec4 uEdgeColor;
uniform float uThreshold;
uniform float uEdgeWidth;
uniform vec2 uTexelSize;
uniform float uTime;

out vec4 FragColor;

// ── Value noise for organic surface distortion ──
float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}
float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1,0)), f.x),
               mix(hash(i + vec2(0,1)), hash(i + vec2(1,1)), f.x), f.y);
}

void main()
{
    // ── Sample density + 4 neighbors for gradient / normals ──
    float density = texture(uTexture, vTexCoord).r;
    float dL = texture(uTexture, vTexCoord - vec2(uTexelSize.x, 0)).r;
    float dR = texture(uTexture, vTexCoord + vec2(uTexelSize.x, 0)).r;
    float dU = texture(uTexture, vTexCoord - vec2(0, uTexelSize.y)).r;
    float dD = texture(uTexture, vTexCoord + vec2(0, uTexelSize.y)).r;

    // ── Organic edge: animated noise distorts the threshold boundary ──
    vec2 px = vTexCoord / uTexelSize;
    float n1 = vnoise(px * 0.06 + uTime * 1.2);
    float n2 = vnoise(px * 0.12 - uTime * 0.8 + 50.0);
    float distort = (n1 + n2 - 1.0) * 0.025;
    float adj = density + distort;

    if (adj < uThreshold) discard;

    float t = clamp((adj - uThreshold) / max(1.0 - uThreshold, 0.001), 0.0, 1.0);

    // ── Surface normal estimated from density gradient ──
    vec2 grad = vec2(dR - dL, dD - dU);
    float gradLen = length(grad);
    vec2 normal = gradLen > 0.001 ? -grad / gradLen : vec2(0.0, -1.0);

    // ── 2D lighting (light from upper-left) ──
    vec2 lightDir = normalize(vec2(0.5, -0.7));
    float diffuse = max(dot(normal, lightDir), 0.0);

    // Specular glint (Blinn-Phong)
    vec2 halfVec = normalize(lightDir + vec2(0.0, -1.0));
    float spec = pow(max(dot(normal, halfVec), 0.0), 28.0);

    // ── Edge / depth bands ──
    float edge = 1.0 - smoothstep(0.0, uEdgeWidth, t);
    float depth = smoothstep(0.0, 0.5, t);

    // ── Compose final color ──
    vec4 body = mix(uColor, uColorDeep, depth);
    vec4 color = mix(body, uEdgeColor, edge * 0.35);

    // Diffuse shading — sides facing away from light are darker
    color.rgb *= 0.85 + diffuse * 0.15;

    // Specular highlight near the surface edge
    color.rgb += spec * edge * vec3(0.45, 0.50, 0.55);

    // Fresnel rim glow at the boundary
    color.rgb += edge * edge * uEdgeColor.rgb * 0.2;

    // Subtle caustic shimmer in the interior
    float caustic = vnoise(px * 0.15 + uTime * 2.5) * vnoise(px * 0.22 - uTime * 1.8);
    color.rgb += caustic * (1.0 - edge) * 0.06;

    FragColor = color;
}";
}
