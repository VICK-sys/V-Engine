using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Queries;

/// <summary>
/// Result of a raycast against the physics world.
/// </summary>
public struct RaycastHit
{
    /// <summary>The body that was hit.</summary>
    public RigidBody Body;

    /// <summary>World-space hit point on the body surface.</summary>
    public Vec2 Point;

    /// <summary>Surface normal at the hit point (pointing away from body).</summary>
    public Vec2 Normal;

    /// <summary>Fraction along the ray (0 = origin, 1 = origin + direction * maxDistance).</summary>
    public float Fraction;
}
