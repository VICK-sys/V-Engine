using System;
using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Collision;

/// <summary>
/// Axis-aligned bounding box for broad-phase collision detection.
/// </summary>
public struct PhysicsAABB
{
    public Vec2 Min;
    public Vec2 Max;

    public PhysicsAABB(Vec2 min, Vec2 max)
    {
        Min = min;
        Max = max;
    }

    public PhysicsAABB(float minX, float minY, float maxX, float maxY)
    {
        Min = new Vec2(minX, minY);
        Max = new Vec2(maxX, maxY);
    }

    public float Width => Max.X - Min.X;
    public float Height => Max.Y - Min.Y;
    public Vec2 Center => new((Min.X + Max.X) * 0.5f, (Min.Y + Max.Y) * 0.5f);
    public Vec2 Extents => new((Max.X - Min.X) * 0.5f, (Max.Y - Min.Y) * 0.5f);
    public float Perimeter => 2f * (Width + Height);

    /// <summary>Test overlap with another AABB.</summary>
    public bool Overlaps(PhysicsAABB other)
    {
        if (Max.X < other.Min.X || Min.X > other.Max.X) return false;
        if (Max.Y < other.Min.Y || Min.Y > other.Max.Y) return false;
        return true;
    }

    /// <summary>Test if a point is inside this AABB.</summary>
    public bool Contains(Vec2 point)
    {
        return point.X >= Min.X && point.X <= Max.X
            && point.Y >= Min.Y && point.Y <= Max.Y;
    }

    /// <summary>Return the smallest AABB enclosing both AABBs.</summary>
    public static PhysicsAABB Merge(PhysicsAABB a, PhysicsAABB b)
    {
        return new PhysicsAABB(
            MathF.Min(a.Min.X, b.Min.X), MathF.Min(a.Min.Y, b.Min.Y),
            MathF.Max(a.Max.X, b.Max.X), MathF.Max(a.Max.Y, b.Max.Y)
        );
    }

    /// <summary>Expand the AABB by a margin on all sides.</summary>
    public PhysicsAABB Fatten(float margin)
    {
        return new PhysicsAABB(
            Min.X - margin, Min.Y - margin,
            Max.X + margin, Max.Y + margin
        );
    }
}
