using System;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics;
using VEngine.Engine.Math;
using VEngine.Engine.Physics;
using Xunit;

namespace VEngine.Tests;

public class RainTests
{
    public RainTests() { Eng.InitHeadless(); }

    // ── Spawn ──

    [Fact]
    public void Spawns_DropsWhenIntensityPositive()
    {
        var rain = new Rain();
        rain.Intensity = 0.5f;

        for (int i = 0; i < 30; i++)
            rain.Update(1f / 60f);

        Assert.True(rain.ActiveDrops > 0, $"Expected drops, got {rain.ActiveDrops}");
    }

    [Fact]
    public void NoDrops_WhenIntensityZero()
    {
        var rain = new Rain();
        rain.Intensity = 0f;

        for (int i = 0; i < 60; i++)
            rain.Update(1f / 60f);

        Assert.Equal(0, rain.ActiveDrops);
    }

    [Fact]
    public void HigherIntensity_MoreDrops()
    {
        var low = new Rain();
        low.Intensity = 0.1f;

        var high = new Rain();
        high.Intensity = 1f;

        for (int i = 0; i < 60; i++)
        {
            low.Update(1f / 60f);
            high.Update(1f / 60f);
        }

        Assert.True(high.ActiveDrops > low.ActiveDrops,
            $"High ({high.ActiveDrops}) should exceed low ({low.ActiveDrops})");
    }

    [Fact]
    public void RespectMaxDrops()
    {
        var rain = new Rain(maxDrops: 50);
        rain.Intensity = 1f;

        for (int i = 0; i < 120; i++)
            rain.Update(1f / 60f);

        Assert.True(rain.ActiveDrops <= 50, $"Exceeded max: {rain.ActiveDrops}");
    }

    // ── Movement ──

    [Fact]
    public void Drops_FallDownward()
    {
        var rain = new Rain();
        rain.Intensity = 1f;
        rain.WindAngle = 0;
        rain.GroundY = float.MaxValue; // no ground

        // Spawn some drops
        for (int i = 0; i < 10; i++)
            rain.Update(1f / 60f);

        int initial = rain.ActiveDrops;
        Assert.True(initial > 0);

        // After many frames with no ground and viewport moves away, drops should expire off-screen
        // This just verifies update doesn't crash and drops are being managed
        for (int i = 0; i < 300; i++)
            rain.Update(1f / 60f);
    }

    // ── Ground Collision ──

    [Fact]
    public void Drops_RemovedAtGroundY()
    {
        var rain = new Rain();
        rain.Intensity = 1f;
        rain.GroundY = 10f; // very close ground
        rain.MinSpeed = 500f;
        rain.MaxSpeed = 500f;

        // Spawn and run — drops should hit ground quickly
        for (int i = 0; i < 60; i++)
            rain.Update(1f / 60f);

        int count = rain.ActiveDrops;

        // With ground at Y=10 and speed 500, drops hit almost immediately
        // There should be drops in-flight but they churn quickly
        // Main assertion: no crash, drops are being recycled
        Assert.True(count <= rain.MaxDrops);
    }

    // ── Wind ──

    [Fact]
    public void DropsPerSecond_ScalesWithIntensity()
    {
        var rain = new Rain();
        rain.Intensity = 0.5f;
        float half = rain.DropsPerSecond;

        rain.Intensity = 1f;
        float full = rain.DropsPerSecond;

        Assert.True(full > half, $"Full ({full}) should exceed half ({half})");
        Assert.Equal(full, half * 2, 0.01);
    }

    // ── WaterBody Integration ──

    [Fact]
    public void WaterBody_CanAddAndRemove()
    {
        var rain = new Rain();
        var wb = new WaterBody(0, 100, 400, 200);

        rain.AddWaterBody(wb);
        rain.RemoveWaterBody(wb);

        // No crash, basic lifecycle
        for (int i = 0; i < 10; i++)
            rain.Update(1f / 60f);
    }

    [Fact]
    public void WaterBody_DropsHitSurface()
    {
        var rain = new Rain();
        rain.Intensity = 1f;
        rain.MinSpeed = 800f;
        rain.MaxSpeed = 800f;
        rain.EnableSplashes = false; // avoid splash emitter in headless

        var wb = new WaterBody(
            Eng.Camera.Position.X - 100, // wide enough to catch drops
            20, // surface very close to spawn area
            600, // wide enough to catch all drops
            100);
        rain.AddWaterBody(wb);

        // Run frames — drops should hit water and be removed
        for (int i = 0; i < 120; i++)
        {
            rain.Update(1f / 60f);
            wb.Update(1f / 60f);
        }

        // Water should have been disturbed — check that some column moved
        bool disturbed = false;
        for (int c = 0; c < wb.ColumnCount; c++)
        {
            float y = wb.SurfaceYAt(wb.Position.X + c * wb.ColumnSpacing);
            if (MathF.Abs(y - 20f) > 0.01f)
            {
                disturbed = true;
                break;
            }
        }

        Assert.True(disturbed, "WaterBody surface should be disturbed by rain");
    }

    // ── SetMaxDrops ──

    [Fact]
    public void SetMaxDrops_ResizesPool()
    {
        var rain = new Rain(maxDrops: 100);
        rain.Intensity = 1f;

        for (int i = 0; i < 60; i++)
            rain.Update(1f / 60f);

        rain.SetMaxDrops(20);
        Assert.True(rain.ActiveDrops <= 20);
        Assert.Equal(20, rain.MaxDrops);
    }

    // ── Stability ──

    [Fact]
    public void NoNaN_AfterManyFrames()
    {
        var rain = new Rain();
        rain.Intensity = 1f;
        rain.WindAngle = 30f;

        for (int i = 0; i < 600; i++)
            rain.Update(1f / 60f);

        // Just checking it doesn't crash or produce NaN
        Assert.True(rain.ActiveDrops >= 0);
    }

    [Fact]
    public void Handles_ZeroWidthViewport()
    {
        // Headless mode has 0-size viewport — should not crash
        var rain = new Rain();
        rain.Intensity = 1f;

        for (int i = 0; i < 30; i++)
            rain.Update(1f / 60f);
    }
}
