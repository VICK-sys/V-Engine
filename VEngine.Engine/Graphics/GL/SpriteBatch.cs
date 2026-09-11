using System;
using Silk.NET.OpenGL;

namespace VEngine.Engine.Graphics.GL;

/// <summary>
/// Batches textured quads into a single draw call for efficient 2D rendering.
/// Supports multi-texture batching: up to 8 textures bound simultaneously.
/// Only flushes when texture slots are full, blend mode changes, or buffer is full.
/// </summary>
public class SpriteBatch : IDisposable
{
    public const int MaxQuads = 8192;
    private const int VerticesPerQuad = 4;
    private const int IndicesPerQuad = 6;
    private const int FloatsPerVertex = 9; // vec2 pos + vec2 uv + vec4 color + float texIndex

    private readonly Silk.NET.OpenGL.GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly float[] _vertices;

    private int _quadCount;
    private BlendMode _currentBlend = BlendMode.Alpha;

    // Multi-texture slot management
    private readonly uint[] _textureSlots = new uint[DefaultShaders.MaxTextureSlots];
    private int _textureSlotCount;

    // Cross-flush reference — set by GLRenderer
    internal PrimitiveBatch? OtherBatch;
    // Shader to activate before drawing — set by GLRenderer
    internal ShaderProgram? Shader;

    /// <summary>Number of draw calls (flushes) this frame. Reset by Begin.</summary>
    public int DrawCalls { get; private set; }
    /// <summary>Total quads submitted this frame. Reset by Begin.</summary>
    public int TotalQuads { get; private set; }

    public SpriteBatch(Silk.NET.OpenGL.GL gl)
    {
        _gl = gl;
        _vertices = new float[MaxQuads * VerticesPerQuad * FloatsPerVertex];

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

        var indices = new uint[MaxQuads * IndicesPerQuad];
        for (int i = 0; i < MaxQuads; i++)
        {
            uint b = (uint)(i * 4);
            int j = i * 6;
            indices[j + 0] = b + 0; indices[j + 1] = b + 1; indices[j + 2] = b + 2;
            indices[j + 3] = b + 2; indices[j + 4] = b + 3; indices[j + 5] = b + 0;
        }
        _ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        unsafe
        {
            fixed (uint* ptr = indices)
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                    (nuint)(indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
        }

        // Vertex attributes: pos(2f), uv(2f), color(4f), texIndex(1f)
        uint stride = FloatsPerVertex * sizeof(float);
        unsafe
        {
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(4 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, (void*)(8 * sizeof(float)));
            _gl.EnableVertexAttribArray(3);
        }

        _gl.BindVertexArray(0);
    }

    public void Begin()
    {
        _quadCount = 0;
        _textureSlotCount = 0;
        DrawCalls = 0;
        TotalQuads = 0;
    }

    public void DrawQuad(
        GLTexture texture,
        float srcX, float srcY, float srcW, float srcH,
        float dstX, float dstY, float dstW, float dstH,
        float r, float g, float b, float a,
        float angleDeg = 0, bool flipX = false, bool flipY = false,
        BlendMode blend = BlendMode.Alpha,
        float? pivotX = null, float? pivotY = null)
    {
        if (blend != _currentBlend || _quadCount >= MaxQuads)
        {
            Flush();
            _currentBlend = blend;
        }

        int texSlot = FindTextureSlot(texture.Handle);
        if (texSlot == -1)
        {
            if (_textureSlotCount >= DefaultShaders.MaxTextureSlots)
                Flush();
            texSlot = _textureSlotCount;
            _textureSlots[_textureSlotCount++] = texture.Handle;
        }

        if (_quadCount == 0)
            OtherBatch?.Flush();

        float u0 = srcX / texture.Width, v0 = srcY / texture.Height;
        float u1 = (srcX + srcW) / texture.Width, v1 = (srcY + srcH) / texture.Height;
        if (flipX) (u0, u1) = (u1, u0);
        if (flipY) (v0, v1) = (v1, v0);

        float x0, y0, x1, y1, x2, y2, x3, y3;
        if (angleDeg != 0)
        {
            float px = pivotX ?? dstW * 0.5f;
            float py = pivotY ?? dstH * 0.5f;
            float cx = dstX + px, cy = dstY + py;
            float rad = angleDeg * MathF.PI / 180f;
            float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
            RotatePoint(-px, -py, cos, sin, cx, cy, out x0, out y0);
            RotatePoint(dstW - px, -py, cos, sin, cx, cy, out x1, out y1);
            RotatePoint(dstW - px, dstH - py, cos, sin, cx, cy, out x2, out y2);
            RotatePoint(-px, dstH - py, cos, sin, cx, cy, out x3, out y3);
        }
        else
        {
            x0 = dstX; y0 = dstY;
            x1 = dstX + dstW; y1 = dstY;
            x2 = dstX + dstW; y2 = dstY + dstH;
            x3 = dstX; y3 = dstY + dstH;
        }

        float ti = texSlot;
        int vi = _quadCount * VerticesPerQuad * FloatsPerVertex;
        WriteVertex(vi + 0 * FloatsPerVertex, x0, y0, u0, v0, r, g, b, a, ti);
        WriteVertex(vi + 1 * FloatsPerVertex, x1, y1, u1, v0, r, g, b, a, ti);
        WriteVertex(vi + 2 * FloatsPerVertex, x2, y2, u1, v1, r, g, b, a, ti);
        WriteVertex(vi + 3 * FloatsPerVertex, x3, y3, u0, v1, r, g, b, a, ti);
        _quadCount++;
    }

    public unsafe void Flush()
    {
        if (_quadCount == 0) return;

        if (Shader != null)
        {
            Shader.Use();
            Shader.SetMatrix4("uProjection", Core.Eng.GL.ActiveProjection);
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        int byteCount = _quadCount * VerticesPerQuad * FloatsPerVertex * sizeof(float);
        fixed (float* ptr = _vertices)
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)byteCount, ptr);

        ApplyBlendMode(_currentBlend);

        for (int i = 0; i < _textureSlotCount; i++)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + i);
            _gl.BindTexture(TextureTarget.Texture2D, _textureSlots[i]);
        }

        if (Shader != null)
            for (int i = 0; i < _textureSlotCount; i++)
                Shader.SetInt($"uTextures[{i}]", i);

        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles,
            (uint)(_quadCount * IndicesPerQuad), DrawElementsType.UnsignedInt, null);

        DrawCalls++;
        TotalQuads += _quadCount;
        _quadCount = 0;
        _textureSlotCount = 0;
    }

    public void End() => Flush();

    private int FindTextureSlot(uint textureHandle)
    {
        for (int i = 0; i < _textureSlotCount; i++)
            if (_textureSlots[i] == textureHandle) return i;
        return -1;
    }

    private void ApplyBlendMode(BlendMode mode)
    {
        switch (mode)
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
    }

    private void WriteVertex(int offset, float x, float y, float u, float v,
        float r, float g, float b, float a, float texIndex)
    {
        _vertices[offset + 0] = x; _vertices[offset + 1] = y;
        _vertices[offset + 2] = u; _vertices[offset + 3] = v;
        _vertices[offset + 4] = r; _vertices[offset + 5] = g;
        _vertices[offset + 6] = b; _vertices[offset + 7] = a;
        _vertices[offset + 8] = texIndex;
    }

    private static void RotatePoint(float x, float y, float cos, float sin,
        float cx, float cy, out float rx, out float ry)
    {
        rx = cx + x * cos - y * sin;
        ry = cy + x * sin + y * cos;
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
    }
}

public enum BlendMode { Alpha, Additive, Multiply, None }
