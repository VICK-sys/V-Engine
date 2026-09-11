using System;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Collision;

namespace VEngine.Engine.Physics.Shapes;

/// <summary>
/// Convex polygon collider shape. Up to 8 vertices, CCW winding.
/// </summary>
public class PolygonShape : Shape
{
    public const int MaxVertices = 8;

    /// <summary>Local-space vertices in counter-clockwise order.</summary>
    public Vec2[] Vertices = Array.Empty<Vec2>();

    /// <summary>Outward-facing edge normals, one per edge.</summary>
    public Vec2[] Normals = Array.Empty<Vec2>();

    /// <summary>Number of vertices/edges.</summary>
    public int Count;

    public override ShapeType Type => ShapeType.Polygon;

    public PolygonShape() { }

    /// <summary>Set vertices from an array. Computes convex hull and normals.</summary>
    public void Set(Vec2[] points)
    {
        if (points.Length < 3)
            throw new ArgumentException("Polygon needs at least 3 vertices.");

        var hull = ComputeConvexHull(points);
        if (hull.Length > MaxVertices)
            throw new ArgumentException($"Convex hull has {hull.Length} vertices, max is {MaxVertices}.");
        Count = hull.Length;
        Vertices = hull;
        Normals = new Vec2[Count];
        ComputeNormals();
    }

    /// <summary>Set as an axis-aligned box centered at the origin.</summary>
    public void SetAsBox(float halfWidth, float halfHeight)
    {
        Count = 4;
        Vertices = new Vec2[4];
        Normals = new Vec2[4];

        // CCW winding (Y-down: bottom-left, bottom-right, top-right, top-left)
        Vertices[0] = new Vec2(-halfWidth, -halfHeight);
        Vertices[1] = new Vec2(halfWidth, -halfHeight);
        Vertices[2] = new Vec2(halfWidth, halfHeight);
        Vertices[3] = new Vec2(-halfWidth, halfHeight);

        ComputeNormals();
    }

    /// <summary>Set as a box with center offset and rotation.</summary>
    public void SetAsBox(float halfWidth, float halfHeight, Vec2 center, float angle)
    {
        SetAsBox(halfWidth, halfHeight);

        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);

        for (int i = 0; i < Count; i++)
        {
            var v = Vertices[i];
            Vertices[i] = new Vec2(
                cos * v.X - sin * v.Y + center.X,
                sin * v.X + cos * v.Y + center.Y
            );
        }

        ComputeNormals();
    }

    private void ComputeNormals()
    {
        for (int i = 0; i < Count; i++)
        {
            int next = (i + 1) % Count;
            var edge = Vertices[next] - Vertices[i];
            // Outward normal (right-hand perpendicular for CCW winding, Y-down)
            Normals[i] = new Vec2(edge.Y, -edge.X).Normalized();
        }
    }

    public override PhysicsAABB ComputeAABB(Vec2 position, float angle)
    {
        if (Count == 0) return new PhysicsAABB(position, position);

        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);

        var v0 = Vertices[0];
        float wx = cos * v0.X - sin * v0.Y + position.X;
        float wy = sin * v0.X + cos * v0.Y + position.Y;
        float minX = wx, minY = wy, maxX = wx, maxY = wy;

        for (int i = 1; i < Count; i++)
        {
            var v = Vertices[i];
            wx = cos * v.X - sin * v.Y + position.X;
            wy = sin * v.X + cos * v.Y + position.Y;
            if (wx < minX) minX = wx;
            if (wy < minY) minY = wy;
            if (wx > maxX) maxX = wx;
            if (wy > maxY) maxY = wy;
        }

        return new PhysicsAABB(minX, minY, maxX, maxY);
    }

    public override MassData ComputeMass(float density)
    {
        if (Count < 3) return default;

        // Compute area, centroid, and inertia using triangle decomposition
        float area = 0;
        float inertia = 0;
        var centroid = Vec2.Zero;
        const float inv3 = 1f / 3f;

        for (int i = 0; i < Count; i++)
        {
            var v1 = Vertices[i];
            var v2 = Vertices[(i + 1) % Count];

            float cross = Vec2.Cross(v1, v2);
            float triArea = 0.5f * cross;
            area += triArea;
            centroid += (v1 + v2) * (inv3 * triArea);

            float ix = v1.X * v1.X + v1.X * v2.X + v2.X * v2.X;
            float iy = v1.Y * v1.Y + v1.Y * v2.Y + v2.Y * v2.Y;
            inertia += cross * (ix + iy);
        }

        if (MathF.Abs(area) < 1e-10f) return default;

        centroid *= 1f / area;
        float mass = density * MathF.Abs(area);
        inertia = density * MathF.Abs(inertia) / 6f;
        // Shift inertia to centroid using parallel axis theorem (subtract)
        inertia -= mass * centroid.LengthSquared();
        inertia = MathF.Abs(inertia);

        return new MassData { Mass = mass, Inertia = inertia, Centroid = centroid };
    }

    public override bool TestPoint(Vec2 position, float angle, Vec2 worldPoint)
    {
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);

        // Transform to local space
        var d = worldPoint - position;
        var local = new Vec2(cos * d.X + sin * d.Y, -sin * d.X + cos * d.Y);

        for (int i = 0; i < Count; i++)
        {
            float dot = Vec2.Dot(Normals[i], local - Vertices[i]);
            if (dot > 0) return false;
        }
        return true;
    }

    public override Shape Clone()
    {
        var clone = new PolygonShape { Count = Count };
        clone.Vertices = new Vec2[Count];
        clone.Normals = new Vec2[Count];
        Array.Copy(Vertices, clone.Vertices, Count);
        Array.Copy(Normals, clone.Normals, Count);
        return clone;
    }

    /// <summary>Get a world-space vertex at the given body transform.</summary>
    public Vec2 GetWorldVertex(int index, Vec2 position, float angle)
    {
        var v = Vertices[index];
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        return new Vec2(cos * v.X - sin * v.Y + position.X, sin * v.X + cos * v.Y + position.Y);
    }

    /// <summary>Get a world-space normal at the given body rotation.</summary>
    public Vec2 GetWorldNormal(int index, float angle)
    {
        var n = Normals[index];
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        return new Vec2(cos * n.X - sin * n.Y, sin * n.X + cos * n.Y);
    }

    // ── Convex Hull (Gift Wrapping) ──

    private static Vec2[] ComputeConvexHull(Vec2[] points)
    {
        int n = points.Length;
        if (n < 3) return (Vec2[])points.Clone();

        // Find leftmost point
        int start = 0;
        for (int i = 1; i < n; i++)
        {
            if (points[i].X < points[start].X ||
                (points[i].X == points[start].X && points[i].Y < points[start].Y))
                start = i;
        }

        var hull = new Vec2[MaxVertices];
        int hullCount = 0;
        int current = start;

        do
        {
            if (hullCount >= MaxVertices) break;
            hull[hullCount++] = points[current];
            int next = 0;

            for (int i = 1; i < n; i++)
            {
                if (i == current) continue;
                if (next == current)
                {
                    next = i;
                    continue;
                }

                var cross = Vec2.Cross(points[i] - points[current], points[next] - points[current]);
                if (cross > 0)  // CCW winding (Y-down): pick counter-clockwise turn
                    next = i;
                else if (cross == 0)
                {
                    // Collinear — pick the farther point
                    if (Vec2.DistanceSquared(points[i], points[current]) >
                        Vec2.DistanceSquared(points[next], points[current]))
                        next = i;
                }
            }

            current = next;
        } while (current != start);

        var result = new Vec2[hullCount];
        Array.Copy(hull, result, hullCount);
        return result;
    }
}
