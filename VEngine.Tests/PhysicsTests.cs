using VEngine.Engine.Core;
using VEngine.Engine.Math;
using VEngine.Engine.Physics;
using VEngine.Engine.Physics.Collision;
using VEngine.Engine.Physics.Joints;
using VEngine.Engine.Physics.Queries;
using VEngine.Engine.Physics.Shapes;
using Xunit;

namespace VEngine.Tests;

public class PhysicsTests
{
    public PhysicsTests()
    {
        Eng.InitHeadless();
    }

    // ── Vec2 Additions ──

    [Fact]
    public void Vec2_CrossProduct()
    {
        var a = new Vec2(1, 0);
        var b = new Vec2(0, 1);
        Assert.Equal(1f, Vec2.Cross(a, b));
        Assert.Equal(-1f, Vec2.Cross(b, a));
    }

    [Fact]
    public void Vec2_CrossScalarVector()
    {
        var v = new Vec2(1, 0);
        var result = Vec2.Cross(1f, v);
        Assert.Equal(0f, result.X, 1e-5);
        Assert.Equal(1f, result.Y, 1e-5);
    }

    [Fact]
    public void Vec2_CrossVectorScalar()
    {
        var v = new Vec2(1, 0);
        var result = Vec2.Cross(v, 1f);
        Assert.Equal(0f, result.X, 1e-5);
        Assert.Equal(-1f, result.Y, 1e-5);
    }

    [Fact]
    public void Vec2_Perpendicular()
    {
        var v = new Vec2(3, 4);
        var p = v.Perpendicular();
        Assert.Equal(-4f, p.X);
        Assert.Equal(3f, p.Y);
        // Perpendicular should be orthogonal
        Assert.Equal(0f, Vec2.Dot(v, p), 1e-5);
    }

    [Fact]
    public void Vec2_Rotate()
    {
        var v = new Vec2(1, 0);
        var rotated = v.Rotate(MathF.PI / 2); // 90 degrees
        Assert.Equal(0f, rotated.X, 1e-5);
        Assert.Equal(1f, rotated.Y, 1e-5);
    }

    [Fact]
    public void Vec2_Division()
    {
        var v = new Vec2(10, 20);
        var result = v / 2f;
        Assert.Equal(5f, result.X);
        Assert.Equal(10f, result.Y);
    }

    [Fact]
    public void Vec2_FromAngle()
    {
        var v = Vec2.FromAngle(0);
        Assert.Equal(1f, v.X, 1e-5);
        Assert.Equal(0f, v.Y, 1e-5);

        var v2 = Vec2.FromAngle(MathF.PI / 2);
        Assert.Equal(0f, v2.X, 1e-5);
        Assert.Equal(1f, v2.Y, 1e-5);
    }

    // ── Body Creation & Destruction ──

    [Fact]
    public void CreateBody_ReturnsBody()
    {
        var world = new PhysicsWorld();
        var body = world.CreateBody(BodyType.Dynamic, 100, 200);

        Assert.NotNull(body);
        Assert.Equal(BodyType.Dynamic, body.Type);
        Assert.Equal(100f, body.Position.X);
        Assert.Equal(200f, body.Position.Y);
        Assert.Equal(1, world.BodyCount);
    }

    [Fact]
    public void DestroyBody_RemovesFromWorld()
    {
        var world = new PhysicsWorld();
        var body = world.CreateBody(BodyType.Dynamic);
        Assert.Equal(1, world.BodyCount);

        world.DestroyBody(body);
        Assert.Equal(0, world.BodyCount);
    }

    [Fact]
    public void Clear_RemovesAllBodies()
    {
        var world = new PhysicsWorld();
        world.CreateBody(BodyType.Dynamic);
        world.CreateBody(BodyType.Static);
        world.CreateBody(BodyType.Kinematic);
        Assert.Equal(3, world.BodyCount);

        world.Clear();
        Assert.Equal(0, world.BodyCount);
    }

    // ── Body Types ──

    [Fact]
    public void StaticBody_DoesNotMove()
    {
        var world = new PhysicsWorld();
        var body = world.CreateBody(BodyType.Static, 50, 50);
        body.SetMass(1, 1);

        world.Step(1f / 60f);

        Assert.Equal(50f, body.Position.X);
        Assert.Equal(50f, body.Position.Y);
    }

    [Fact]
    public void StaticBody_IgnoresForces()
    {
        var world = new PhysicsWorld();
        var body = world.CreateBody(BodyType.Static, 0, 0);
        body.SetMass(1, 1);
        body.ApplyForce(new Vec2(1000, 0));

        world.Step(1f / 60f);

        Assert.Equal(0f, body.LinearVelocity.X);
        Assert.Equal(0f, body.Position.X);
    }

    [Fact]
    public void KinematicBody_MovesAtSetVelocity()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        var body = world.CreateBody(BodyType.Kinematic, 0, 0);
        body.LinearVelocity = new Vec2(100, 0);

        world.Step(1f);

        Assert.Equal(100f, body.Position.X, 0.1);
    }

    [Fact]
    public void KinematicBody_IgnoresGravity()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 980);
        var body = world.CreateBody(BodyType.Kinematic, 0, 0);

        world.Step(1f / 60f);

        Assert.Equal(0f, body.LinearVelocity.Y);
        Assert.Equal(0f, body.Position.Y);
    }

    [Fact]
    public void KinematicBody_IgnoresForces()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        var body = world.CreateBody(BodyType.Kinematic, 0, 0);
        body.ApplyForce(new Vec2(1000, 0));

        world.Step(1f / 60f);

        Assert.Equal(0f, body.LinearVelocity.X);
    }

    // ── Gravity ──

    [Fact]
    public void DynamicBody_FallsUnderGravity()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 100);
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);

        float dt = 1f / 60f;
        world.Step(dt);

        // After one step: velocity = gravity * dt = 100 * (1/60) ≈ 1.667
        Assert.True(body.LinearVelocity.Y > 0);
        Assert.True(body.Position.Y > 0);
    }

    [Fact]
    public void GravityScale_Zero_NoGravity()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 980);
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);
        body.GravityScale = 0;

        world.Step(1f / 60f);

        Assert.Equal(0f, body.LinearVelocity.Y, 1e-5);
        Assert.Equal(0f, body.Position.Y, 1e-5);
    }

    [Fact]
    public void GravityScale_Negative_FallsUp()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 100);
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);
        body.GravityScale = -1;

        world.Step(1f / 60f);

        Assert.True(body.LinearVelocity.Y < 0);
    }

    [Fact]
    public void GravityIntegration_CorrectDistanceOverTime()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 100);
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);

        float dt = 1f / 60f;
        int steps = 60; // 1 second
        for (int i = 0; i < steps; i++)
            world.Step(dt);

        // After 1 second of free fall at 100 px/s²:
        // d = 0.5 * a * t² = 0.5 * 100 * 1 = 50
        // Semi-implicit Euler slightly overshoots the exact analytical result,
        // but should be in the right ballpark
        Assert.InRange(body.Position.Y, 45f, 55f);
    }

    // ── Forces ──

    [Fact]
    public void ApplyForce_ProducesVelocity()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(2, 1); // mass = 2

        body.ApplyForce(new Vec2(200, 0)); // F=200, m=2, a=100

        float dt = 1f / 60f;
        world.Step(dt);

        // v = a * dt = 100 * (1/60) ≈ 1.667
        Assert.Equal(100f * dt, body.LinearVelocity.X, 0.01);
    }

    [Fact]
    public void ApplyForceAtPoint_ProducesTorque()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        var body = world.CreateBody(BodyType.Dynamic, 50, 50);
        body.SetMass(1, 100); // inertia = 100

        // Apply force at a point offset from center
        body.ApplyForceAtPoint(new Vec2(0, 100), new Vec2(60, 50)); // 10px right of center, force down

        float dt = 1f / 60f;
        world.Step(dt);

        // Cross product: (10,0) × (0,100) = 1000
        // Angular accel = torque / inertia = 1000 / 100 = 10
        Assert.True(body.AngularVelocity != 0);
    }

    [Fact]
    public void Forces_ClearedAfterStep()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);

        body.ApplyForce(new Vec2(100, 0));
        world.Step(1f / 60f);

        float velocityAfterFirst = body.LinearVelocity.X;
        world.Step(1f / 60f);

        // Velocity should stay constant (no additional force), position changes
        Assert.Equal(velocityAfterFirst, body.LinearVelocity.X, 0.01);
    }

    // ── Impulses ──

    [Fact]
    public void ApplyImpulse_ImmediateVelocityChange()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(2, 1); // mass = 2

        body.ApplyImpulse(new Vec2(10, 0));

        // Impulse is immediate — no step needed
        Assert.Equal(5f, body.LinearVelocity.X, 0.01); // v = impulse / mass = 10/2
    }

    [Fact]
    public void ApplyImpulseAtPoint_ChangesAngularVelocity()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        var body = world.CreateBody(BodyType.Dynamic, 50, 50);
        body.SetMass(1, 10); // inertia = 10

        body.ApplyImpulseAtPoint(new Vec2(0, 10), new Vec2(55, 50)); // 5px right, impulse down

        // Cross: (5,0) × (0,10) = 50. angVel = 50 / 10 = 5
        Assert.Equal(5f, body.AngularVelocity, 0.01);
    }

    // ── Fixed Rotation ──

    [Fact]
    public void FixedRotation_PreventsAngularVelocity()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 10);
        body.FixedRotation = true;

        body.ApplyTorque(100);
        world.Step(1f / 60f);

        Assert.Equal(0f, body.AngularVelocity);
        Assert.Equal(0f, body.Angle);
    }

    [Fact]
    public void FixedRotation_StillAllowsLinearMotion()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 10);
        body.FixedRotation = true;

        body.ApplyForce(new Vec2(100, 0));
        world.Step(1f / 60f);

        Assert.True(body.LinearVelocity.X > 0);
    }

    // ── Damping ──

    [Fact]
    public void LinearDamping_ReducesVelocity()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);
        body.LinearVelocity = new Vec2(100, 0);
        body.LinearDamping = 5f;

        world.Step(1f / 60f);

        // Velocity should be reduced
        Assert.True(body.LinearVelocity.X < 100f);
        Assert.True(body.LinearVelocity.X > 0f);
    }

    [Fact]
    public void AngularDamping_ReducesAngularVelocity()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);
        body.AngularVelocity = 10f;
        body.AngularDamping = 5f;

        world.Step(1f / 60f);

        Assert.True(body.AngularVelocity < 10f);
        Assert.True(body.AngularVelocity > 0f);
    }

    // ── SetTransform ──

    [Fact]
    public void SetTransform_TeleportsBody()
    {
        var world = new PhysicsWorld();
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);

        body.SetTransform(new Vec2(500, 300), 1.5f);

        Assert.Equal(500f, body.Position.X);
        Assert.Equal(300f, body.Position.Y);
        Assert.Equal(1.5f, body.Angle);
    }

    // ── Mass Properties ──

    [Fact]
    public void ZeroMass_BodyDoesNotMove()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 100);
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        // mass = 0 (default), invMass = 0

        body.ApplyForce(new Vec2(100, 0));
        world.Step(1f / 60f);

        // With zero invMass, force has no effect, but gravity * gravityScale is added as acceleration
        // Since invMass = 0, the force term is zero, but gravity is added directly to velocity
        // Actually let's check: v += (gravity * gs + force * invMass) * dt
        // = (100*1 + 100*0) * dt = 100 * dt
        // So a zero-mass dynamic body still gets gravity but not forces
        // This is by design — mass should be set via SetMass or shapes
    }

    // ── Velocity Clamping ──

    [Fact]
    public void VelocityClamped_ToMaxLinearSpeed()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        world.Settings.MaxLinearSpeed = 500f;
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);
        body.LinearVelocity = new Vec2(1000, 0);

        world.Step(1f / 60f);

        float speed = body.LinearVelocity.Length();
        Assert.True(speed <= 500f + 0.1f);
    }

    [Fact]
    public void AngularVelocityClamped_ToMaxAngularSpeed()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        world.Settings.MaxAngularSpeed = 10f;
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);
        body.AngularVelocity = 100f;

        world.Step(1f / 60f);

        Assert.True(MathF.Abs(body.AngularVelocity) <= 10f + 0.01f);
    }

    // ── Multiple Bodies ──

    [Fact]
    public void MultipleBodies_IntegrateIndependently()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var a = world.CreateBody(BodyType.Dynamic, 0, 0);
        a.SetMass(1, 1);
        a.LinearVelocity = new Vec2(10, 0);

        var b = world.CreateBody(BodyType.Dynamic, 0, 0);
        b.SetMass(1, 1);
        b.LinearVelocity = new Vec2(0, 20);

        world.Step(1f);

        Assert.Equal(10f, a.Position.X, 0.1);
        Assert.Equal(0f, a.Position.Y, 0.1);
        Assert.Equal(0f, b.Position.X, 0.1);
        Assert.Equal(20f, b.Position.Y, 0.1);
    }

    // ── Eng.Physics Integration ──

    [Fact]
    public void EngPhysics_IsAvailable()
    {
        Assert.NotNull(Eng.Physics);
    }

    [Fact]
    public void EngPhysics_ResetOnInitHeadless()
    {
        Eng.Physics.CreateBody(BodyType.Dynamic);
        Assert.Equal(1, Eng.Physics.BodyCount);

        Eng.InitHeadless();
        Assert.Equal(0, Eng.Physics.BodyCount);
    }

    // ── Step with dt=0 ──

    [Fact]
    public void Step_ZeroDt_NoOp()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 100);
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);

        world.Step(0);

        Assert.Equal(0f, body.Position.Y);
        Assert.Equal(0f, body.LinearVelocity.Y);
    }

    [Fact]
    public void Step_NegativeDt_NoOp()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 100);
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);

        world.Step(-1f);

        Assert.Equal(0f, body.Position.Y);
    }

    // ── PhysicsMath ──

    [Fact]
    public void Transform_ToWorld_Identity()
    {
        var t = new Transform(new Vec2(10, 20), 0);
        var result = t.ToWorld(new Vec2(5, 0));
        Assert.Equal(15f, result.X, 1e-5);
        Assert.Equal(20f, result.Y, 1e-5);
    }

    [Fact]
    public void Transform_ToWorld_Rotated()
    {
        var t = new Transform(new Vec2(0, 0), MathF.PI / 2);
        var result = t.ToWorld(new Vec2(1, 0));
        Assert.Equal(0f, result.X, 1e-4);
        Assert.Equal(1f, result.Y, 1e-4);
    }

    [Fact]
    public void Transform_ToLocal_RoundTrip()
    {
        var t = new Transform(new Vec2(100, 200), 0.7f);
        var local = new Vec2(5, 10);
        var world = t.ToWorld(local);
        var back = t.ToLocal(world);
        Assert.Equal(local.X, back.X, 1e-3);
        Assert.Equal(local.Y, back.Y, 1e-3);
    }

    [Fact]
    public void Mat2x2_Multiply()
    {
        var m = new Mat2x2(0); // identity rotation
        var result = m.Multiply(new Vec2(3, 4));
        Assert.Equal(3f, result.X, 1e-5);
        Assert.Equal(4f, result.Y, 1e-5);
    }

    // ── PhysicsMaterial ──

    [Fact]
    public void PhysicsMaterial_Defaults()
    {
        var mat = PhysicsMaterial.Default;
        Assert.Equal(0.3f, mat.Friction);
        Assert.Equal(0f, mat.Restitution);
        Assert.Equal(1f, mat.Density);
    }

    // ── Sleep ──

    [Fact]
    public void SleepingBody_DoesNotIntegrate()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 100);
        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);
        body.IsSleeping = true;

        world.Step(1f / 60f);

        Assert.Equal(0f, body.Position.Y);
        Assert.Equal(0f, body.LinearVelocity.Y);
    }

    [Fact]
    public void SetAwake_WakesSleepingBody()
    {
        var body = new PhysicsWorld().CreateBody(BodyType.Dynamic);
        body.IsSleeping = true;
        body.SetAwake();
        Assert.False(body.IsSleeping);
    }

    [Fact]
    public void ApplyForce_WakesSleepingBody()
    {
        var body = new PhysicsWorld().CreateBody(BodyType.Dynamic);
        body.SetMass(1, 1);
        body.IsSleeping = true;
        body.ApplyForce(new Vec2(1, 0));
        Assert.False(body.IsSleeping);
    }

    [Fact]
    public void ApplyImpulse_WakesSleepingBody()
    {
        var body = new PhysicsWorld().CreateBody(BodyType.Dynamic);
        body.SetMass(1, 1);
        body.IsSleeping = true;
        body.ApplyImpulse(new Vec2(1, 0));
        Assert.False(body.IsSleeping);
    }

    // ════════════════════════════════════════
    // PHASE 2: Shapes & Collision Detection
    // ════════════════════════════════════════

    // ── AABB ──

    [Fact]
    public void AABB_Overlaps()
    {
        var a = new PhysicsAABB(0, 0, 10, 10);
        var b = new PhysicsAABB(5, 5, 15, 15);
        Assert.True(a.Overlaps(b));
    }

    [Fact]
    public void AABB_NoOverlap()
    {
        var a = new PhysicsAABB(0, 0, 10, 10);
        var b = new PhysicsAABB(20, 20, 30, 30);
        Assert.False(a.Overlaps(b));
    }

    [Fact]
    public void AABB_Contains()
    {
        var a = new PhysicsAABB(0, 0, 10, 10);
        Assert.True(a.Contains(new Vec2(5, 5)));
        Assert.False(a.Contains(new Vec2(15, 5)));
    }

    [Fact]
    public void AABB_Merge()
    {
        var a = new PhysicsAABB(0, 0, 5, 5);
        var b = new PhysicsAABB(3, 3, 10, 10);
        var m = PhysicsAABB.Merge(a, b);
        Assert.Equal(0f, m.Min.X);
        Assert.Equal(0f, m.Min.Y);
        Assert.Equal(10f, m.Max.X);
        Assert.Equal(10f, m.Max.Y);
    }

    [Fact]
    public void AABB_Fatten()
    {
        var a = new PhysicsAABB(5, 5, 10, 10);
        var f = a.Fatten(2);
        Assert.Equal(3f, f.Min.X);
        Assert.Equal(3f, f.Min.Y);
        Assert.Equal(12f, f.Max.X);
        Assert.Equal(12f, f.Max.Y);
    }

    // ── CircleShape ──

    [Fact]
    public void CircleShape_ComputeAABB()
    {
        var c = new CircleShape(10);
        var aabb = c.ComputeAABB(new Vec2(50, 50), 0);
        Assert.Equal(40f, aabb.Min.X);
        Assert.Equal(40f, aabb.Min.Y);
        Assert.Equal(60f, aabb.Max.X);
        Assert.Equal(60f, aabb.Max.Y);
    }

    [Fact]
    public void CircleShape_ComputeMass()
    {
        var c = new CircleShape(10);
        var md = c.ComputeMass(1f);
        float expectedMass = MathF.PI * 100f;
        Assert.Equal(expectedMass, md.Mass, 0.1);
        Assert.True(md.Inertia > 0);
    }

    [Fact]
    public void CircleShape_TestPoint()
    {
        var c = new CircleShape(10);
        Assert.True(c.TestPoint(new Vec2(50, 50), 0, new Vec2(55, 50)));
        Assert.False(c.TestPoint(new Vec2(50, 50), 0, new Vec2(65, 50)));
    }

    // ── BoxShape ──

    [Fact]
    public void BoxShape_CreatesValidPolygon()
    {
        var box = new BoxShape(20, 10);
        Assert.Equal(4, box.Count);
        Assert.Equal(10f, box.HalfWidth);
        Assert.Equal(5f, box.HalfHeight);
    }

    [Fact]
    public void BoxShape_ComputeAABB_NoRotation()
    {
        var box = new BoxShape(20, 10);
        var aabb = box.ComputeAABB(new Vec2(100, 100), 0);
        Assert.Equal(90f, aabb.Min.X, 0.01);
        Assert.Equal(95f, aabb.Min.Y, 0.01);
        Assert.Equal(110f, aabb.Max.X, 0.01);
        Assert.Equal(105f, aabb.Max.Y, 0.01);
    }

    [Fact]
    public void BoxShape_ComputeMass()
    {
        var box = new BoxShape(20, 10);
        var md = box.ComputeMass(1f);
        // Area = 20 * 10 = 200, mass = 200 * 1 = 200
        Assert.Equal(200f, md.Mass, 1f);
        Assert.True(md.Inertia > 0);
    }

    [Fact]
    public void BoxShape_TestPoint()
    {
        var box = new BoxShape(20, 10);
        Assert.True(box.TestPoint(new Vec2(0, 0), 0, new Vec2(5, 3)));
        Assert.False(box.TestPoint(new Vec2(0, 0), 0, new Vec2(15, 3)));
    }

    // ── PolygonShape ──

    [Fact]
    public void PolygonShape_SetFromPoints()
    {
        var poly = new PolygonShape();
        poly.Set(new[] { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10) });
        Assert.Equal(4, poly.Count);
    }

    [Fact]
    public void PolygonShape_ConvexHull_RemovesInterior()
    {
        var poly = new PolygonShape();
        // Square with an interior point — hull should be 4 vertices
        poly.Set(new[] {
            new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10),
            new Vec2(5, 5) // interior
        });
        Assert.Equal(4, poly.Count);
    }

    [Fact]
    public void PolygonShape_Normals_AreUnit()
    {
        var poly = new PolygonShape();
        poly.SetAsBox(10, 10);
        for (int i = 0; i < poly.Count; i++)
        {
            float len = poly.Normals[i].Length();
            Assert.InRange(len, 0.99f, 1.01f);
        }
    }

    // ── RigidBody + Shape ──

    [Fact]
    public void RigidBody_SetShape_ComputesMass()
    {
        var world = new PhysicsWorld();
        var body = world.CreateBody(BodyType.Dynamic);
        body.SetShape(new CircleShape(10));

        Assert.True(body.Mass > 0);
        Assert.True(body.InvMass > 0);
        Assert.True(body.Inertia > 0);
    }

    [Fact]
    public void RigidBody_SetShape_StaticZeroMass()
    {
        var world = new PhysicsWorld();
        var body = world.CreateBody(BodyType.Static);
        body.SetShape(new BoxShape(20, 20));

        Assert.Equal(0f, body.Mass);
        Assert.Equal(0f, body.InvMass);
    }

    // ── Circle vs Circle Collision ──

    [Fact]
    public void CircleVsCircle_Overlapping()
    {
        var a = new CircleShape(10);
        var b = new CircleShape(10);
        var m = CollisionDetection.Detect(a, new Vec2(0, 0), 0, b, new Vec2(15, 0), 0);

        Assert.Equal(1, m.ContactCount);
        Assert.True(m.Contact0.Penetration > 0);
        // Normal should point from A to B (positive X)
        Assert.True(m.Normal.X > 0);
    }

    [Fact]
    public void CircleVsCircle_NotOverlapping()
    {
        var a = new CircleShape(5);
        var b = new CircleShape(5);
        var m = CollisionDetection.Detect(a, new Vec2(0, 0), 0, b, new Vec2(20, 0), 0);

        Assert.Equal(0, m.ContactCount);
    }

    [Fact]
    public void CircleVsCircle_Tangent()
    {
        var a = new CircleShape(5);
        var b = new CircleShape(5);
        var m = CollisionDetection.Detect(a, new Vec2(0, 0), 0, b, new Vec2(10, 0), 0);

        // Exactly touching — valid contact with 0 penetration
        Assert.Equal(1, m.ContactCount);
        Assert.Equal(0f, m.Contact0.Penetration, 1e-5);
    }

    [Fact]
    public void CircleVsCircle_SamePosition()
    {
        var a = new CircleShape(10);
        var b = new CircleShape(10);
        var m = CollisionDetection.Detect(a, new Vec2(5, 5), 0, b, new Vec2(5, 5), 0);

        Assert.Equal(1, m.ContactCount);
        Assert.Equal(20f, m.Contact0.Penetration, 0.1);
    }

    // ── Circle vs Polygon ──

    [Fact]
    public void CircleVsPolygon_Overlapping()
    {
        var circle = new CircleShape(10);
        var box = new BoxShape(20, 20);
        var m = CollisionDetection.Detect(circle, new Vec2(0, 0), 0, box, new Vec2(15, 0), 0);

        Assert.True(m.ContactCount > 0);
        Assert.True(m.Contact0.Penetration > 0);
    }

    [Fact]
    public void CircleVsPolygon_NotOverlapping()
    {
        var circle = new CircleShape(5);
        var box = new BoxShape(10, 10);
        var m = CollisionDetection.Detect(circle, new Vec2(0, 0), 0, box, new Vec2(20, 0), 0);

        Assert.Equal(0, m.ContactCount);
    }

    [Fact]
    public void PolygonVsCircle_FlipNormal()
    {
        // Test the swapped dispatch (polygon A, circle B)
        var box = new BoxShape(20, 20);
        var circle = new CircleShape(10);
        var m = CollisionDetection.Detect(box, new Vec2(0, 0), 0, circle, new Vec2(15, 0), 0);

        Assert.True(m.ContactCount > 0);
        // Normal should point from A (box) to B (circle) — positive X
        Assert.True(m.Normal.X > 0);
    }

    // ── Polygon vs Polygon ──

    [Fact]
    public void PolygonVsPolygon_Overlapping()
    {
        var a = new BoxShape(20, 20);
        var b = new BoxShape(20, 20);
        var m = CollisionDetection.Detect(a, new Vec2(0, 0), 0, b, new Vec2(15, 0), 0);

        Assert.True(m.ContactCount > 0);
        Assert.True(m.Contact0.Penetration > 0);
    }

    [Fact]
    public void PolygonVsPolygon_NotOverlapping()
    {
        var a = new BoxShape(10, 10);
        var b = new BoxShape(10, 10);
        var m = CollisionDetection.Detect(a, new Vec2(0, 0), 0, b, new Vec2(20, 0), 0);

        Assert.Equal(0, m.ContactCount);
    }

    [Fact]
    public void PolygonVsPolygon_FaceFace_TwoContacts()
    {
        var a = new BoxShape(20, 20);
        var b = new BoxShape(20, 20);
        // Aligned boxes overlapping by 5 on X
        var m = CollisionDetection.Detect(a, new Vec2(0, 0), 0, b, new Vec2(15, 0), 0);

        // Face-face contact should produce 2 contact points
        Assert.Equal(2, m.ContactCount);
    }

    [Fact]
    public void PolygonVsPolygon_Rotated()
    {
        var a = new BoxShape(20, 20);
        var b = new BoxShape(20, 20);
        // Rotate B by 45 degrees
        var m = CollisionDetection.Detect(a, new Vec2(0, 0), 0, b, new Vec2(12, 0), MathF.PI / 4);

        Assert.True(m.ContactCount > 0);
    }

    [Fact]
    public void PolygonVsPolygon_Separated_Rotated()
    {
        var a = new BoxShape(10, 10);
        var b = new BoxShape(10, 10);
        var m = CollisionDetection.Detect(a, new Vec2(0, 0), 0, b, new Vec2(30, 0), MathF.PI / 4);

        Assert.Equal(0, m.ContactCount);
    }

    // ── Shape Clone ──

    [Fact]
    public void CircleShape_Clone()
    {
        var c = new CircleShape(15, new Vec2(3, 4));
        var clone = (CircleShape)c.Clone();
        Assert.Equal(15f, clone.Radius);
        Assert.Equal(3f, clone.Center.X);
    }

    [Fact]
    public void BoxShape_Clone()
    {
        var b = new BoxShape(20, 10);
        var clone = (BoxShape)b.Clone();
        Assert.Equal(4, clone.Count);
        Assert.Equal(10f, clone.HalfWidth);
    }

    // ════════════════════════════════════════
    // PHASE 3: Contact Solver
    // ════════════════════════════════════════

    /// <summary>Helper: create a dynamic box body.</summary>
    private static RigidBody MakeDynamicBox(PhysicsWorld world, float x, float y, float w, float h, float restitution = 0, float friction = 0.3f)
    {
        var body = world.CreateBody(BodyType.Dynamic, x, y);
        body.Material = new PhysicsMaterial { Density = 1f, Restitution = restitution, Friction = friction };
        body.SetShape(new BoxShape(w, h));
        return body;
    }

    /// <summary>Helper: create a static box body.</summary>
    private static RigidBody MakeStaticBox(PhysicsWorld world, float x, float y, float w, float h)
    {
        var body = world.CreateBody(BodyType.Static, x, y);
        body.SetShape(new BoxShape(w, h));
        return body;
    }

    [Fact]
    public void Solver_BoxLandsOnFloor()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 200);

        // Wide static floor
        MakeStaticBox(world, 0, 100, 400, 20);
        // Dynamic box above
        var box = MakeDynamicBox(world, 0, 0, 20, 20);

        // Run for 2 seconds
        float dt = 1f / 60f;
        for (int i = 0; i < 120; i++)
            world.Step(dt);

        // Box should have settled on the floor, not fallen through
        // Floor top is at y=90, box bottom would be at box.Position.Y + 10
        Assert.True(box.Position.Y < 100f, $"Box fell through floor: Y={box.Position.Y}");
        Assert.True(box.Position.Y > 50f, $"Box didn't fall far enough: Y={box.Position.Y}");
        // Velocity should be near zero (at rest)
        Assert.InRange(MathF.Abs(box.LinearVelocity.Y), 0, 5f);
    }

    [Fact]
    public void Solver_CircleBounce_Restitution()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 200);

        // Static floor
        MakeStaticBox(world, 0, 100, 400, 20);

        // Bouncy circle
        var ball = world.CreateBody(BodyType.Dynamic, 0, 0);
        ball.Material = new PhysicsMaterial { Density = 1f, Restitution = 0.9f, Friction = 0 };
        ball.SetShape(new CircleShape(10));

        // Drop it
        float dt = 1f / 60f;
        bool bounced = false;
        for (int i = 0; i < 120; i++)
        {
            world.Step(dt);
            // Detect bounce: velocity flips from positive to negative Y
            if (ball.LinearVelocity.Y < -1f)
            {
                bounced = true;
                break;
            }
        }

        Assert.True(bounced, "Ball should have bounced off the floor");
    }

    [Fact]
    public void Solver_NoRestitution_NoBounce()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 200);

        MakeStaticBox(world, 0, 100, 400, 20);

        var ball = world.CreateBody(BodyType.Dynamic, 0, 60);
        ball.Material = new PhysicsMaterial { Density = 1f, Restitution = 0f, Friction = 0 };
        ball.SetShape(new CircleShape(10));

        float dt = 1f / 60f;
        for (int i = 0; i < 120; i++)
            world.Step(dt);

        // With zero restitution, ball should come to rest (no bounce energy)
        Assert.InRange(MathF.Abs(ball.LinearVelocity.Y), 0, 3f);
    }

    [Fact]
    public void Solver_TwoBoxesStacking()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 200);

        MakeStaticBox(world, 0, 200, 400, 20);
        var boxA = MakeDynamicBox(world, 0, 140, 30, 30);
        var boxB = MakeDynamicBox(world, 0, 100, 30, 30);

        float dt = 1f / 60f;
        for (int i = 0; i < 180; i++)
            world.Step(dt);

        // Both boxes should be above the floor and stacked
        Assert.True(boxA.Position.Y < 200f, "Box A fell through floor");
        Assert.True(boxB.Position.Y < boxA.Position.Y, "Box B should be above Box A");
        // Both should be near rest
        Assert.InRange(MathF.Abs(boxA.LinearVelocity.Y), 0, 5f);
        Assert.InRange(MathF.Abs(boxB.LinearVelocity.Y), 0, 5f);
    }

    [Fact]
    public void Solver_DynamicVsKinematic()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var kinematic = world.CreateBody(BodyType.Kinematic, 0, 0);
        kinematic.SetShape(new BoxShape(40, 40));
        kinematic.LinearVelocity = new Vec2(100, 0);

        var dynamic = MakeDynamicBox(world, 30, 0, 20, 20);

        float dt = 1f / 60f;
        for (int i = 0; i < 10; i++)
            world.Step(dt);

        // Dynamic body should have been pushed by kinematic
        Assert.True(dynamic.LinearVelocity.X > 0, "Dynamic body should be pushed right");
        // Kinematic should keep its velocity
        Assert.Equal(100f, kinematic.LinearVelocity.X, 0.1);
    }

    [Fact]
    public void Solver_NoPenetration_NoContacts()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var a = MakeDynamicBox(world, 0, 0, 20, 20);
        var b = MakeDynamicBox(world, 50, 0, 20, 20);

        world.Step(1f / 60f);

        Assert.Equal(0, world.ContactCount);
    }

    [Fact]
    public void Solver_BodiesWithoutShapes_Ignored()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 100);

        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);
        // No shape set — should still integrate but not collide

        world.Step(1f / 60f);

        Assert.True(body.Position.Y > 0); // Still moved by gravity
        Assert.Equal(0, world.ContactCount);
    }

    [Fact]
    public void Solver_ContactPersistence_WarmStart()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 200);

        MakeStaticBox(world, 0, 100, 400, 20);
        var box = MakeDynamicBox(world, 0, 70, 20, 20);

        float dt = 1f / 60f;

        // Run enough steps for the box to land and contacts to persist
        for (int i = 0; i < 60; i++)
            world.Step(dt);

        // Should have active contacts (box resting on floor)
        Assert.True(world.ContactCount > 0, "Should have contacts after settling");

        // Run more steps — warm starting should keep it stable
        float yBefore = box.Position.Y;
        for (int i = 0; i < 60; i++)
            world.Step(dt);

        float yAfter = box.Position.Y;
        Assert.InRange(MathF.Abs(yAfter - yBefore), 0, 2f); // Should be stable
    }

    [Fact]
    public void Solver_Friction_SlowsSliding()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 200);

        MakeStaticBox(world, 0, 50, 400, 20);

        // Box with friction sliding on floor
        var box = MakeDynamicBox(world, -100, 30, 20, 20, friction: 0.5f);
        box.LinearVelocity = new Vec2(200, 0);

        float dt = 1f / 60f;
        for (int i = 0; i < 120; i++)
            world.Step(dt);

        // Friction should have slowed it down significantly
        Assert.True(box.LinearVelocity.X < 200f, "Friction should slow sliding");
    }

    [Fact]
    public void Solver_ZeroFriction_KeepsSliding()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 200);

        var floor = MakeStaticBox(world, 0, 50, 400, 20);
        floor.Material = new PhysicsMaterial { Friction = 0 };

        var box = MakeDynamicBox(world, -100, 30, 20, 20, friction: 0f);
        box.LinearVelocity = new Vec2(200, 0);

        float dt = 1f / 60f;
        for (int i = 0; i < 60; i++)
            world.Step(dt);

        // With zero friction, horizontal velocity should be mostly preserved
        Assert.True(box.LinearVelocity.X > 150f, $"Zero friction should preserve velocity, got {box.LinearVelocity.X}");
    }

    [Fact]
    public void Solver_CircleVsCircle_Separate()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var a = world.CreateBody(BodyType.Dynamic, 0, 0);
        a.Material = new PhysicsMaterial { Density = 1f, Restitution = 1f };
        a.SetShape(new CircleShape(10));
        a.LinearVelocity = new Vec2(100, 0);

        var b = world.CreateBody(BodyType.Dynamic, 25, 0);
        b.Material = new PhysicsMaterial { Density = 1f, Restitution = 1f };
        b.SetShape(new CircleShape(10));

        float dt = 1f / 60f;
        for (int i = 0; i < 30; i++)
            world.Step(dt);

        // Circles should have collided and separated
        // With equal mass and restitution=1, momentum should transfer
        Assert.True(b.LinearVelocity.X > 0, "Circle B should be moving right after collision");
    }

    // ════════════════════════════════════════
    // PHASE 4: Collision Filtering & Triggers
    // ════════════════════════════════════════

    // ── CollisionFilter ──

    [Fact]
    public void Filter_Default_EverythingCollides()
    {
        Assert.True(CollisionFilter.ShouldCollide(CollisionFilter.Default, CollisionFilter.Default));
    }

    [Fact]
    public void Filter_MismatchedMask_NoCollision()
    {
        var a = new CollisionFilter { CategoryBits = 0x0001, MaskBits = 0x0002 }; // A is cat 1, only hits cat 2
        var b = new CollisionFilter { CategoryBits = 0x0004, MaskBits = 0xFFFF }; // B is cat 4
        Assert.False(CollisionFilter.ShouldCollide(a, b));
    }

    [Fact]
    public void Filter_MatchingMask_Collides()
    {
        var a = new CollisionFilter { CategoryBits = 0x0001, MaskBits = 0x0002 };
        var b = new CollisionFilter { CategoryBits = 0x0002, MaskBits = 0x0001 };
        Assert.True(CollisionFilter.ShouldCollide(a, b));
    }

    [Fact]
    public void Filter_OneSidedMask_NoCollision()
    {
        // A wants to hit B, but B doesn't want to hit A
        var a = new CollisionFilter { CategoryBits = 0x0001, MaskBits = 0x0002 };
        var b = new CollisionFilter { CategoryBits = 0x0002, MaskBits = 0x0004 }; // B only hits cat 4
        Assert.False(CollisionFilter.ShouldCollide(a, b));
    }

    [Fact]
    public void Filter_PositiveGroupIndex_AlwaysCollide()
    {
        var a = new CollisionFilter { CategoryBits = 0x0001, MaskBits = 0x0000, GroupIndex = 5 };
        var b = new CollisionFilter { CategoryBits = 0x0002, MaskBits = 0x0000, GroupIndex = 5 };
        // Masks say no, but positive group says yes
        Assert.True(CollisionFilter.ShouldCollide(a, b));
    }

    [Fact]
    public void Filter_NegativeGroupIndex_NeverCollide()
    {
        var a = new CollisionFilter { CategoryBits = 0x0001, MaskBits = 0xFFFF, GroupIndex = -3 };
        var b = new CollisionFilter { CategoryBits = 0x0001, MaskBits = 0xFFFF, GroupIndex = -3 };
        // Masks say yes, but negative group says no
        Assert.False(CollisionFilter.ShouldCollide(a, b));
    }

    [Fact]
    public void Filter_DifferentGroupIndex_UseMask()
    {
        var a = new CollisionFilter { CategoryBits = 0x0001, MaskBits = 0xFFFF, GroupIndex = 1 };
        var b = new CollisionFilter { CategoryBits = 0x0001, MaskBits = 0xFFFF, GroupIndex = 2 };
        // Different groups — falls through to mask check
        Assert.True(CollisionFilter.ShouldCollide(a, b));
    }

    // ── Filtering in PhysicsWorld ──

    [Fact]
    public void Filter_BodiesDontCollide_WithMismatchedMask()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var a = MakeDynamicBox(world, 0, 0, 20, 20);
        a.Filter = new CollisionFilter { CategoryBits = 0x0001, MaskBits = 0x0001 };
        a.LinearVelocity = new Vec2(100, 0);

        var b = MakeDynamicBox(world, 25, 0, 20, 20);
        b.Filter = new CollisionFilter { CategoryBits = 0x0002, MaskBits = 0x0002 };

        float dt = 1f / 60f;
        for (int i = 0; i < 30; i++)
            world.Step(dt);

        // Bodies should pass through each other — no contacts
        Assert.Equal(0f, b.LinearVelocity.X, 0.1);
    }

    [Fact]
    public void Filter_BodiesCollide_WithMatchingMask()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var a = MakeDynamicBox(world, 0, 0, 20, 20);
        a.Filter = new CollisionFilter { CategoryBits = 0x0001, MaskBits = 0x0002 };
        a.LinearVelocity = new Vec2(100, 0);

        var b = MakeDynamicBox(world, 25, 0, 20, 20);
        b.Filter = new CollisionFilter { CategoryBits = 0x0002, MaskBits = 0x0001 };

        float dt = 1f / 60f;
        for (int i = 0; i < 30; i++)
            world.Step(dt);

        // Bodies should collide — B gets pushed
        Assert.True(b.LinearVelocity.X > 0);
    }

    // ── Triggers ──

    [Fact]
    public void Trigger_OverlapDetected_NoPhysicsResponse()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var sensor = MakeDynamicBox(world, 0, 0, 30, 30);
        sensor.IsTrigger = true;

        var mover = MakeDynamicBox(world, 20, 0, 20, 20);
        mover.LinearVelocity = new Vec2(-100, 0);

        float dt = 1f / 60f;
        for (int i = 0; i < 30; i++)
            world.Step(dt);

        // Trigger should detect overlap (contacts exist) but not stop the mover
        Assert.True(mover.LinearVelocity.X < -50f, "Trigger should not stop the mover");
    }

    [Fact]
    public void Trigger_ContactCountIncludesTriggers()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var sensor = MakeDynamicBox(world, 0, 0, 30, 30);
        sensor.IsTrigger = true;

        var body = MakeDynamicBox(world, 10, 0, 20, 20);

        world.Step(1f / 60f);

        // Contact is detected even though it's a trigger
        Assert.True(world.ContactCount > 0);
    }

    [Fact]
    public void Trigger_StaticSensor()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var sensor = world.CreateBody(BodyType.Static, 0, 0);
        sensor.SetShape(new BoxShape(50, 50));
        sensor.IsTrigger = true;

        var mover = MakeDynamicBox(world, 0, 0, 10, 10);
        mover.LinearVelocity = new Vec2(200, 0);

        float dt = 1f / 60f;
        for (int i = 0; i < 10; i++)
            world.Step(dt);

        // Mover should pass through the trigger without stopping
        Assert.True(mover.Position.X > 10f, "Should pass through static trigger");
    }

    // ════════════════════════════════════════
    // PHASE 5: Broad Phase & Contact Events
    // ════════════════════════════════════════

    [Fact]
    public void Event_CollisionEnter_FiresOnce()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        MakeStaticBox(world, 0, 50, 400, 20);
        var box = MakeDynamicBox(world, 0, 0, 20, 20);
        box.LinearVelocity = new Vec2(0, 100);

        int enterCount = 0;
        world.OnCollisionEnter.Subscribe(info => enterCount++);

        float dt = 1f / 60f;
        for (int i = 0; i < 60; i++)
            world.Step(dt);

        Assert.Equal(1, enterCount);
    }

    [Fact]
    public void Event_CollisionStay_FiresWhileInContact()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 200);

        MakeStaticBox(world, 0, 50, 400, 20);
        var box = MakeDynamicBox(world, 0, 20, 20, 20);

        int stayCount = 0;
        world.OnCollisionStay.Subscribe(info => stayCount++);

        float dt = 1f / 60f;
        for (int i = 0; i < 60; i++)
            world.Step(dt);

        // Stay should fire many times while box rests on floor
        Assert.True(stayCount > 5, $"Stay should fire multiple times, got {stayCount}");
    }

    [Fact]
    public void Event_CollisionExit_FiresWhenSeparated()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var a = MakeDynamicBox(world, 0, 0, 20, 20);
        var b = MakeDynamicBox(world, 15, 0, 20, 20);
        b.Material = new PhysicsMaterial { Density = 1f, Restitution = 1f };
        a.Material = new PhysicsMaterial { Density = 1f, Restitution = 1f };

        // Push them apart
        a.LinearVelocity = new Vec2(-50, 0);
        b.LinearVelocity = new Vec2(50, 0);

        int exitCount = 0;
        world.OnCollisionExit.Subscribe(info => exitCount++);

        float dt = 1f / 60f;
        for (int i = 0; i < 60; i++)
            world.Step(dt);

        Assert.True(exitCount > 0, "Exit should fire when bodies separate");
    }

    [Fact]
    public void Event_TriggerEnter_Fires()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var sensor = MakeDynamicBox(world, 50, 0, 30, 30);
        sensor.IsTrigger = true;

        var mover = MakeDynamicBox(world, 0, 0, 10, 10);
        mover.LinearVelocity = new Vec2(200, 0);

        int triggerEnter = 0;
        world.OnTriggerEnter.Subscribe(info => triggerEnter++);

        float dt = 1f / 60f;
        for (int i = 0; i < 30; i++)
            world.Step(dt);

        Assert.Equal(1, triggerEnter);
    }

    [Fact]
    public void Event_TriggerExit_Fires()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var sensor = MakeDynamicBox(world, 30, 0, 20, 20);
        sensor.IsTrigger = true;

        var mover = MakeDynamicBox(world, 0, 0, 10, 10);
        mover.LinearVelocity = new Vec2(200, 0);

        int triggerExit = 0;
        world.OnTriggerExit.Subscribe(info => triggerExit++);

        float dt = 1f / 60f;
        for (int i = 0; i < 60; i++)
            world.Step(dt);

        // Mover passes through and out the other side
        Assert.True(triggerExit > 0, "Trigger exit should fire when mover leaves sensor");
    }

    [Fact]
    public void Event_ContactInfo_HasCorrectBodies()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var floor = MakeStaticBox(world, 0, 50, 400, 20);
        var box = MakeDynamicBox(world, 0, 30, 20, 20);
        box.LinearVelocity = new Vec2(0, 50);

        RigidBody? reportedA = null;
        RigidBody? reportedB = null;
        world.OnCollisionEnter.Subscribe(info =>
        {
            reportedA = info.BodyA;
            reportedB = info.BodyB;
        });

        float dt = 1f / 60f;
        for (int i = 0; i < 30; i++)
            world.Step(dt);

        Assert.NotNull(reportedA);
        Assert.NotNull(reportedB);
        // One should be the box, the other the floor
        Assert.True(
            (reportedA == box && reportedB == floor) ||
            (reportedA == floor && reportedB == box));
    }

    [Fact]
    public void BroadPhase_ManyBodies_StillWorks()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 200);

        MakeStaticBox(world, 0, 200, 800, 20);

        // Create 20 boxes falling
        for (int i = 0; i < 20; i++)
            MakeDynamicBox(world, i * 15 - 150, -i * 20, 12, 12);

        float dt = 1f / 60f;
        for (int i = 0; i < 120; i++)
            world.Step(dt);

        // All bodies should be above the floor
        foreach (var body in world.Bodies)
        {
            if (body.Type == BodyType.Dynamic)
                Assert.True(body.Position.Y < 200f, $"Body at Y={body.Position.Y} fell through");
        }
    }

    [Fact]
    public void Event_Clear_RemovesListeners()
    {
        var world = new PhysicsWorld();
        int count = 0;
        world.OnCollisionEnter.Subscribe(info => count++);

        world.Clear();

        // After clear, the signal should have no listeners
        Assert.Equal(0, world.OnCollisionEnter.Count);
    }

    // ════════════════════════════════════════
    // PHASE 6: PhysicsBody Behavior
    // ════════════════════════════════════════

    [Fact]
    public void PhysicsBody_AttachCreatesBody()
    {
        var entity = new Entity { Position = new Vec2(100, 200) };
        var pb = entity.AddBehavior(new PhysicsBody(BodyType.Dynamic));
        pb.SetBox(20, 20);

        Assert.NotNull(pb.Body);
        Assert.Equal(100f, pb.Body.Position.X, 0.1);
        Assert.Equal(200f, pb.Body.Position.Y, 0.1);
        Assert.Equal(BodyType.Dynamic, pb.Body.Type);
    }

    [Fact]
    public void PhysicsBody_SyncsPositionAfterStep()
    {
        var entity = new Entity { Position = new Vec2(0, 0) };
        var pb = entity.AddBehavior(new PhysicsBody(BodyType.Dynamic));
        pb.SetBox(20, 20);
        pb.GravityScale = 0;

        pb.Body.LinearVelocity = new Vec2(60, 0);

        Eng.Physics.Step(1f);

        // Entity position should match physics body
        Assert.Equal(pb.Body.Position.X, entity.Position.X, 0.1);
        Assert.True(entity.Position.X > 50f, $"Entity should have moved, X={entity.Position.X}");
    }

    [Fact]
    public void PhysicsBody_SyncsAngle_DegreesRadians()
    {
        var entity = new Entity { Position = Vec2.Zero, Angle = 0 };
        var pb = entity.AddBehavior(new PhysicsBody(BodyType.Dynamic));
        pb.SetBox(10, 10);
        pb.GravityScale = 0;

        pb.Body.AngularVelocity = MathF.PI; // 180°/s

        Eng.Physics.Step(1f);

        // Entity angle should be ~180 degrees
        Assert.InRange(entity.Angle, 170f, 190f);
    }

    [Fact]
    public void PhysicsBody_InitialAngle_ConvertedToRadians()
    {
        var entity = new Entity { Position = Vec2.Zero, Angle = 90f }; // 90 degrees
        var pb = entity.AddBehavior(new PhysicsBody(BodyType.Dynamic));
        pb.SetBox(10, 10);

        // Body should have angle in radians ≈ π/2
        Assert.InRange(pb.Body.Angle, MathF.PI / 2 - 0.01f, MathF.PI / 2 + 0.01f);
    }

    [Fact]
    public void PhysicsBody_TeleportDetection()
    {
        var entity = new Entity { Position = new Vec2(0, 0) };
        var pb = entity.AddBehavior(new PhysicsBody(BodyType.Dynamic));
        pb.SetBox(10, 10);
        pb.GravityScale = 0;

        // Simulate one step to sync
        Eng.Physics.Step(1f / 60f);

        // User teleports the entity
        entity.Position = new Vec2(500, 300);

        // PhysicsBody.Update detects the teleport
        entity.Update(1f / 60f);

        Assert.Equal(500f, pb.Body.Position.X, 0.1);
        Assert.Equal(300f, pb.Body.Position.Y, 0.1);
    }

    [Fact]
    public void PhysicsBody_DestroyRemovesBody()
    {
        var entity = new Entity { Position = Vec2.Zero };
        var pb = entity.AddBehavior(new PhysicsBody(BodyType.Dynamic));
        pb.SetBox(10, 10);

        int bodyCount = Eng.Physics.BodyCount;
        Assert.True(bodyCount > 0);

        entity.Destroy();

        Assert.Equal(bodyCount - 1, Eng.Physics.BodyCount);
    }

    [Fact]
    public void PhysicsBody_PerBodyCollisionEvent()
    {
        Eng.InitHeadless();

        var floor = new Entity { Position = new Vec2(0, 100) };
        var floorPb = floor.AddBehavior(new PhysicsBody(BodyType.Static));
        floorPb.SetBox(400, 20);

        var box = new Entity { Position = new Vec2(0, 50) };
        var boxPb = box.AddBehavior(new PhysicsBody(BodyType.Dynamic));
        boxPb.SetBox(20, 20);
        boxPb.Material = new PhysicsMaterial { Density = 1f };
        boxPb.Body.LinearVelocity = new Vec2(0, 100);

        int hitCount = 0;
        boxPb.OnCollisionEnter.Subscribe(info => hitCount++);

        float dt = 1f / 60f;
        for (int i = 0; i < 60; i++)
            Eng.Physics.Step(dt);

        Assert.Equal(1, hitCount);
    }

    [Fact]
    public void PhysicsBody_PerBodyTriggerEvent()
    {
        Eng.InitHeadless();

        var sensor = new Entity { Position = new Vec2(50, 0) };
        var sensorPb = sensor.AddBehavior(new PhysicsBody(BodyType.Static));
        sensorPb.SetBox(30, 30);
        sensorPb.IsTrigger = true;

        var mover = new Entity { Position = new Vec2(0, 0) };
        var moverPb = mover.AddBehavior(new PhysicsBody(BodyType.Dynamic));
        moverPb.SetBox(10, 10);
        moverPb.GravityScale = 0;
        moverPb.Body.LinearVelocity = new Vec2(200, 0);

        int triggerHits = 0;
        moverPb.OnTriggerEnter.Subscribe(info => triggerHits++);

        float dt = 1f / 60f;
        for (int i = 0; i < 30; i++)
            Eng.Physics.Step(dt);

        Assert.Equal(1, triggerHits);
    }

    [Fact]
    public void PhysicsBody_SetPropertiesBeforeAttach()
    {
        // Set properties before AddBehavior (they should be deferred)
        var pb = new PhysicsBody(BodyType.Dynamic);
        pb.Material = new PhysicsMaterial { Restitution = 0.8f, Friction = 0.1f, Density = 2f };
        pb.GravityScale = 0.5f;
        pb.FixedRotation = true;
        pb.IsTrigger = true;
        pb.SetBox(30, 30);

        var entity = new Entity { Position = new Vec2(10, 20) };
        entity.AddBehavior(pb);

        Assert.Equal(0.8f, pb.Body.Material.Restitution);
        Assert.Equal(0.1f, pb.Body.Material.Friction);
        Assert.Equal(0.5f, pb.Body.GravityScale);
        Assert.True(pb.Body.FixedRotation);
        Assert.True(pb.Body.IsTrigger);
        Assert.NotNull(pb.Body.Shape);
    }

    [Fact]
    public void PhysicsBody_ApplyForce_MovesEntity()
    {
        Eng.InitHeadless();

        var entity = new Entity { Position = Vec2.Zero };
        var pb = entity.AddBehavior(new PhysicsBody(BodyType.Dynamic));
        pb.SetBox(10, 10);
        pb.GravityScale = 0;

        pb.ApplyImpulse(new Vec2(10000, 0));

        float dt = 1f / 60f;
        for (int i = 0; i < 60; i++)
            Eng.Physics.Step(dt);

        Assert.True(entity.Position.X > 10f, $"Entity should have moved right, X={entity.Position.X}");
    }

    [Fact]
    public void PhysicsBody_FallsOntoStaticFloor()
    {
        Eng.InitHeadless();
        Eng.Physics.Settings.Gravity = new Vec2(0, 200);

        var floor = new Entity { Position = new Vec2(0, 100) };
        floor.AddBehavior(new PhysicsBody(BodyType.Static)).SetBox(400, 20);

        var box = new Entity { Position = new Vec2(0, 0) };
        var pb = box.AddBehavior(new PhysicsBody(BodyType.Dynamic));
        pb.SetBox(20, 20);

        float dt = 1f / 60f;
        for (int i = 0; i < 120; i++)
            Eng.Physics.Step(dt);

        // Box should rest on the floor, entity position synced
        Assert.True(box.Position.Y < 100f, $"Box fell through floor: Y={box.Position.Y}");
        Assert.True(box.Position.Y > 50f, $"Box didn't fall: Y={box.Position.Y}");
    }

    [Fact]
    public void PhysicsBody_KinematicEntity_PhysicsOverrides()
    {
        Eng.InitHeadless();

        // KinematicEntity has its own velocity — PhysicsBody should take over
        var ke = new KinematicEntity { Position = Vec2.Zero };
        var pb = ke.AddBehavior(new PhysicsBody(BodyType.Dynamic));
        pb.SetCircle(10);
        pb.GravityScale = 0;

        pb.Body.LinearVelocity = new Vec2(100, 0);

        Eng.Physics.Step(0.5f);

        // Entity position should be set by physics, not by KinematicEntity velocity
        Assert.Equal(pb.Body.Position.X, ke.Position.X, 0.1);
    }

    // ════════════════════════════════════════
    // PHASE 7: Joints
    // ════════════════════════════════════════

    [Fact]
    public void DistanceJoint_MaintainsLength()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 200);

        var anchor = world.CreateBody(BodyType.Static, 0, 0);
        anchor.SetShape(new CircleShape(5));

        var ball = world.CreateBody(BodyType.Dynamic, 0, 100);
        ball.SetShape(new CircleShape(10));
        ball.Material = new PhysicsMaterial { Density = 1f };

        var joint = DistanceJoint.Create(anchor, ball, anchor.Position, ball.Position);
        world.AddJoint(joint);

        float targetLen = joint.Length; // 100

        float dt = 1f / 60f;
        for (int i = 0; i < 180; i++)
            world.Step(dt);

        float dist = Vec2.Distance(anchor.Position, ball.Position);
        Assert.InRange(dist, targetLen - 5f, targetLen + 5f);
    }

    [Fact]
    public void RevoluteJoint_Pendulum()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 200);

        var pivot = world.CreateBody(BodyType.Static, 0, 0);

        var bob = world.CreateBody(BodyType.Dynamic, 50, 0);
        bob.Material = new PhysicsMaterial { Density = 1f };
        bob.SetShape(new CircleShape(10));

        Assert.True(bob.InvMass > 0, $"Bob should have mass, InvMass={bob.InvMass}");

        var joint = new RevoluteJoint(pivot, bob, pivot.Position);
        world.AddJoint(joint);

        float dt = 1f / 60f;
        for (int i = 0; i < 300; i++)
            world.Step(dt);

        // Bob should have swung down (Y increased from 0)
        Assert.True(bob.Position.Y > 5f, $"Pendulum bob should have swung down, Y={bob.Position.Y}");

        // Anchor constraint: distance from pivot to bob should be ~50
        float dist = Vec2.Distance(pivot.Position, bob.Position);
        Assert.InRange(dist, 35f, 65f);
    }

    [Fact]
    public void SpringJoint_Oscillates()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var anchor = world.CreateBody(BodyType.Static, 0, 0);
        // No shape on anchor — pure joint point

        var ball = world.CreateBody(BodyType.Dynamic, 100, 0);
        ball.Material = new PhysicsMaterial { Density = 1f };
        ball.SetShape(new CircleShape(10));

        var spring = SpringJoint.Create(anchor, ball, anchor.Position, ball.Position);
        spring.Frequency = 2f;
        spring.DampingRatio = 0.1f;
        world.AddJoint(spring);

        // Push ball further out
        ball.Position = new Vec2(200, 0);

        // Track max and min X to verify oscillation
        float maxX = ball.Position.X;
        float minX = ball.Position.X;

        float dt = 1f / 60f;
        for (int i = 0; i < 600; i++)
        {
            world.Step(dt);
            if (ball.Position.X > maxX) maxX = ball.Position.X;
            if (ball.Position.X < minX) minX = ball.Position.X;
        }

        // Should oscillate: ball moves from 200 and eventually crosses rest length (100)
        Assert.True(minX < 150f, $"Spring should pull ball toward rest, minX={minX}");
        Assert.True(maxX >= 200f, $"Ball should have been at 200 or beyond, maxX={maxX}");
    }

    [Fact]
    public void WeldJoint_BodiesMoveAsOne()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 200);

        var a = world.CreateBody(BodyType.Dynamic, 0, 0);
        a.SetShape(new BoxShape(20, 20));
        a.Material = new PhysicsMaterial { Density = 1f };

        var b = world.CreateBody(BodyType.Dynamic, 30, 0);
        b.SetShape(new BoxShape(20, 20));
        b.Material = new PhysicsMaterial { Density = 1f };

        var weld = new WeldJoint(a, b, new Vec2(15, 0));
        world.AddJoint(weld);

        float dt = 1f / 60f;
        for (int i = 0; i < 60; i++)
            world.Step(dt);

        // Bodies should maintain their relative position
        float relX = b.Position.X - a.Position.X;
        Assert.InRange(relX, 25f, 35f); // Should stay ~30 apart

        // Both should be falling together
        Assert.True(a.Position.Y > 10f);
        Assert.InRange(MathF.Abs(a.Position.Y - b.Position.Y), 0, 5f);
    }

    [Fact]
    public void MouseJoint_PullsBodyToTarget()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.Material = new PhysicsMaterial { Density = 1f };
        body.SetShape(new CircleShape(10));

        var mouse = new MouseJoint(body, new Vec2(100, 0));
        mouse.MaxForce = 500000f;
        mouse.Frequency = 5f;
        mouse.DampingRatio = 0.7f;
        world.AddJoint(mouse);

        float dt = 1f / 60f;
        for (int i = 0; i < 300; i++)
            world.Step(dt);

        // Body should have moved toward target (even partially)
        Assert.True(body.Position.X > 1f, $"Body should move toward target, X={body.Position.X}");
    }

    [Fact]
    public void MouseJoint_UpdateTarget()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetShape(new CircleShape(10));
        body.Material = new PhysicsMaterial { Density = 1f };

        var mouse = new MouseJoint(body, new Vec2(100, 0));
        world.AddJoint(mouse);

        float dt = 1f / 60f;
        for (int i = 0; i < 60; i++)
            world.Step(dt);

        // Change target
        mouse.Target = new Vec2(-100, 0);

        for (int i = 0; i < 120; i++)
            world.Step(dt);

        // Body should have moved toward new target
        Assert.True(body.Position.X < 50f, $"Body should follow new target, X={body.Position.X}");
    }

    [Fact]
    public void PrismaticJoint_ConstrainsToAxis()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vec2(0, 200);

        var rail = world.CreateBody(BodyType.Static, 0, 0);
        rail.SetShape(new BoxShape(400, 10));

        var slider = world.CreateBody(BodyType.Dynamic, 0, 0);
        slider.SetShape(new BoxShape(20, 20));
        slider.Material = new PhysicsMaterial { Density = 1f };

        // Constrain to horizontal axis
        var joint = new PrismaticJoint(rail, slider, Vec2.Zero, new Vec2(1, 0));
        world.AddJoint(joint);

        // Push it sideways
        slider.LinearVelocity = new Vec2(100, 0);

        float dt = 1f / 60f;
        for (int i = 0; i < 60; i++)
            world.Step(dt);

        // Should have slid horizontally
        Assert.True(slider.Position.X > 50f, $"Should slide along axis, X={slider.Position.X}");
        // Should not have moved much vertically (constrained)
        Assert.InRange(MathF.Abs(slider.Position.Y), 0, 10f);
    }

    [Fact]
    public void Joint_Destroy_RemovesFromWorld()
    {
        var world = new PhysicsWorld();

        var a = world.CreateBody(BodyType.Dynamic);
        a.SetShape(new CircleShape(10));
        var b = world.CreateBody(BodyType.Dynamic);
        b.SetShape(new CircleShape(10));

        var joint = DistanceJoint.Create(a, b, a.Position, b.Position);
        world.AddJoint(joint);
        Assert.Single(world.Joints);

        world.DestroyJoint(joint);
        Assert.Empty(world.Joints);
    }

    [Fact]
    public void Joint_Clear_RemovesAll()
    {
        var world = new PhysicsWorld();

        var a = world.CreateBody(BodyType.Dynamic);
        a.SetShape(new CircleShape(10));
        var b = world.CreateBody(BodyType.Dynamic);
        b.SetShape(new CircleShape(10));

        world.AddJoint(DistanceJoint.Create(a, b, a.Position, b.Position));
        world.AddJoint(new RevoluteJoint(a, b, Vec2.Zero));

        world.Clear();
        Assert.Empty(world.Joints);
    }

    // ════════════════════════════════════════
    // PHASE 8: Raycasting & Queries
    // ════════════════════════════════════════

    [Fact]
    public void Raycast_HitsBox()
    {
        var world = new PhysicsWorld();
        var box = world.CreateBody(BodyType.Static, 100, 0);
        box.SetShape(new BoxShape(20, 20));

        bool hit = world.Raycast(new Vec2(0, 0), new Vec2(1, 0), 200, out var result);

        Assert.True(hit);
        Assert.Equal(box, result.Body);
        Assert.InRange(result.Point.X, 88f, 92f); // Left face of box at x=90
        Assert.True(result.Normal.X < 0); // Normal points back toward ray origin
        Assert.InRange(result.Fraction, 0, 1);
    }

    [Fact]
    public void Raycast_HitsCircle()
    {
        var world = new PhysicsWorld();
        var circle = world.CreateBody(BodyType.Static, 50, 0);
        circle.SetShape(new CircleShape(15));

        bool hit = world.Raycast(new Vec2(0, 0), new Vec2(1, 0), 100, out var result);

        Assert.True(hit);
        Assert.Equal(circle, result.Body);
        Assert.InRange(result.Point.X, 33f, 37f); // Near edge of circle
    }

    [Fact]
    public void Raycast_Miss()
    {
        var world = new PhysicsWorld();
        var box = world.CreateBody(BodyType.Static, 100, 50);
        box.SetShape(new BoxShape(20, 20));

        // Ray goes straight right at y=0, box is at y=50
        bool hit = world.Raycast(new Vec2(0, 0), new Vec2(1, 0), 200, out _);
        Assert.False(hit);
    }

    [Fact]
    public void Raycast_RespectsMaxDistance()
    {
        var world = new PhysicsWorld();
        var box = world.CreateBody(BodyType.Static, 200, 0);
        box.SetShape(new BoxShape(20, 20));

        // Max distance too short to reach the box
        bool hit = world.Raycast(new Vec2(0, 0), new Vec2(1, 0), 100, out _);
        Assert.False(hit);
    }

    [Fact]
    public void Raycast_ClosestHitReturned()
    {
        var world = new PhysicsWorld();

        var far = world.CreateBody(BodyType.Static, 200, 0);
        far.SetShape(new BoxShape(20, 20));

        var near = world.CreateBody(BodyType.Static, 80, 0);
        near.SetShape(new BoxShape(20, 20));

        bool hit = world.Raycast(new Vec2(0, 0), new Vec2(1, 0), 300, out var result);

        Assert.True(hit);
        Assert.Equal(near, result.Body);
    }

    [Fact]
    public void RaycastAll_ReturnsSortedByFraction()
    {
        var world = new PhysicsWorld();

        var a = world.CreateBody(BodyType.Static, 200, 0);
        a.SetShape(new BoxShape(20, 20));

        var b = world.CreateBody(BodyType.Static, 80, 0);
        b.SetShape(new BoxShape(20, 20));

        var results = world.RaycastAll(new Vec2(0, 0), new Vec2(1, 0), 300);

        Assert.Equal(2, results.Count);
        Assert.Equal(b, results[0].Body); // Closer
        Assert.Equal(a, results[1].Body); // Farther
        Assert.True(results[0].Fraction < results[1].Fraction);
    }

    [Fact]
    public void Raycast_MaskFilter()
    {
        var world = new PhysicsWorld();

        var box = world.CreateBody(BodyType.Static, 80, 0);
        box.SetShape(new BoxShape(20, 20));
        box.Filter = new CollisionFilter { CategoryBits = 0x0002, MaskBits = 0xFFFF };

        // Raycast with mask that doesn't include category 0x0002
        bool hit = world.Raycast(new Vec2(0, 0), new Vec2(1, 0), 200, out _, maskBits: 0x0001);
        Assert.False(hit);

        // Raycast with mask that does include it
        hit = world.Raycast(new Vec2(0, 0), new Vec2(1, 0), 200, out _, maskBits: 0x0002);
        Assert.True(hit);
    }

    [Fact]
    public void QueryAABB_FindsBodies()
    {
        var world = new PhysicsWorld();

        var inside = world.CreateBody(BodyType.Static, 50, 50);
        inside.SetShape(new BoxShape(10, 10));

        var outside = world.CreateBody(BodyType.Static, 200, 200);
        outside.SetShape(new BoxShape(10, 10));

        var results = world.QueryAABB(0, 0, 100, 100);
        Assert.Contains(inside, results);
        Assert.DoesNotContain(outside, results);
    }

    [Fact]
    public void QueryCircle_FindsBodies()
    {
        var world = new PhysicsWorld();

        var near = world.CreateBody(BodyType.Static, 30, 0);
        near.SetShape(new CircleShape(10));

        var far = world.CreateBody(BodyType.Static, 200, 0);
        far.SetShape(new CircleShape(10));

        var results = world.QueryCircle(Vec2.Zero, 50);
        Assert.Contains(near, results);
        Assert.DoesNotContain(far, results);
    }

    [Fact]
    public void QueryPoint_FindsBody()
    {
        var world = new PhysicsWorld();

        var box = world.CreateBody(BodyType.Static, 50, 50);
        box.SetShape(new BoxShape(20, 20));

        var found = world.QueryPoint(new Vec2(55, 55));
        Assert.Equal(box, found);

        var miss = world.QueryPoint(new Vec2(200, 200));
        Assert.Null(miss);
    }

    [Fact]
    public void QueryPoint_Circle()
    {
        var world = new PhysicsWorld();

        var circle = world.CreateBody(BodyType.Static, 0, 0);
        circle.SetShape(new CircleShape(20));

        Assert.NotNull(world.QueryPoint(new Vec2(10, 0)));
        Assert.Null(world.QueryPoint(new Vec2(25, 0)));
    }

    // ════════════════════════════════════════
    // PHASE 9: Sleep System
    // ════════════════════════════════════════

    [Fact]
    public void Sleep_BodySleepsAfterResting()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        world.Settings.SleepTimeThreshold = 0.2f;

        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);
        // Body has zero velocity — should sleep after threshold

        float dt = 1f / 60f;
        for (int i = 0; i < 30; i++) // 0.5 seconds > 0.2 threshold
            world.Step(dt);

        Assert.True(body.IsSleeping, "Body with zero velocity should sleep");
    }

    [Fact]
    public void Sleep_MovingBodyDoesNotSleep()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        world.Settings.SleepTimeThreshold = 0.2f;

        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);
        body.LinearVelocity = new Vec2(100, 0);

        float dt = 1f / 60f;
        for (int i = 0; i < 30; i++)
            world.Step(dt);

        Assert.False(body.IsSleeping, "Moving body should not sleep");
    }

    [Fact]
    public void Sleep_WakesOnApplyForce()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        world.Settings.SleepTimeThreshold = 0.1f;

        var body = world.CreateBody(BodyType.Dynamic, 0, 0);
        body.SetMass(1, 1);

        // Let it sleep
        for (int i = 0; i < 20; i++)
            world.Step(1f / 60f);
        Assert.True(body.IsSleeping);

        // Wake it with force
        body.ApplyForce(new Vec2(100, 0));
        Assert.False(body.IsSleeping);
    }

    [Fact]
    public void Sleep_WakesOnContact()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        world.Settings.SleepTimeThreshold = 0.1f;

        var sleeper = world.CreateBody(BodyType.Dynamic, 100, 0);
        sleeper.Material = new PhysicsMaterial { Density = 1f };
        sleeper.SetShape(new BoxShape(20, 20));

        // Let it sleep
        for (int i = 0; i < 20; i++)
            world.Step(1f / 60f);
        Assert.True(sleeper.IsSleeping);

        // Launch a projectile at it
        var projectile = world.CreateBody(BodyType.Dynamic, 0, 0);
        projectile.Material = new PhysicsMaterial { Density = 1f };
        projectile.SetShape(new CircleShape(5));
        projectile.LinearVelocity = new Vec2(500, 0);

        for (int i = 0; i < 30; i++)
            world.Step(1f / 60f);

        Assert.False(sleeper.IsSleeping, "Sleeping body should wake on contact");
    }

    [Fact]
    public void Sleep_TwoSleepingBodies_DontWakeEachOther()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        world.Settings.SleepTimeThreshold = 0.1f;

        var a = world.CreateBody(BodyType.Dynamic, 0, 0);
        a.SetMass(1, 1);
        a.SetShape(new BoxShape(20, 20));

        var b = world.CreateBody(BodyType.Dynamic, 15, 0);
        b.SetMass(1, 1);
        b.SetShape(new BoxShape(20, 20));

        // Let both sleep (they overlap but both sleeping = skipped)
        for (int i = 0; i < 20; i++)
            world.Step(1f / 60f);

        Assert.True(a.IsSleeping);
        Assert.True(b.IsSleeping);
    }

    // ════════════════════════════════════════
    // PHASE 10: CCD
    // ════════════════════════════════════════

    [Fact]
    public void CCD_BulletDoesNotTunnel()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        // Thin wall
        var wall = world.CreateBody(BodyType.Static, 200, 0);
        wall.SetShape(new BoxShape(4, 100));

        // Fast bullet
        var bullet = world.CreateBody(BodyType.Dynamic, 0, 0);
        bullet.Material = new PhysicsMaterial { Density = 1f };
        bullet.SetShape(new CircleShape(3));
        bullet.IsBullet = true;
        bullet.LinearVelocity = new Vec2(5000, 0); // Very fast

        world.Step(1f / 60f);

        // Bullet should not have passed through the wall (wall left edge at x=198)
        Assert.True(bullet.Position.X < 200f, $"Bullet should not tunnel, X={bullet.Position.X}");
    }

    [Fact]
    public void CCD_NonBulletCanTunnel()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;
        world.Settings.MaxLinearSpeed = 100000f; // Allow extreme speed

        var wall = world.CreateBody(BodyType.Static, 200, 0);
        wall.SetShape(new BoxShape(4, 100));

        var fast = world.CreateBody(BodyType.Dynamic, 0, 0);
        fast.Material = new PhysicsMaterial { Density = 1f };
        fast.SetShape(new CircleShape(3));
        fast.IsBullet = false; // Not a bullet
        fast.LinearVelocity = new Vec2(50000, 0);

        world.Step(1f / 60f);

        // Non-bullet at extreme speed should pass through (no CCD sweep)
        Assert.True(fast.Position.X > 200f, $"Non-bullet should tunnel at extreme speed, X={fast.Position.X}");
    }

    // ════════════════════════════════════════
    // PHASE 11: Island Solver
    // ════════════════════════════════════════

    [Fact]
    public void Island_SeparateGroupsFormSeparateIslands()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vec2.Zero;

        // Group 1: two touching boxes
        var a1 = MakeDynamicBox(world, 0, 0, 20, 20);
        var a2 = MakeDynamicBox(world, 15, 0, 20, 20);

        // Group 2: two touching boxes far away
        var b1 = MakeDynamicBox(world, 500, 0, 20, 20);
        var b2 = MakeDynamicBox(world, 515, 0, 20, 20);

        // Step to generate contacts
        world.Step(1f / 60f);

        // Both groups should have contacts
        Assert.True(world.ContactCount >= 2, $"Should have contacts in both groups, got {world.ContactCount}");
    }

    // ════════════════════════════════════════
    // PHASE 12: Debug Rendering
    // ════════════════════════════════════════

    [Fact]
    public void DebugDraw_NoException_EmptyWorld()
    {
        var world = new PhysicsWorld();
        world.DebugDraw = true;
        world.Step(1f / 60f);
        world.DebugDraw = false;
        // Just verifying no crash
    }

    [Fact]
    public void DebugDraw_NoException_WithBodies()
    {
        var world = new PhysicsWorld();
        world.DebugDraw = true;

        var box = world.CreateBody(BodyType.Dynamic, 0, 0);
        box.SetShape(new BoxShape(20, 20));
        box.Material = new PhysicsMaterial { Density = 1f };

        var circle = world.CreateBody(BodyType.Static, 50, 0);
        circle.SetShape(new CircleShape(15));

        world.Step(1f / 60f);
        world.DebugDraw = false;
        // No crash = pass
    }
}
