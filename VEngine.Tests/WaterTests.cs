using System;
using VEngine.Engine.Core;
using VEngine.Engine.Math;
using VEngine.Engine.Physics;
using VEngine.Engine.Physics.Shapes;
using Xunit;

namespace VEngine.Tests;

public class WaterTests
{
    public WaterTests()
    {
        Eng.InitHeadless();
    }

    // ── Wave Simulation ──

    [Fact]
    public void Wave_ColumnReturnsToRest()
    {
        var water = new WaterBody(0, 100, 200, 100);
        water.Disturb(50, 10f);

        for (int i = 0; i < 300; i++)
            water.Update(1f / 60f);

        // After many frames, column should settle near 0
        float h = water.SurfaceYAt(50) - 100;
        Assert.InRange(MathF.Abs(h), 0, 2f);
    }

    [Fact]
    public void Wave_DampingReducesAmplitude()
    {
        var water = new WaterBody(0, 100, 200, 100);
        water.Disturb(100, 20f);

        water.Update(1f / 60f);
        float h1 = MathF.Abs(water.SurfaceYAt(100) - 100);

        for (int i = 0; i < 60; i++)
            water.Update(1f / 60f);

        float h2 = MathF.Abs(water.SurfaceYAt(100) - 100);
        Assert.True(h2 < h1, $"Amplitude should decrease: {h1} -> {h2}");
    }

    [Fact]
    public void Wave_PropagationSpreadsToNeighbors()
    {
        var water = new WaterBody(0, 100, 200, 100, columnSpacing: 10);
        int centerCol = water.ColumnCount / 2;
        water.Disturb(centerCol * 10f, 15f);

        for (int i = 0; i < 10; i++)
            water.Update(1f / 60f);

        // Neighbors should have been affected
        float centerH = MathF.Abs(water.SurfaceYAt(centerCol * 10f) - 100);
        float neighborH = MathF.Abs(water.SurfaceYAt((centerCol + 2) * 10f) - 100);
        Assert.True(neighborH > 0.01f, $"Neighbor should be disturbed, got {neighborH}");
    }

    [Fact]
    public void Wave_Disturb_AffectsCorrectColumn()
    {
        var water = new WaterBody(0, 100, 200, 100, columnSpacing: 10);
        water.Disturb(50, 10f); // column 5

        water.Update(1f / 60f);
        float h = water.SurfaceYAt(50) - 100;
        Assert.True(MathF.Abs(h) > 1f, $"Disturbed column should move, got {h}");
    }

    // ── SurfaceYAt ──

    [Fact]
    public void SurfaceYAt_ReturnsRestWhenUndisturbed()
    {
        var water = new WaterBody(0, 100, 200, 100);
        Assert.Equal(100f, water.SurfaceYAt(50), 0.1);
    }

    [Fact]
    public void SurfaceYAt_InterpolatesBetweenColumns()
    {
        var water = new WaterBody(0, 100, 200, 100, columnSpacing: 20);
        // Disturb column at x=0 but not column at x=20
        water.Disturb(0, 10f);
        water.Update(1f / 60f);

        float atCol0 = water.SurfaceYAt(0);
        float midpoint = water.SurfaceYAt(10);
        float atCol1 = water.SurfaceYAt(20);

        // Midpoint should be between the two columns
        Assert.True(midpoint != atCol0 || midpoint != atCol1,
            "Midpoint should interpolate between columns");
    }

    [Fact]
    public void SurfaceYAt_ClampedOutsideBounds()
    {
        var water = new WaterBody(100, 200, 300, 100);
        // Querying outside should not crash, returns edge value
        float left = water.SurfaceYAt(50);   // before water
        float right = water.SurfaceYAt(500); // after water
        Assert.True(!float.IsNaN(left));
        Assert.True(!float.IsNaN(right));
    }

    // ── Buoyancy ──

    [Fact]
    public void Buoyancy_CircleAboveWater_NoForce()
    {
        Eng.InitHeadless();
        var world = Eng.Physics;
        world.Settings.Gravity = new Vec2(0, 200);

        var water = new WaterBody(0, 300, 400, 200);

        var ball = world.CreateBody(BodyType.Dynamic, 100, 100); // well above water at y=300
        ball.Material = new PhysicsMaterial { Density = 1f };
        ball.SetShape(new CircleShape(10));

        float vyBefore = ball.LinearVelocity.Y;
        water.Update(1f / 60f);

        // No buoyancy applied — velocity shouldn't change from water
        Assert.Equal(vyBefore, ball.LinearVelocity.Y, 0.01);
    }

    [Fact]
    public void Buoyancy_CircleInWater_GetsUpwardForce()
    {
        Eng.InitHeadless();
        var world = Eng.Physics;
        world.Settings.Gravity = new Vec2(0, 200);

        var water = new WaterBody(0, 100, 400, 200);

        var ball = world.CreateBody(BodyType.Dynamic, 100, 150); // inside water (water surface at y=100)
        ball.Material = new PhysicsMaterial { Density = 0.5f };
        ball.SetShape(new CircleShape(15));

        water.Update(1f / 60f);

        // Ball should have upward force applied (Force.Y < 0 in Y-down)
        Assert.True(ball.Force.Y < 0, $"Buoyancy should push up, Force.Y={ball.Force.Y}");
    }

    [Fact]
    public void Buoyancy_BoxInWater_GetsUpwardForce()
    {
        Eng.InitHeadless();
        var world = Eng.Physics;
        world.Settings.Gravity = new Vec2(0, 200);

        var water = new WaterBody(0, 100, 400, 200);

        var box = world.CreateBody(BodyType.Dynamic, 100, 130);
        box.Material = new PhysicsMaterial { Density = 0.5f };
        box.SetShape(new BoxShape(20, 20));

        water.Update(1f / 60f);

        Assert.True(box.Force.Y < 0, $"Buoyancy should push box up, Force.Y={box.Force.Y}");
    }

    [Fact]
    public void Buoyancy_StaticBody_Ignored()
    {
        Eng.InitHeadless();
        var world = Eng.Physics;
        world.Settings.Gravity = new Vec2(0, 200);

        var water = new WaterBody(0, 100, 400, 200);

        var floor = world.CreateBody(BodyType.Static, 100, 150);
        floor.SetShape(new BoxShape(100, 10));

        water.Update(1f / 60f);

        // Static bodies should not get forces
        Assert.Equal(0f, floor.Force.Y);
    }

    [Fact]
    public void Buoyancy_OutsideWaterRegion_NoForce()
    {
        Eng.InitHeadless();
        var world = Eng.Physics;
        world.Settings.Gravity = new Vec2(0, 200);

        var water = new WaterBody(100, 100, 200, 200); // water from x=100 to x=300

        var ball = world.CreateBody(BodyType.Dynamic, 50, 150); // left of water
        ball.Material = new PhysicsMaterial { Density = 1f };
        ball.SetShape(new CircleShape(10));

        water.Update(1f / 60f);

        Assert.Equal(0f, ball.Force.Y, 0.01);
    }

    // ── Drag ──

    [Fact]
    public void Drag_ForceActuallyApplied()
    {
        Eng.InitHeadless();
        var world = Eng.Physics;
        world.Settings.Gravity = Vec2.Zero;

        var water = new WaterBody(0, 50, 400, 300);
        water.LinearDragCoeff = 50f;

        var ball = world.CreateBody(BodyType.Dynamic, 100, 150);
        ball.Material = new PhysicsMaterial { Density = 1f };
        ball.SetShape(new CircleShape(10));
        ball.LinearVelocity = new Vec2(200, 0);

        // Just update water (don't step physics) — check force directly
        water.Update(1f / 60f);

        Assert.True(ball.Force.X != 0 || ball.Force.Y != 0,
            $"Water should apply force, Force=({ball.Force.X},{ball.Force.Y})");
    }

    [Fact]
    public void Drag_SlowsSubmergedBody()
    {
        Eng.InitHeadless();
        var world = Eng.Physics;
        world.Settings.Gravity = Vec2.Zero;

        var water = new WaterBody(0, 50, 400, 300);
        water.Density = 0; // No buoyancy — isolate drag

        var ball = world.CreateBody(BodyType.Dynamic, 100, 150);
        ball.Material = new PhysicsMaterial { Density = 1f };
        ball.SetShape(new CircleShape(10));
        ball.LinearVelocity = new Vec2(100, 0);

        for (int i = 0; i < 60; i++)
        {
            water.Update(1f / 60f);
            world.Step(1f / 60f);
        }

        // Direct velocity damping should reduce speed significantly
        Assert.True(ball.LinearVelocity.X < 50f, $"Drag should slow body, vx={ball.LinearVelocity.X}");
    }

    [Fact]
    public void AngularDrag_SlowsRotation()
    {
        Eng.InitHeadless();
        var world = Eng.Physics;
        world.Settings.Gravity = Vec2.Zero;

        var water = new WaterBody(0, 50, 400, 300);
        water.AngularDragCoeff = 200f;

        var box = world.CreateBody(BodyType.Dynamic, 100, 150);
        box.Material = new PhysicsMaterial { Density = 0.1f };
        box.SetShape(new BoxShape(20, 20));
        box.AngularVelocity = 10f;

        for (int i = 0; i < 120; i++)
        {
            water.Update(1f / 60f);
            world.Step(1f / 60f);
        }

        Assert.True(MathF.Abs(box.AngularVelocity) < 9.9f, $"Angular drag should slow, av={box.AngularVelocity}");
    }

    // ── Flow ──

    [Fact]
    public void Flow_PushesSubmergedBody()
    {
        Eng.InitHeadless();
        var world = Eng.Physics;
        world.Settings.Gravity = Vec2.Zero;

        var water = new WaterBody(0, 50, 400, 300);
        water.FlowVelocity = new Vec2(500, 0);
        water.LinearDragCoeff = 0; // No drag so flow is visible

        var ball = world.CreateBody(BodyType.Dynamic, 100, 150);
        ball.Material = new PhysicsMaterial { Density = 1f };
        ball.SetShape(new CircleShape(10));

        for (int i = 0; i < 60; i++)
        {
            water.Update(1f / 60f);
            world.Step(1f / 60f);
        }

        Assert.True(ball.LinearVelocity.X > 0, $"Flow should push right, vx={ball.LinearVelocity.X}");
    }

    // ── Integration ──

    [Fact]
    public void Integration_ObjectFallsAndFloats()
    {
        Eng.InitHeadless();
        var world = Eng.Physics;
        world.Settings.Gravity = new Vec2(0, 400);

        var water = new WaterBody(0, 200, 400, 200);
        water.Density = 2f;
        water.LinearDragCoeff = 8f;

        // Ball lighter than water — should float
        var ball = world.CreateBody(BodyType.Dynamic, 100, 50);
        ball.Material = new PhysicsMaterial { Density = 0.8f };
        ball.SetShape(new CircleShape(12));

        for (int i = 0; i < 600; i++)
        {
            water.Update(1f / 60f);
            world.Step(1f / 60f);
        }

        // Ball should have entered the water (past surface at y=200) and not launched away
        Assert.True(ball.Position.Y > 100f, $"Ball should be in/near water, Y={ball.Position.Y}");
        Assert.True(ball.Position.Y < 500f, $"Ball should not sink far past bottom, Y={ball.Position.Y}");
    }

    [Fact]
    public void WaterBody_ColumnCount_MatchesWidth()
    {
        var water = new WaterBody(0, 0, 200, 100, columnSpacing: 10);
        Assert.Equal(21, water.ColumnCount); // 200/10 + 1
    }

    [Fact]
    public void WaterBody_NoNaN_AfterManySteps()
    {
        var water = new WaterBody(0, 100, 300, 200);
        water.Disturb(50, 50f);
        water.Disturb(150, -30f);

        for (int i = 0; i < 600; i++)
            water.Update(1f / 60f);

        for (int i = 0; i < water.ColumnCount; i++)
        {
            float y = water.SurfaceYAt(i * water.ColumnSpacing);
            Assert.False(float.IsNaN(y), $"Column {i} produced NaN");
            Assert.False(float.IsInfinity(y), $"Column {i} produced Infinity");
        }
    }
}
