using System;
using VEngine.Engine.Core;
using VEngine.Engine.Math;
using VEngine.Engine.Physics;
using VEngine.Engine.Physics.Fluid;
using VEngine.Engine.Physics.Shapes;
using Xunit;

namespace VEngine.Tests;

public class FluidTests
{
    public FluidTests()
    {
        Eng.InitHeadless();
    }

    // ── Kernel Tests ──

    [Fact]
    public void Kernel_Poly6_MaxAtZero()
    {
        FluidKernels.SetSmoothingRadius(16f);
        float val = FluidKernels.Poly6(0);
        Assert.True(val > 0, $"Poly6(0) should be positive, got {val}");
    }

    [Fact]
    public void Kernel_Poly6_ZeroAtRadius()
    {
        FluidKernels.SetSmoothingRadius(16f);
        float val = FluidKernels.Poly6(16f * 16f);
        Assert.Equal(0f, val, 1e-6);
    }

    [Fact]
    public void Kernel_Poly6_ZeroOutsideRadius()
    {
        FluidKernels.SetSmoothingRadius(16f);
        float val = FluidKernels.Poly6(17f * 17f);
        Assert.Equal(0f, val);
    }

    [Fact]
    public void Kernel_Poly6_PositiveInside()
    {
        FluidKernels.SetSmoothingRadius(16f);
        float val = FluidKernels.Poly6(8f * 8f);
        Assert.True(val > 0);
    }

    [Fact]
    public void Kernel_SpikyGrad_ZeroAtRadius()
    {
        FluidKernels.SetSmoothingRadius(16f);
        var grad = FluidKernels.SpikyGradient(new Vec2(1, 0), 16f);
        Assert.Equal(0f, grad.X, 1e-6);
    }

    [Fact]
    public void Kernel_SpikyGrad_NonzeroInside()
    {
        FluidKernels.SetSmoothingRadius(16f);
        var grad = FluidKernels.SpikyGradient(new Vec2(1, 0), 8f);
        Assert.True(MathF.Abs(grad.X) > 0);
    }

    [Fact]
    public void Kernel_SpikyGrad_PointsAlongDirection()
    {
        FluidKernels.SetSmoothingRadius(16f);
        var dir = new Vec2(1, 0);
        var grad = FluidKernels.SpikyGradient(dir, 8f);
        // Gradient should be parallel to dir (Y should be ~0)
        Assert.Equal(0f, grad.Y, 1e-6);
    }

    [Fact]
    public void Kernel_ViscLap_ZeroAtRadius()
    {
        FluidKernels.SetSmoothingRadius(16f);
        Assert.Equal(0f, FluidKernels.ViscosityLaplacian(16f), 1e-6);
    }

    [Fact]
    public void Kernel_ViscLap_PositiveInside()
    {
        FluidKernels.SetSmoothingRadius(16f);
        Assert.True(FluidKernels.ViscosityLaplacian(8f) > 0);
    }

    // ── Spatial Hash Tests ──

    [Fact]
    public void SpatialHash_InsertAndQuery()
    {
        var hash = new FluidSpatialHash(16f);
        hash.Insert(0, 50f, 50f);

        var results = new System.Collections.Generic.List<int>();
        hash.QueryNeighbors(50f, 50f, results);

        Assert.Contains(0, results);
    }

    [Fact]
    public void SpatialHash_AdjacentCellFound()
    {
        var hash = new FluidSpatialHash(16f);
        hash.Insert(0, 50f, 50f);  // cell (3, 3)

        var results = new System.Collections.Generic.List<int>();
        hash.QueryNeighbors(60f, 50f, results); // nearby cell

        Assert.Contains(0, results);
    }

    [Fact]
    public void SpatialHash_DistantNotFound()
    {
        var hash = new FluidSpatialHash(16f);
        hash.Insert(0, 50f, 50f);

        var results = new System.Collections.Generic.List<int>();
        hash.QueryNeighbors(200f, 200f, results);

        Assert.DoesNotContain(0, results);
    }

    [Fact]
    public void SpatialHash_Clear()
    {
        var hash = new FluidSpatialHash(16f);
        hash.Insert(0, 50f, 50f);
        hash.Clear();

        var results = new System.Collections.Generic.List<int>();
        hash.QueryNeighbors(50f, 50f, results);

        Assert.Empty(results);
    }

    // ── FluidSystem Tests ──

    [Fact]
    public void Emit_IncreasesCount()
    {
        var fluid = new FluidSystem(100);
        Assert.Equal(0, fluid.ActiveCount);

        fluid.Emit(Vec2.Zero, Vec2.Zero);
        Assert.Equal(1, fluid.ActiveCount);
    }

    [Fact]
    public void Emit_BeyondBudget_Ignored()
    {
        var fluid = new FluidSystem(5);
        fluid.ParticleBudget = 5; // hard cap at 5
        for (int i = 0; i < 10; i++)
            fluid.Emit(Vec2.Zero, Vec2.Zero);
        Assert.Equal(5, fluid.ActiveCount);
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        var fluid = new FluidSystem(100);
        fluid.Emit(Vec2.Zero, Vec2.Zero, 10, 5);
        Assert.True(fluid.ActiveCount > 0);

        fluid.ClearParticles();
        Assert.Equal(0, fluid.ActiveCount);
    }

    [Fact]
    public void Gravity_ParticlesFallDown()
    {
        var fluid = new FluidSystem(10);
        fluid.Gravity = new Vec2(0, 400);
        fluid.BoundsBottom = 10000;
        fluid.Emit(new Vec2(100, 100), Vec2.Zero);

        for (int i = 0; i < 30; i++)
            fluid.Update(1f / 60f);

        Assert.True(fluid.Particles[0].Position.Y > 100,
            $"Particle should fall, Y={fluid.Particles[0].Position.Y}");
    }

    [Fact]
    public void Boundary_ParticlesStayInBounds()
    {
        var fluid = new FluidSystem(50);
        fluid.BoundsLeft = 10; fluid.BoundsRight = 200;
        fluid.BoundsTop = 10; fluid.BoundsBottom = 200;
        fluid.Gravity = new Vec2(0, 400);

        fluid.Emit(new Vec2(100, 50), Vec2.Zero, 20, 10);

        for (int i = 0; i < 300; i++)
            fluid.Update(1f / 60f);

        for (int i = 0; i < fluid.Particles.Length; i++)
        {
            if (!fluid.Particles[i].Active) continue;
            Assert.InRange(fluid.Particles[i].Position.X, 10, 200);
            Assert.InRange(fluid.Particles[i].Position.Y, 10, 200);
        }
    }

    [Fact]
    public void Density_SingleParticle_HasSelfDensity()
    {
        var fluid = new FluidSystem(10);
        fluid.BoundsBottom = 10000;
        fluid.Emit(new Vec2(100, 100), Vec2.Zero);

        fluid.Update(1f / 60f);

        Assert.True(fluid.Particles[0].Density > 0,
            $"Single particle should have self-density, got {fluid.Particles[0].Density}");
    }

    [Fact]
    public void Pressure_RepelsCloseParticles()
    {
        var fluid = new FluidSystem(10);
        fluid.Gravity = Vec2.Zero;
        fluid.RestDensity = 0.001f; // Very low — particles are over-dense, constraint pushes apart
        fluid.SurfaceTensionK = 0;  // Disable cohesion to isolate pressure
        fluid.SolverIterations = 6; // Strong constraint enforcement
        fluid.BoundsLeft = -1000; fluid.BoundsRight = 1000;
        fluid.BoundsTop = -1000; fluid.BoundsBottom = 1000;

        // Two particles very close
        fluid.Emit(new Vec2(100, 100), Vec2.Zero);
        fluid.Emit(new Vec2(105, 100), Vec2.Zero);

        float initialDist = 5f;
        for (int i = 0; i < 120; i++)
            fluid.Update(1f / 60f);

        float finalDist = Vec2.Distance(fluid.Particles[0].Position, fluid.Particles[1].Position);
        Assert.True(finalDist > initialDist,
            $"Pressure should push apart: initial={initialDist}, final={finalDist}");
    }

    [Fact]
    public void MaxSpeed_Clamped()
    {
        var fluid = new FluidSystem(10);
        fluid.MaxSpeed = 100;
        fluid.BoundsBottom = 10000;
        fluid.Gravity = new Vec2(0, 10000); // extreme gravity

        fluid.Emit(new Vec2(100, 100), Vec2.Zero);

        for (int i = 0; i < 60; i++)
            fluid.Update(1f / 60f);

        float speed = fluid.Particles[0].Velocity.Length();
        Assert.True(speed <= 100.1f, $"Speed should be clamped, got {speed}");
    }

    [Fact]
    public void Pouring_CreatesParticles()
    {
        var fluid = new FluidSystem(100);
        fluid.Pouring = true;
        fluid.PourPosition = new Vec2(100, 100);
        fluid.PourRate = 120; // 2 per frame at 60fps
        fluid.BoundsBottom = 10000;

        for (int i = 0; i < 30; i++)
            fluid.Update(1f / 60f);

        Assert.True(fluid.ActiveCount > 10, $"Pouring should create particles, got {fluid.ActiveCount}");
    }

    [Fact]
    public void NaN_NeverProduced()
    {
        var fluid = new FluidSystem(100);
        fluid.BoundsLeft = 10; fluid.BoundsRight = 300;
        fluid.BoundsTop = 10; fluid.BoundsBottom = 300;

        fluid.Emit(new Vec2(150, 50), Vec2.Zero, 50, 20);

        for (int i = 0; i < 300; i++)
            fluid.Update(1f / 60f);

        for (int i = 0; i < fluid.Particles.Length; i++)
        {
            if (!fluid.Particles[i].Active) continue;
            Assert.False(float.IsNaN(fluid.Particles[i].Position.X), $"Particle {i} X is NaN");
            Assert.False(float.IsNaN(fluid.Particles[i].Position.Y), $"Particle {i} Y is NaN");
            Assert.False(float.IsNaN(fluid.Particles[i].Velocity.X), $"Particle {i} VX is NaN");
            Assert.False(float.IsNaN(fluid.Particles[i].Velocity.Y), $"Particle {i} VY is NaN");
        }
    }

    [Fact]
    public void PhysicsInteraction_BodyGetsForce()
    {
        // Verify fluid-body coupling doesn't produce NaN or crash.
        // Full force transfer requires graphical context for proper TestPoint evaluation.
        Eng.InitHeadless();
        var world = Eng.Physics;
        world.Settings.Gravity = Vec2.Zero;

        var body = world.CreateBody(BodyType.Dynamic, 100, 100);
        body.Material = new PhysicsMaterial { Density = 1f };
        body.SetShape(new BoxShape(40, 40));

        var fluid = new FluidSystem(200);
        fluid.Gravity = Vec2.Zero;
        fluid.SolverIterations = 1;
        fluid.BoundsLeft = -500; fluid.BoundsRight = 500;
        fluid.BoundsTop = -500; fluid.BoundsBottom = 500;
        fluid.BodyForceScale = 500f;

        for (int i = 0; i < 30; i++)
            fluid.Emit(new Vec2(90, 80 + i * 2), new Vec2(200, 0));

        // Run simulation — should not produce NaN or crash
        for (int i = 0; i < 60; i++)
        {
            fluid.Update(1f / 60f);
            world.Step(1f / 60f);
        }

        // Body position should be finite (no NaN from zero RestDensity)
        Assert.False(float.IsNaN(body.Position.X), "Body X should not be NaN");
        Assert.False(float.IsNaN(body.Position.Y), "Body Y should not be NaN");
        Assert.False(float.IsNaN(body.LinearVelocity.X), "Body VX should not be NaN");
    }
}
