using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Joints;

/// <summary>
/// Base class for physics constraints between two bodies.
/// </summary>
public abstract class Joint
{
    /// <summary>First body.</summary>
    public RigidBody BodyA { get; internal set; } = null!;

    /// <summary>Second body (null for MouseJoint).</summary>
    public RigidBody? BodyB { get; internal set; }

    /// <summary>Anchor point on body A in local space.</summary>
    public Vec2 LocalAnchorA;

    /// <summary>Anchor point on body B in local space.</summary>
    public Vec2 LocalAnchorB;

    /// <summary>If true, connected bodies can collide with each other. Default false.</summary>
    public bool CollideConnected;

    internal PhysicsWorld? World;

    /// <summary>Get anchor A in world space.</summary>
    public Vec2 WorldAnchorA => BodyA.GetTransform().ToWorld(LocalAnchorA);

    /// <summary>Get anchor B in world space.</summary>
    public Vec2 WorldAnchorB => BodyB != null ? BodyB.GetTransform().ToWorld(LocalAnchorB) : LocalAnchorB;

    internal abstract void InitVelocityConstraints(float dt);
    internal abstract void SolveVelocityConstraints(float dt);
    internal abstract bool SolvePositionConstraints();
}
