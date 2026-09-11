using System;

namespace VEngine.Engine.Math;

public struct Vec2
{
    public float X;
    public float Y;

    public static readonly Vec2 Zero = new(0, 0);
    public static readonly Vec2 One = new(1, 1);
    public static readonly Vec2 Up = new(0, -1);
    public static readonly Vec2 Down = new(0, 1);
    public static readonly Vec2 Left = new(-1, 0);
    public static readonly Vec2 Right = new(1, 0);

    public Vec2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public float Length() => MathF.Sqrt(X * X + Y * Y);
    public float LengthSquared() => X * X + Y * Y;

    public Vec2 Normalized()
    {
        float len = Length();
        return len > 0 ? new Vec2(X / len, Y / len) : Zero;
    }

    public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    public static float Distance(Vec2 a, Vec2 b) => (a - b).Length();
    public static float DistanceSquared(Vec2 a, Vec2 b) => (a - b).LengthSquared();

    public static Vec2 Lerp(Vec2 a, Vec2 b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>Angle in radians from this vector to another (atan2).</summary>
    public static float AngleTo(Vec2 from, Vec2 to) =>
        MathF.Atan2(to.Y - from.Y, to.X - from.X);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(float s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator /(Vec2 a, float s) => new(a.X / s, a.Y / s);

    /// <summary>2D cross product (scalar). Returns the z-component of the 3D cross product.</summary>
    public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>Cross product of scalar and vector: returns (-s * v.Y, s * v.X).</summary>
    public static Vec2 Cross(float s, Vec2 v) => new(-s * v.Y, s * v.X);

    /// <summary>Cross product of vector and scalar: returns (s * v.Y, -s * v.X).</summary>
    public static Vec2 Cross(Vec2 v, float s) => new(s * v.Y, -s * v.X);

    /// <summary>Returns the perpendicular vector (-Y, X), rotated 90 degrees counter-clockwise.</summary>
    public Vec2 Perpendicular() => new(-Y, X);

    /// <summary>Rotates this vector by the given angle in radians.</summary>
    public Vec2 Rotate(float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vec2(X * cos - Y * sin, X * sin + Y * cos);
    }

    /// <summary>Creates a unit vector from an angle in radians.</summary>
    public static Vec2 FromAngle(float radians) => new(MathF.Cos(radians), MathF.Sin(radians));

    public override string ToString() => $"({X:F1}, {Y:F1})";
}
