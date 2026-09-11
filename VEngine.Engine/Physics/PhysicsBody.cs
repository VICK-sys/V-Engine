using System;
using VEngine.Engine.Core;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Collision;
using VEngine.Engine.Physics.Shapes;

namespace VEngine.Engine.Physics;

/// <summary>
/// Behavior that attaches a physics rigid body to any Entity.
/// Syncs position and angle between the Entity and the physics simulation.
/// </summary>
public class PhysicsBody : Behavior
{
    private const float DegToRad = MathF.PI / 180f;
    private const float RadToDeg = 180f / MathF.PI;

    /// <summary>The underlying rigid body in the physics world.</summary>
    public RigidBody Body { get; private set; } = null!;

    private readonly BodyType _initialType;
    private PhysicsMaterial _material = PhysicsMaterial.Default;
    private CollisionFilter _filter = CollisionFilter.Default;
    private Shape? _pendingShape;
    private bool _isTrigger;
    private bool _fixedRotation;
    private float _gravityScale = 1f;
    private float _linearDamping;
    private float _angularDamping;

    // For detecting user teleports
    private Vec2 _lastSyncedPosition;
    private float _lastSyncedAngle;

    // ── Per-body collision events ──

    /// <summary>Fires when this body begins colliding with another.</summary>
    public readonly Signal<ContactInfo> OnCollisionEnter = new();

    /// <summary>Fires when this body stops colliding with another.</summary>
    public readonly Signal<ContactInfo> OnCollisionExit = new();

    /// <summary>Fires when this body enters a trigger zone (or a trigger enters this body).</summary>
    public readonly Signal<ContactInfo> OnTriggerEnter = new();

    /// <summary>Fires when this body exits a trigger zone.</summary>
    public readonly Signal<ContactInfo> OnTriggerExit = new();

    /// <summary>Create a PhysicsBody behavior with the given body type.</summary>
    public PhysicsBody(BodyType type = BodyType.Dynamic)
    {
        _initialType = type;
    }

    // ── Shape Setup (chainable) ──

    /// <summary>Set a box collider. Chainable.</summary>
    public PhysicsBody SetBox(float width, float height)
    {
        var shape = new BoxShape(width, height);
        if (Body != null)
            ApplyShape(shape);
        else
            _pendingShape = shape;
        return this;
    }

    /// <summary>Set a circle collider. Chainable.</summary>
    public PhysicsBody SetCircle(float radius)
    {
        var shape = new CircleShape(radius);
        if (Body != null)
            ApplyShape(shape);
        else
            _pendingShape = shape;
        return this;
    }

    /// <summary>Set a polygon collider from vertices. Chainable.</summary>
    public PhysicsBody SetPolygon(Vec2[] vertices)
    {
        var shape = new PolygonShape();
        shape.Set(vertices);
        if (Body != null)
            ApplyShape(shape);
        else
            _pendingShape = shape;
        return this;
    }

    // ── Properties ──

    /// <summary>Physical material (friction, restitution, density).</summary>
    public PhysicsMaterial Material
    {
        get => Body?.Material ?? _material;
        set { _material = value; if (Body != null) Body.Material = value; }
    }

    /// <summary>Collision filter (category/mask bits).</summary>
    public CollisionFilter Filter
    {
        get => Body?.Filter ?? _filter;
        set { _filter = value; if (Body != null) Body.Filter = value; }
    }

    /// <summary>Whether this body is a trigger (sensor) — detects overlap without physical response.</summary>
    public bool IsTrigger
    {
        get => Body?.IsTrigger ?? _isTrigger;
        set { _isTrigger = value; if (Body != null) Body.IsTrigger = value; }
    }

    /// <summary>Per-body gravity multiplier. Default 1.</summary>
    public float GravityScale
    {
        get => Body?.GravityScale ?? _gravityScale;
        set { _gravityScale = value; if (Body != null) Body.GravityScale = value; }
    }

    /// <summary>Lock rotation.</summary>
    public bool FixedRotation
    {
        get => Body?.FixedRotation ?? _fixedRotation;
        set { _fixedRotation = value; if (Body != null) Body.FixedRotation = value; }
    }

    /// <summary>Linear velocity damping.</summary>
    public float LinearDamping
    {
        get => Body?.LinearDamping ?? _linearDamping;
        set { _linearDamping = value; if (Body != null) Body.LinearDamping = value; }
    }

    /// <summary>Angular velocity damping.</summary>
    public float AngularDamping
    {
        get => Body?.AngularDamping ?? _angularDamping;
        set { _angularDamping = value; if (Body != null) Body.AngularDamping = value; }
    }

    // ── Force/Impulse API (delegates to Body) ──

    /// <summary>Apply a force at the center of mass.</summary>
    public void ApplyForce(Vec2 force) => Body?.ApplyForce(force);

    /// <summary>Apply a force at a world point.</summary>
    public void ApplyForceAtPoint(Vec2 force, Vec2 worldPoint) => Body?.ApplyForceAtPoint(force, worldPoint);

    /// <summary>Apply an instantaneous impulse at the center of mass.</summary>
    public void ApplyImpulse(Vec2 impulse) => Body?.ApplyImpulse(impulse);

    /// <summary>Apply an instantaneous impulse at a world point.</summary>
    public void ApplyImpulseAtPoint(Vec2 impulse, Vec2 worldPoint) => Body?.ApplyImpulseAtPoint(impulse, worldPoint);

    /// <summary>Apply a torque.</summary>
    public void ApplyTorque(float torque) => Body?.ApplyTorque(torque);

    // ── Behavior Lifecycle ──

    public override void OnAttach()
    {
        var world = Eng.Physics;
        Body = world.CreateBody(_initialType, Owner.Position.X, Owner.Position.Y);
        Body.Angle = Owner.Angle * DegToRad;
        Body.Material = _material;
        Body.Filter = _filter;
        Body.IsTrigger = _isTrigger;
        Body.GravityScale = _gravityScale;
        Body.FixedRotation = _fixedRotation;
        Body.LinearDamping = _linearDamping;
        Body.AngularDamping = _angularDamping;
        Body.UserData = this;

        if (_pendingShape != null)
        {
            ApplyShape(_pendingShape);
            _pendingShape = null;
        }

        _lastSyncedPosition = Owner.Position;
        _lastSyncedAngle = Owner.Angle;
    }

    public override void Update(float dt)
    {
        if (Body == null) return;

        // Detect user teleport: if Entity.Position was changed externally, push to Body
        if (Owner.Position.X != _lastSyncedPosition.X || Owner.Position.Y != _lastSyncedPosition.Y
            || Owner.Angle != _lastSyncedAngle)
        {
            Body.SetTransform(Owner.Position, Owner.Angle * DegToRad);
        }
    }

    public override void OnDestroy()
    {
        if (Body != null)
        {
            Body.UserData = null;
            Eng.Physics.DestroyBody(Body);
            Body = null!;
        }
        OnCollisionEnter.Clear();
        OnCollisionExit.Clear();
        OnTriggerEnter.Clear();
        OnTriggerExit.Clear();
    }

    // ── Sync: called by PhysicsWorld after Step ──

    /// <summary>Write physics body position/angle back to the Entity.</summary>
    internal void SyncToEntity()
    {
        if (Body == null || Owner == null) return;

        Owner.Position = Body.Position;
        Owner.Angle = Body.Angle * RadToDeg;

        _lastSyncedPosition = Owner.Position;
        _lastSyncedAngle = Owner.Angle;
    }

    private void ApplyShape(Shape shape)
    {
        Body.Material = _material;
        Body.SetShape(shape);
    }
}
