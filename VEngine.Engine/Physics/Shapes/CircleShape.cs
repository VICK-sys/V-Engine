using System;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Collision;

namespace VEngine.Engine.Physics.Shapes;

/// <summary>
/// Circle collider shape.
/// </summary>
public class CircleShape : Shape
{
    /// <summary>Local-space offset from body center.</summary>
    public Vec2 Center;

    /// <summary>Circle radius in pixels.</summary>
    public float Radius;

    public override ShapeType Type => ShapeType.Circle;

    public CircleShape(float radius, Vec2 center = default)
    {
        Radius = radius;
        Center = center;
    }

    public override PhysicsAABB ComputeAABB(Vec2 position, float angle)
    {
        // Transform center to world space
        var worldCenter = position + Center.Rotate(angle);
        return new PhysicsAABB(
            worldCenter.X - Radius, worldCenter.Y - Radius,
            worldCenter.X + Radius, worldCenter.Y + Radius
        );
    }

    public override MassData ComputeMass(float density)
    {
        float mass = density * MathF.PI * Radius * Radius;
        // Inertia of a solid disc about its center: I = 0.5 * m * r²
        // Plus parallel axis theorem for offset center: I += m * d²
        float inertia = 0.5f * mass * Radius * Radius
                      + mass * Center.LengthSquared();
        return new MassData { Mass = mass, Inertia = inertia, Centroid = Center };
    }

    public override bool TestPoint(Vec2 position, float angle, Vec2 worldPoint)
    {
        var worldCenter = position + Center.Rotate(angle);
        return Vec2.DistanceSquared(worldCenter, worldPoint) <= Radius * Radius;
    }

    public override Shape Clone() => new CircleShape(Radius, Center);
}
