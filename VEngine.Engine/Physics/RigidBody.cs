using System;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Collision;
using VEngine.Engine.Physics.Shapes;

namespace VEngine.Engine.Physics;

/// <summary>
/// Defines how a rigid body behaves in the physics simulation.
/// </summary>
public enum BodyType
{
    /// <summary>Zero mass, zero velocity, never moves. Use for floors, walls, platforms.</summary>
    Static,
    /// <summary>Full physics simulation: mass, forces, collisions. Use for most game objects.</summary>
    Dynamic,
    /// <summary>User-controlled velocity, infinite mass. Pushes dynamic bodies but isn't affected by them.</summary>
    Kinematic
}

/// <summary>
/// A rigid body in the physics simulation. Created via PhysicsWorld.CreateBody().
/// </summary>
public class RigidBody
{
    // ── Identity ──

    internal int Id;
    internal PhysicsWorld? World;

    /// <summary>Arbitrary user data. PhysicsBody behavior stores the Entity reference here.</summary>
    public object? UserData;

    // ── Type ──

    private BodyType _type;

    /// <summary>Body type: Static, Dynamic, or Kinematic.</summary>
    public BodyType Type
    {
        get => _type;
        set
        {
            _type = value;
            if (value == BodyType.Static)
            {
                LinearVelocity = Vec2.Zero;
                AngularVelocity = 0;
            }
            RecomputeMassFromType();
        }
    }

    // ── Transform ──

    /// <summary>World-space position (center of mass) in pixels.</summary>
    public Vec2 Position;

    /// <summary>Rotation angle in radians.</summary>
    public float Angle;

    // ── Velocity ──

    /// <summary>Linear velocity in pixels/second.</summary>
    public Vec2 LinearVelocity;

    /// <summary>Angular velocity in radians/second.</summary>
    public float AngularVelocity;

    // ── Force accumulation (cleared each step) ──

    internal Vec2 Force;
    internal float Torque;

    // ── Mass properties ──

    /// <summary>Mass in arbitrary units. 0 for static bodies.</summary>
    public float Mass { get; private set; }

    /// <summary>Inverse mass. 0 for static/kinematic bodies.</summary>
    public float InvMass { get; private set; }

    /// <summary>Rotational inertia (moment of inertia). 0 for static bodies.</summary>
    public float Inertia { get; private set; }

    /// <summary>Inverse inertia. 0 for static/kinematic bodies or fixed rotation.</summary>
    public float InvInertia { get; private set; }

    // ── Raw mass data before type/fixedRotation adjustments ──
    private float _rawMass;
    private float _rawInertia;

    // ── Properties ──

    /// <summary>Linear velocity damping. 0 = no damping, higher = more drag. Default 0.</summary>
    public float LinearDamping;

    /// <summary>Angular velocity damping. 0 = no damping. Default 0.</summary>
    public float AngularDamping;

    /// <summary>Per-body gravity multiplier. 1 = normal, 0 = no gravity, -1 = reverse. Default 1.</summary>
    public float GravityScale = 1f;

    /// <summary>If true, prevents all rotation. Torque and angular velocity are zeroed.</summary>
    public bool FixedRotation
    {
        get => _fixedRotation;
        set
        {
            _fixedRotation = value;
            if (value) AngularVelocity = 0;
            RecomputeMassFromType();
        }
    }
    private bool _fixedRotation;

    /// <summary>If true, uses continuous collision detection to prevent tunneling. More expensive.</summary>
    public bool IsBullet;

    /// <summary>Whether this body is currently sleeping (not simulated).</summary>
    public bool IsSleeping { get; internal set; }

    /// <summary>If true, this body detects overlaps but does not produce physical response (sensor).</summary>
    public bool IsTrigger;

    /// <summary>Collision filter controlling which bodies this one can collide with.</summary>
    public CollisionFilter Filter = CollisionFilter.Default;

    /// <summary>Physical material (friction, restitution, density).</summary>
    public PhysicsMaterial Material = PhysicsMaterial.Default;

    internal float SleepTimer;

    // ── Shape ──

    /// <summary>The collider shape attached to this body. Null until a shape is added.</summary>
    public Shape? Shape { get; private set; }

    /// <summary>Cached world-space AABB, updated each step.</summary>
    internal PhysicsAABB AABB;

    /// <summary>
    /// Set the collider shape. Auto-computes mass and inertia from shape + material density.
    /// </summary>
    public void SetShape(Shape shape)
    {
        Shape = shape;
        if (_type == BodyType.Dynamic)
        {
            var md = shape.ComputeMass(Material.Density);
            _rawMass = md.Mass;
            _rawInertia = md.Inertia;
            RecomputeMassFromType();
        }
        UpdateAABB();
    }

    /// <summary>Update the cached AABB from current position/angle.</summary>
    internal void UpdateAABB()
    {
        if (Shape != null)
            AABB = Shape.ComputeAABB(Position, Angle);
    }

    // ── Construction ──

    internal RigidBody(int id, BodyType type, float x, float y)
    {
        Id = id;
        _type = type;
        Position = new Vec2(x, y);
        RecomputeMassFromType();
    }

    // ── Mass computation ──

    /// <summary>
    /// Set mass properties directly. For Phase 2, shapes will auto-compute this.
    /// </summary>
    public void SetMass(float mass, float inertia)
    {
        _rawMass = mass;
        _rawInertia = inertia;
        RecomputeMassFromType();
    }

    private void RecomputeMassFromType()
    {
        if (_type == BodyType.Dynamic)
        {
            Mass = _rawMass;
            InvMass = _rawMass > 0 ? 1f / _rawMass : 0f;
            Inertia = _rawInertia;
            InvInertia = (_rawInertia > 0 && !_fixedRotation) ? 1f / _rawInertia : 0f;
        }
        else
        {
            // Static and kinematic bodies have infinite mass (zero inverse)
            Mass = 0;
            InvMass = 0;
            Inertia = 0;
            InvInertia = 0;
        }
    }

    // ── Forces & Impulses ──

    /// <summary>Apply a force at the center of mass (accumulated over the step).</summary>
    public void ApplyForce(Vec2 force)
    {
        if (_type != BodyType.Dynamic) return;
        Force += force;
        SetAwake();
    }

    /// <summary>Apply a force at a world point, generating torque if off-center.</summary>
    public void ApplyForceAtPoint(Vec2 force, Vec2 worldPoint)
    {
        if (_type != BodyType.Dynamic) return;
        Force += force;
        Torque += Vec2.Cross(worldPoint - Position, force);
        SetAwake();
    }

    /// <summary>Apply an instantaneous impulse at the center of mass.</summary>
    public void ApplyImpulse(Vec2 impulse)
    {
        if (_type != BodyType.Dynamic) return;
        LinearVelocity += impulse * InvMass;
        SetAwake();
    }

    /// <summary>Apply an instantaneous impulse at a world point.</summary>
    public void ApplyImpulseAtPoint(Vec2 impulse, Vec2 worldPoint)
    {
        if (_type != BodyType.Dynamic) return;
        LinearVelocity += impulse * InvMass;
        AngularVelocity += Vec2.Cross(worldPoint - Position, impulse) * InvInertia;
        SetAwake();
    }

    /// <summary>Apply a torque (accumulated over the step).</summary>
    public void ApplyTorque(float torque)
    {
        if (_type != BodyType.Dynamic) return;
        Torque += torque;
        SetAwake();
    }

    // ── Transform ──

    /// <summary>Teleport the body to a new position and angle.</summary>
    public void SetTransform(Vec2 position, float angle)
    {
        Position = position;
        Angle = angle;
        SetAwake();
    }

    /// <summary>Wake this body from sleep.</summary>
    public void SetAwake()
    {
        if (IsSleeping)
        {
            IsSleeping = false;
            SleepTimer = 0;
        }
    }

    /// <summary>Get the current transform.</summary>
    public Transform GetTransform() => new(Position, Angle);
}
