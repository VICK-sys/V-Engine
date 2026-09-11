using System;
using VEngine.Engine.Math;

namespace VEngine.Engine.Physics;

/// <summary>
/// 2x2 rotation matrix for transforming shapes between local and world space.
/// </summary>
public struct Mat2x2
{
    public float M00, M01, M10, M11;

    public Mat2x2(float angle)
    {
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        M00 = cos; M01 = -sin;
        M10 = sin; M11 = cos;
    }

    public Vec2 Multiply(Vec2 v) => new(M00 * v.X + M01 * v.Y, M10 * v.X + M11 * v.Y);

    public Mat2x2 Transpose() => new() { M00 = M00, M01 = M10, M10 = M01, M11 = M11 };
}

/// <summary>
/// Position + rotation transform for rigid bodies.
/// </summary>
public struct Transform
{
    public Vec2 Position;
    public float Angle;

    public Transform(Vec2 position, float angle)
    {
        Position = position;
        Angle = angle;
    }

    /// <summary>Transform a point from local space to world space.</summary>
    public Vec2 ToWorld(Vec2 local)
    {
        var rot = new Mat2x2(Angle);
        return rot.Multiply(local) + Position;
    }

    /// <summary>Transform a point from world space to local space.</summary>
    public Vec2 ToLocal(Vec2 world)
    {
        var rot = new Mat2x2(Angle);
        var d = world - Position;
        return rot.Transpose().Multiply(d);
    }
}
