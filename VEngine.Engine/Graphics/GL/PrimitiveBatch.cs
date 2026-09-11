using System;
using Silk.NET.OpenGL;

namespace VEngine.Engine.Graphics.GL;

/// <summary>
/// Batches primitives (lines, filled rects, rect outlines, circles) for efficient rendering.
/// Uses GL_LINES and GL_TRIANGLES draw modes, flushing when switching between them.
/// </summary>
public class PrimitiveBatch : IDisposable
{
    private const int MaxVertices = 16384;
    private const int FloatsPerVertex = 6; // vec2 pos + vec4 color

    private readonly Silk.NET.OpenGL.GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly float[] _vertices;

    private int _vertexCount;
    private PrimitiveType _currentMode = PrimitiveType.Triangles;

    // Cross-flush reference — set by GLRenderer
    internal SpriteBatch? OtherBatch;
    // Shader to activate before drawing — set by GLRenderer
    internal ShaderProgram? Shader;

    /// <summary>Blend mode used during Flush. Default Alpha. Set to Additive for light rendering.</summary>
    internal BlendMode Blend = BlendMode.Alpha;

    /// <summary>Number of draw calls (flushes) this frame. Reset by Begin.</summary>
    public int DrawCalls { get; private set; }

    public PrimitiveBatch(Silk.NET.OpenGL.GL gl)
    {
        _gl = gl;
        _vertices = new float[MaxVertices * FloatsPerVertex];

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer,
                (nuint)(_vertices.Length * sizeof(float)),
                null, BufferUsageARB.DynamicDraw);
        }

        // Vertex attributes: pos(2f), color(4f)
        uint stride = FloatsPerVertex * sizeof(float);
        unsafe
        {
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
        }

        _gl.BindVertexArray(0);
    }

    public void Begin()
    {
        _vertexCount = 0;
        DrawCalls = 0;
    }

    /// <summary>Draw a 1px line between two screen-space points.</summary>
    public void DrawLine(float x1, float y1, float x2, float y2, float r, float g, float b, float a)
    {
        EnsureMode(PrimitiveType.Lines, 2);
        AddVertex(x1, y1, r, g, b, a);
        AddVertex(x2, y2, r, g, b, a);
    }

    /// <summary>Draw a filled rectangle in screen-space (2 triangles).</summary>
    public void DrawFilledRect(float x, float y, float w, float h, float r, float g, float b, float a)
    {
        EnsureMode(PrimitiveType.Triangles, 6);
        // Triangle 1: TL, TR, BR
        AddVertex(x, y, r, g, b, a);
        AddVertex(x + w, y, r, g, b, a);
        AddVertex(x + w, y + h, r, g, b, a);
        // Triangle 2: BR, BL, TL
        AddVertex(x + w, y + h, r, g, b, a);
        AddVertex(x, y + h, r, g, b, a);
        AddVertex(x, y, r, g, b, a);
    }

    /// <summary>Draw a rectangle outline in screen-space (4 lines).</summary>
    public void DrawRect(float x, float y, float w, float h, float r, float g, float b, float a)
    {
        EnsureMode(PrimitiveType.Lines, 8);
        // Top
        AddVertex(x, y, r, g, b, a);
        AddVertex(x + w, y, r, g, b, a);
        // Right
        AddVertex(x + w, y, r, g, b, a);
        AddVertex(x + w, y + h, r, g, b, a);
        // Bottom
        AddVertex(x + w, y + h, r, g, b, a);
        AddVertex(x, y + h, r, g, b, a);
        // Left
        AddVertex(x, y + h, r, g, b, a);
        AddVertex(x, y, r, g, b, a);
    }

    /// <summary>Draw a circle outline in screen-space.</summary>
    /// <summary>Draw a filled quad from 4 arbitrary points (2 triangles).</summary>
    public void DrawFilledQuad(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3,
        float r, float g, float b, float a)
    {
        EnsureMode(PrimitiveType.Triangles, 6);
        AddVertex(x0, y0, r, g, b, a);
        AddVertex(x1, y1, r, g, b, a);
        AddVertex(x2, y2, r, g, b, a);
        AddVertex(x2, y2, r, g, b, a);
        AddVertex(x3, y3, r, g, b, a);
        AddVertex(x0, y0, r, g, b, a);
    }

    /// <summary>Draw a filled quad with per-vertex colors (2 triangles). Used by TrailRenderer for smooth gradients.</summary>
    public void DrawFilledQuadGradient(
        float x0, float y0, float r0, float g0, float b0, float a0,
        float x1, float y1, float r1, float g1, float b1, float a1,
        float x2, float y2, float r2, float g2, float b2, float a2,
        float x3, float y3, float r3, float g3, float b3, float a3)
    {
        EnsureMode(PrimitiveType.Triangles, 6);
        AddVertex(x0, y0, r0, g0, b0, a0);
        AddVertex(x1, y1, r1, g1, b1, a1);
        AddVertex(x2, y2, r2, g2, b2, a2);
        AddVertex(x2, y2, r2, g2, b2, a2);
        AddVertex(x3, y3, r3, g3, b3, a3);
        AddVertex(x0, y0, r0, g0, b0, a0);
    }

    /// <summary>Draw a single triangle with per-vertex colors. Used for light fans and gradients.</summary>
    public void DrawTriangle(
        float x0, float y0, float r0, float g0, float b0, float a0,
        float x1, float y1, float r1, float g1, float b1, float a1,
        float x2, float y2, float r2, float g2, float b2, float a2)
    {
        EnsureMode(PrimitiveType.Triangles, 3);
        AddVertex(x0, y0, r0, g0, b0, a0);
        AddVertex(x1, y1, r1, g1, b1, a1);
        AddVertex(x2, y2, r2, g2, b2, a2);
    }

    public void DrawCircle(float cx, float cy, float radius, float r, float g, float b, float a)
    {
        int segments = System.Math.Clamp((int)(radius * 0.5f), 16, MaxVertices / 6);
        EnsureMode(PrimitiveType.Lines, segments * 2);

        float step = MathF.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * step;
            float a1 = (i + 1) * step;
            AddVertex(cx + MathF.Cos(a0) * radius, cy + MathF.Sin(a0) * radius, r, g, b, a);
            AddVertex(cx + MathF.Cos(a1) * radius, cy + MathF.Sin(a1) * radius, r, g, b, a);
        }
    }

    /// <summary>Draw a filled circle in screen-space (triangle fan as triangles).</summary>
    public void DrawFilledCircle(float cx, float cy, float radius, float r, float g, float b, float a)
    {
        int segments = System.Math.Clamp((int)(radius * 0.5f), 16, MaxVertices / 6);
        EnsureMode(PrimitiveType.Triangles, segments * 3);

        float step = MathF.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * step;
            float a1 = (i + 1) * step;
            AddVertex(cx, cy, r, g, b, a);
            AddVertex(cx + MathF.Cos(a0) * radius, cy + MathF.Sin(a0) * radius, r, g, b, a);
            AddVertex(cx + MathF.Cos(a1) * radius, cy + MathF.Sin(a1) * radius, r, g, b, a);
        }
    }

    /// <summary>Flush all pending primitives to the GPU.</summary>
    public unsafe void Flush()
    {
        if (_vertexCount == 0) return;

        // Ensure primitive shader is active with correct projection
        if (Shader != null)
        {
            Shader.Use();
            Shader.SetMatrix4("uProjection", Core.Eng.GL.ActiveProjection);
        }

        switch (Blend)
        {
            case BlendMode.Alpha:
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                break;
            case BlendMode.Additive:
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                break;
            case BlendMode.Multiply:
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.DstColor, BlendingFactor.Zero);
                break;
            case BlendMode.None:
                _gl.Disable(EnableCap.Blend);
                break;
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        int byteCount = _vertexCount * FloatsPerVertex * sizeof(float);
        fixed (float* ptr = _vertices)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)byteCount, ptr);
        }

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(_currentMode, 0, (uint)_vertexCount);

        DrawCalls++;
        _vertexCount = 0;
    }

    public void End()
    {
        Flush();
        // done
    }

    private void EnsureMode(PrimitiveType mode, int verticesNeeded)
    {
        if (mode != _currentMode || _vertexCount + verticesNeeded > MaxVertices)
            Flush();

        if (_vertexCount == 0)
            OtherBatch?.Flush(); // preserve draw order

        _currentMode = mode;
    }

    private void AddVertex(float x, float y, float r, float g, float b, float a)
    {
        int i = _vertexCount * FloatsPerVertex;
        _vertices[i + 0] = x;
        _vertices[i + 1] = y;
        _vertices[i + 2] = r;
        _vertices[i + 3] = g;
        _vertices[i + 4] = b;
        _vertices[i + 5] = a;
        _vertexCount++;
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
    }
}
