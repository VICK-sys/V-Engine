using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Collision;

/// <summary>
/// Pre-computed solver data for a single contact point.
/// </summary>
internal struct ContactPointData
{
    // Relative vectors from body centers to contact point
    public Vec2 RelativeA;
    public Vec2 RelativeB;

    // Effective masses along normal and tangent
    public float NormalMass;
    public float TangentMass;

    // Velocity bias for restitution
    public float VelocityBias;

    // Accumulated impulses (warm starting, clamped)
    public float NormalImpulse;
    public float TangentImpulse;

    // Original manifold data
    public Vec2 Position;
    public float Penetration;
}

/// <summary>
/// A contact constraint between two bodies, holding solver data for 1-2 contact points.
/// Persisted across frames for warm starting.
/// </summary>
internal class ContactConstraint
{
    public RigidBody BodyA = null!;
    public RigidBody BodyB = null!;

    public Vec2 Normal;
    public Vec2 Tangent;

    public float Friction;
    public float Restitution;

    public ContactPointData Point0;
    public ContactPointData Point1;
    public int PointCount;

    /// <summary>Unique key for this body pair (for persistence lookup).</summary>
    public long PairKey;

    /// <summary>Whether this constraint was active this frame (for cleanup).</summary>
    public bool Active;

    /// <summary>Whether this is a trigger (sensor) contact — detected but not resolved.</summary>
    public bool IsTrigger;

    /// <summary>Mix friction: geometric mean.</summary>
    public static float MixFriction(float a, float b)
    {
        return MathF.Sqrt(a * b);
    }

    /// <summary>Mix restitution: max.</summary>
    public static float MixRestitution(float a, float b)
    {
        return MathF.Max(a, b);
    }

    /// <summary>Compute a stable pair key from two body IDs (order-independent).</summary>
    public static long MakePairKey(int idA, int idB)
    {
        int lo = idA < idB ? idA : idB;
        int hi = idA < idB ? idB : idA;
        return ((long)lo << 32) | (uint)hi;
    }
}
