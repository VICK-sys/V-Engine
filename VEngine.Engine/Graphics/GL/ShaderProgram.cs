using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;

namespace VEngine.Engine.Graphics.GL;

/// <summary>
/// Compiles and links a GLSL vertex+fragment shader program.
/// Caches uniform locations for efficient setting.
/// </summary>
public class ShaderProgram : IDisposable
{
    private readonly Silk.NET.OpenGL.GL _gl;
    private readonly Dictionary<string, int> _uniformLocations = new();

    public uint Handle { get; }

    public ShaderProgram(Silk.NET.OpenGL.GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;

        uint vs = CompileShader(ShaderType.VertexShader, vertexSource);
        uint fs = CompileShader(ShaderType.FragmentShader, fragmentSource);

        Handle = _gl.CreateProgram();
        _gl.AttachShader(Handle, vs);
        _gl.AttachShader(Handle, fs);
        _gl.LinkProgram(Handle);

        _gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            string log = _gl.GetProgramInfoLog(Handle);
            _gl.DeleteProgram(Handle);
            _gl.DeleteShader(vs);
            _gl.DeleteShader(fs);
            throw new Exception($"Shader link failed: {log}");
        }

        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
    }

    public void Use() => _gl.UseProgram(Handle);

    public void SetInt(string name, int value)
    {
        _gl.Uniform1(GetLocation(name), value);
    }

    public void SetFloat(string name, float value)
    {
        _gl.Uniform1(GetLocation(name), value);
    }

    public void SetVec2(string name, float x, float y)
    {
        _gl.Uniform2(GetLocation(name), x, y);
    }

    public void SetVec4(string name, float x, float y, float z, float w)
    {
        _gl.Uniform4(GetLocation(name), x, y, z, w);
    }

    public unsafe void SetMatrix4(string name, Matrix4x4 mat)
    {
        _gl.UniformMatrix4(GetLocation(name), 1, false, (float*)&mat);
    }

    private int GetLocation(string name)
    {
        if (_uniformLocations.TryGetValue(name, out int loc))
            return loc;
        loc = _gl.GetUniformLocation(Handle, name);
        _uniformLocations[name] = loc;
        return loc;
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new Exception($"Shader compile failed ({type}): {log}");
        }
        return shader;
    }

    public void Dispose()
    {
        _gl.DeleteProgram(Handle);
    }
}
