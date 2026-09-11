using System;
using System.Collections.Generic;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Collision;
using VEngine.Engine.Physics.Joints;
using VEngine.Engine.Physics.Queries;
using VEngine.Engine.Physics.Shapes;

namespace VEngine.Engine.Physics;

/// <summary>
/// The physics simulation world. Manages rigid bodies, applies forces, integrates motion,
/// detects collisions, and resolves contacts.
/// Access globally via Eng.Physics.
/// </summary>
public class PhysicsWorld
{
    public bool UseNativeSolver { get; set; } = true;
    public bool UsingNativeSolver => UseNativeSolver && Core.NativeRuntime.IsAvailable("physics_solver");
    /// <summary>Physics simulation settings (gravity, iterations, limits).</summary>
    public PhysicsSettings Settings { get; set; } = new();

    /// <summary>Enable debug rendering of physics shapes, contacts, and joints.</summary>
    public bool DebugDraw
    {
        get => _debugDraw;
        set
        {
            _debugDraw = value;
            if (value) PhysicsDebug.Enable();
            else PhysicsDebug.Disable();
        }
    }
    private bool _debugDraw;

    private readonly List<RigidBody> _bodies = new();
    private readonly Dictionary<int, RigidBody> _bodyById = new();
    private int _nextId;

    // Contact persistence for warm starting
    private readonly Dictionary<long, ContactConstraint> _contacts = new();
    private readonly List<ContactConstraint> _activeContacts = new();
    private readonly List<long> _staleKeys = new();

    // Broad phase
    private readonly PhysicsSpatialHash _broadPhase = new(64f);
    private readonly List<(RigidBody, RigidBody)> _candidatePairs = new();

    // Joints
    private readonly List<Joint> _joints = new();

    // Contact events
    internal readonly ContactManager ContactEvents = new();

    /// <summary>All bodies in the world.</summary>
    public IReadOnlyList<RigidBody> Bodies => _bodies;

    /// <summary>Number of bodies in the world.</summary>
    public int BodyCount => _bodies.Count;

    /// <summary>Number of active contact constraints this frame.</summary>
    public int ContactCount => _activeContacts.Count;

    // ── Contact Events ──

    /// <summary>Fires when two non-trigger bodies begin colliding.</summary>
    public Core.Signal<ContactInfo> OnCollisionEnter => ContactEvents.OnCollisionEnter;
    /// <summary>Fires each frame while two non-trigger bodies remain in contact.</summary>
    public Core.Signal<ContactInfo> OnCollisionStay => ContactEvents.OnCollisionStay;
    /// <summary>Fires when two non-trigger bodies stop colliding.</summary>
    public Core.Signal<ContactInfo> OnCollisionExit => ContactEvents.OnCollisionExit;
    /// <summary>Fires when a body enters a trigger zone.</summary>
    public Core.Signal<ContactInfo> OnTriggerEnter => ContactEvents.OnTriggerEnter;
    /// <summary>Fires each frame while a body is inside a trigger zone.</summary>
    public Core.Signal<ContactInfo> OnTriggerStay => ContactEvents.OnTriggerStay;
    /// <summary>Fires when a body leaves a trigger zone.</summary>
    public Core.Signal<ContactInfo> OnTriggerExit => ContactEvents.OnTriggerExit;

    // ── Joint Management ──

    /// <summary>All joints in the world.</summary>
    public IReadOnlyList<Joint> Joints => _joints;

    /// <summary>Add a joint to the world. Returns the joint for chaining.</summary>
    public T AddJoint<T>(T joint) where T : Joint
    {
        joint.World = this;
        _joints.Add(joint);
        return joint;
    }

    /// <summary>Remove a joint from the world.</summary>
    public void DestroyJoint(Joint joint)
    {
        if (joint.World != this) return;
        joint.World = null;
        _joints.Remove(joint);
    }

    // ── Body Management ──

    /// <summary>Create a new rigid body in the world.</summary>
    public RigidBody CreateBody(BodyType type, float x = 0, float y = 0)
    {
        var body = new RigidBody(_nextId++, type, x, y) { World = this };
        _bodies.Add(body);
        _bodyById[body.Id] = body;
        return body;
    }

    /// <summary>Destroy a body and remove it from the world.</summary>
    public void DestroyBody(RigidBody body)
    {
        if (body.World != this) return;
        body.World = null;
        _bodyById.Remove(body.Id);
        // Swap-with-last for O(1) removal
        int idx = _bodies.IndexOf(body);
        if (idx >= 0)
        {
            int last = _bodies.Count - 1;
            if (idx != last) _bodies[idx] = _bodies[last];
            _bodies.RemoveAt(last);
        }
    }

    /// <summary>Look up a body by its unique ID. Returns null if not found.</summary>
    public RigidBody? GetBody(int id) => _bodyById.GetValueOrDefault(id);

    /// <summary>Remove all bodies, contacts, and joints from the world. Called on scene switch.</summary>
    public void Clear()
    {
        foreach (var body in _bodies)
            body.World = null;
        _bodies.Clear();
        _bodyById.Clear();
        _contacts.Clear();
        _activeContacts.Clear();
        _broadPhase.Clear();
        _candidatePairs.Clear();
        foreach (var j in _joints) j.World = null;
        _joints.Clear();
        ContactEvents.Clear();
    }

    // ── Queries ──

    /// <summary>
    /// Cast a ray and return the closest hit. Returns true if something was hit.
    /// </summary>
    public bool Raycast(Vec2 origin, Vec2 direction, float maxDistance, out RaycastHit hit, ushort maskBits = 0xFFFF)
    {
        hit = default;
        var dir = direction.Normalized();
        float bestFraction = float.MaxValue;
        bool found = false;

        for (int i = 0; i < _bodies.Count; i++)
        {
            var body = _bodies[i];
            if (body.Shape == null) continue;
            if ((body.Filter.CategoryBits & maskBits) == 0) continue;

            // Quick AABB check
            if (!PhysicsQuery.RayVsAABB(origin, dir, maxDistance, body.AABB))
                continue;

            float fraction;
            Vec2 point, normal;

            if (body.Shape.Type == ShapeType.Circle)
                fraction = PhysicsQuery.RayVsCircle(origin, dir, maxDistance, (CircleShape)body.Shape, body.Position, body.Angle, out point, out normal);
            else
                fraction = PhysicsQuery.RayVsPolygon(origin, dir, maxDistance, (PolygonShape)body.Shape, body.Position, body.Angle, out point, out normal);

            if (fraction >= 0 && fraction < bestFraction)
            {
                bestFraction = fraction;
                hit = new RaycastHit { Body = body, Point = point, Normal = normal, Fraction = fraction };
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Cast a ray and return all hits, sorted by fraction (closest first).
    /// </summary>
    public List<RaycastHit> RaycastAll(Vec2 origin, Vec2 direction, float maxDistance, ushort maskBits = 0xFFFF)
    {
        var results = new List<RaycastHit>();
        var dir = direction.Normalized();

        for (int i = 0; i < _bodies.Count; i++)
        {
            var body = _bodies[i];
            if (body.Shape == null) continue;
            if ((body.Filter.CategoryBits & maskBits) == 0) continue;
            if (!PhysicsQuery.RayVsAABB(origin, dir, maxDistance, body.AABB)) continue;

            float fraction;
            Vec2 point, normal;

            if (body.Shape.Type == ShapeType.Circle)
                fraction = PhysicsQuery.RayVsCircle(origin, dir, maxDistance, (CircleShape)body.Shape, body.Position, body.Angle, out point, out normal);
            else
                fraction = PhysicsQuery.RayVsPolygon(origin, dir, maxDistance, (PolygonShape)body.Shape, body.Position, body.Angle, out point, out normal);

            if (fraction >= 0)
                results.Add(new RaycastHit { Body = body, Point = point, Normal = normal, Fraction = fraction });
        }

        results.Sort((a, b) => a.Fraction.CompareTo(b.Fraction));
        return results;
    }

    /// <summary>Find all bodies whose AABB overlaps the given region.</summary>
    public List<RigidBody> QueryAABB(float x, float y, float w, float h)
    {
        var query = new PhysicsAABB(x, y, x + w, y + h);
        var results = new List<RigidBody>();
        for (int i = 0; i < _bodies.Count; i++)
        {
            if (_bodies[i].Shape != null && _bodies[i].AABB.Overlaps(query))
                results.Add(_bodies[i]);
        }
        return results;
    }

    /// <summary>Find all bodies within a circle.</summary>
    public List<RigidBody> QueryCircle(Vec2 center, float radius)
    {
        var query = new PhysicsAABB(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius);
        float r2 = radius * radius;
        var results = new List<RigidBody>();
        for (int i = 0; i < _bodies.Count; i++)
        {
            var body = _bodies[i];
            if (body.Shape == null) continue;
            if (!body.AABB.Overlaps(query)) continue;
            // Check distance from circle center to body AABB center (approximate)
            var bc = body.AABB.Center;
            if (Vec2.DistanceSquared(center, bc) <= r2 + body.AABB.Extents.LengthSquared())
                results.Add(body);
        }
        return results;
    }

    /// <summary>Find the body under a world-space point, or null.</summary>
    public RigidBody? QueryPoint(Vec2 point)
    {
        for (int i = 0; i < _bodies.Count; i++)
        {
            var body = _bodies[i];
            if (body.Shape == null) continue;
            if (!body.AABB.Contains(point)) continue;
            if (body.Shape.TestPoint(body.Position, body.Angle, point))
                return body;
        }
        return null;
    }

    // ── Simulation Step ──

    /// <summary>
    /// Advance the physics simulation by dt seconds.
    /// Full pipeline: integrate velocities → broad phase → narrow phase → solve → integrate positions.
    /// </summary>
    public void Step(float dt)
    {
        if (dt <= 0) return;
        bool useNativeSolver = UsingNativeSolver;

        var gravity = Settings.Gravity;
        float maxLinSpeed = Settings.MaxLinearSpeed;
        float maxAngSpeed = Settings.MaxAngularSpeed;

        // ── 1. Integrate velocities (semi-implicit Euler) ──
        for (int i = 0; i < _bodies.Count; i++)
        {
            var body = _bodies[i];
            if (body.Type != BodyType.Dynamic || body.IsSleeping) continue;

            body.LinearVelocity += (gravity * body.GravityScale + body.Force * body.InvMass) * dt;
            body.AngularVelocity += body.Torque * body.InvInertia * dt;

            // Damping
            body.LinearVelocity *= 1f / (1f + body.LinearDamping * dt);
            body.AngularVelocity *= 1f / (1f + body.AngularDamping * dt);
        }

        // ── 2. Update AABBs ──
        for (int i = 0; i < _bodies.Count; i++)
            _bodies[i].UpdateAABB();

        // ── 3. Broad phase + Narrow phase ──
        FindContacts();

        // ── 4. Pre-solve contacts and joints ──
        for (int i = 0; i < _activeContacts.Count; i++)
        {
            if (!_activeContacts[i].IsTrigger)
            {
                if (useNativeSolver) NativeContactSolver.PreSolve(_activeContacts[i]);
                else ContactSolver.PreSolve(_activeContacts[i], dt);
            }
        }
        for (int i = 0; i < _joints.Count; i++)
            _joints[i].InitVelocityConstraints(dt);

        // ── 5. Velocity iterations (contacts + joints) ──
        for (int iter = 0; iter < Settings.VelocityIterations; iter++)
        {
            for (int i = 0; i < _joints.Count; i++)
                _joints[i].SolveVelocityConstraints(dt);
            for (int i = 0; i < _activeContacts.Count; i++)
            {
                if (!_activeContacts[i].IsTrigger)
                {
                    if (useNativeSolver) NativeContactSolver.SolveVelocity(_activeContacts[i]);
                    else ContactSolver.SolveVelocity(_activeContacts[i]);
                }
            }
        }

        // ── 6. Clamp velocities then integrate positions ──
        for (int i = 0; i < _bodies.Count; i++)
        {
            var body = _bodies[i];
            if (body.Type == BodyType.Static || body.IsSleeping) continue;

            // Clamp before integration to prevent overshoot
            float linSpeedSq = body.LinearVelocity.LengthSquared();
            if (linSpeedSq > maxLinSpeed * maxLinSpeed)
                body.LinearVelocity *= maxLinSpeed / MathF.Sqrt(linSpeedSq);
            if (body.AngularVelocity > maxAngSpeed) body.AngularVelocity = maxAngSpeed;
            else if (body.AngularVelocity < -maxAngSpeed) body.AngularVelocity = -maxAngSpeed;

            body.Position += body.LinearVelocity * dt;
            body.Angle += body.AngularVelocity * dt;
        }

        // ── 7. Position iterations (contacts + joints) ──
        for (int iter = 0; iter < Settings.PositionIterations; iter++)
        {
            bool allSolved = true;
            for (int i = 0; i < _joints.Count; i++)
            {
                if (!_joints[i].SolvePositionConstraints())
                    allSolved = false;
            }
            for (int i = 0; i < _activeContacts.Count; i++)
            {
                if (!_activeContacts[i].IsTrigger)
                {
                    if (!(useNativeSolver ? NativeContactSolver.SolvePosition(_activeContacts[i]) : ContactSolver.SolvePosition(_activeContacts[i])))
                        allSolved = false;
                }
            }
            if (allSolved) break;
        }

        // ── 8. CCD for bullet bodies ──
        SweepBullets(dt);

        // ── 9. Clear forces ──
        for (int i = 0; i < _bodies.Count; i++)
        {
            _bodies[i].Force = Vec2.Zero;
            _bodies[i].Torque = 0;
        }

        // ── 10. Sync physics positions back to entities ──
        for (int i = 0; i < _bodies.Count; i++)
        {
            if (_bodies[i].UserData is PhysicsBody pb)
                pb.SyncToEntity();
        }

        // ── 11. Sleep evaluation ──
        EvaluateSleep(dt);

        // ── 12. Clean up stale contacts ──
        CleanupStaleContacts();
    }

    private void EvaluateSleep(float dt)
    {
        float linTol = Settings.SleepLinearTolerance;
        float angTol = Settings.SleepAngularTolerance;
        float threshold = Settings.SleepTimeThreshold;
        float linTolSq = linTol * linTol;

        for (int i = 0; i < _bodies.Count; i++)
        {
            var body = _bodies[i];
            if (body.Type != BodyType.Dynamic) continue;
            if (body.IsSleeping) continue;

            if (body.LinearVelocity.LengthSquared() > linTolSq ||
                MathF.Abs(body.AngularVelocity) > angTol)
            {
                body.SleepTimer = 0;
            }
            else
            {
                body.SleepTimer += dt;
                if (body.SleepTimer >= threshold)
                {
                    body.IsSleeping = true;
                    body.LinearVelocity = Vec2.Zero;
                    body.AngularVelocity = 0;
                }
            }
        }
    }

    // ── CCD ──

    private void SweepBullets(float dt)
    {
        for (int i = 0; i < _bodies.Count; i++)
        {
            var bullet = _bodies[i];
            if (!bullet.IsBullet || bullet.Type != BodyType.Dynamic || bullet.Shape == null)
                continue;
            if (bullet.IsSleeping) continue;

            var displacement = bullet.LinearVelocity * dt;
            if (displacement.LengthSquared() < 1f) continue; // Not moving fast enough

            float earliestTOI = 1f;

            for (int j = 0; j < _bodies.Count; j++)
            {
                if (i == j) continue;
                var other = _bodies[j];
                if (other.Shape == null) continue;
                if (!CollisionFilter.ShouldCollide(bullet.Filter, other.Filter)) continue;

                float toi = TOI.ComputeTOI(bullet, displacement, other);
                if (toi < earliestTOI)
                    earliestTOI = toi;
            }

            if (earliestTOI < 1f)
            {
                // Move bullet to TOI position (rewind from post-integration position)
                bullet.Position -= displacement * (1f - earliestTOI);
            }
        }
    }

    // ── Collision Detection ──

    private void FindContacts()
    {
        // Mark all existing contacts as inactive
        foreach (var kv in _contacts)
            kv.Value.Active = false;

        _activeContacts.Clear();
        ContactEvents.BeginFrame();

        // ── Broad phase: spatial hash ──
        _broadPhase.Clear();
        _candidatePairs.Clear();

        for (int i = 0; i < _bodies.Count; i++)
        {
            if (_bodies[i].Shape != null)
                _broadPhase.Insert(_bodies[i]);
        }

        _broadPhase.FindPairs(_candidatePairs);

        // ── Narrow phase ──
        for (int p = 0; p < _candidatePairs.Count; p++)
        {
            var (bodyA, bodyB) = _candidatePairs[p];

            // Skip if both are non-dynamic
            if (bodyA.Type != BodyType.Dynamic && bodyB.Type != BodyType.Dynamic)
                continue;

            // Skip sleeping pairs
            if (bodyA.IsSleeping && bodyB.IsSleeping)
                continue;

            // Collision filter
            if (!CollisionFilter.ShouldCollide(bodyA.Filter, bodyB.Filter))
                continue;

            // Narrow phase: shape collision
            var manifold = CollisionDetection.Detect(
                bodyA.Shape!, bodyA.Position, bodyA.Angle,
                bodyB.Shape!, bodyB.Position, bodyB.Angle);

            if (manifold.ContactCount == 0)
                continue;

            // Wake sleeping body if the other is awake
            if (bodyA.IsSleeping && !bodyB.IsSleeping) bodyA.SetAwake();
            if (bodyB.IsSleeping && !bodyA.IsSleeping) bodyB.SetAwake();

            // Get or create contact constraint
            long key = ContactConstraint.MakePairKey(bodyA.Id, bodyB.Id);

            if (!_contacts.TryGetValue(key, out var cc))
            {
                cc = new ContactConstraint();
                _contacts[key] = cc;
            }

            // Update constraint
            cc.BodyA = bodyA;
            cc.BodyB = bodyB;
            cc.Normal = manifold.Normal;
            cc.Friction = ContactConstraint.MixFriction(bodyA.Material.Friction, bodyB.Material.Friction);
            cc.Restitution = ContactConstraint.MixRestitution(bodyA.Material.Restitution, bodyB.Material.Restitution);
            cc.PairKey = key;
            cc.Active = true;
            cc.IsTrigger = bodyA.IsTrigger || bodyB.IsTrigger;

            // Transfer manifold contacts with warm starting
            var oldP0 = cc.Point0;
            var oldP1 = cc.Point1;
            int oldCount = cc.PointCount;

            cc.PointCount = manifold.ContactCount;

            for (int ci = 0; ci < manifold.ContactCount; ci++)
            {
                var mc = manifold.GetContact(ci);
                ref var cp = ref (ci == 0 ? ref cc.Point0 : ref cc.Point1);

                cp.Position = mc.Position;
                cp.Penetration = mc.Penetration;

                if (ci < oldCount)
                {
                    var old = ci == 0 ? oldP0 : oldP1;
                    cp.NormalImpulse = old.NormalImpulse;
                    cp.TangentImpulse = old.TangentImpulse;
                }
                else
                {
                    cp.NormalImpulse = 0;
                    cp.TangentImpulse = 0;
                }
            }

            _activeContacts.Add(cc);

            // Track for enter/stay/exit events
            ContactEvents.TrackContact(cc);
        }

        // Fire exit events for pairs that ended
        ContactEvents.EndFrame();
    }

    private void CleanupStaleContacts()
    {
        _staleKeys.Clear();
        foreach (var kv in _contacts)
        {
            if (!kv.Value.Active)
                _staleKeys.Add(kv.Key);
        }
        for (int i = 0; i < _staleKeys.Count; i++)
            _contacts.Remove(_staleKeys[i]);
    }
}
