using VEngine.Engine.Math;
using VEngine.Engine.Physics.Collision;

namespace VEngine.Engine.Physics.Shapes;

public enum ShapeType { Circle, Polygon }

/// <summary>
/// Mass data computed from a shape and density.
/// </summary>
public struct MassData
{
    public float Mass;
    public float Inertia;
    public Vec2 Centroid;
}

/// <summary>
/// Abstract base for physics collider shapes.
/// </summary>
public abstract class Shape
{
    public abstract ShapeType Type { get; }

    /// <summary>Compute the world-space AABB for this shape at the given transform.</summary>
    public abstract PhysicsAABB ComputeAABB(Vec2 position, float angle);

    /// <summary>Compute mass, inertia, and centroid for the given density.</summary>
    public abstract MassData ComputeMass(float density);

    /// <summary>Test if a world-space point is inside this shape at the given transform.</summary>
    public abstract bool TestPoint(Vec2 position, float angle, Vec2 worldPoint);

    /// <summary>Create a deep copy of this shape.</summary>
    public abstract Shape Clone();
}
