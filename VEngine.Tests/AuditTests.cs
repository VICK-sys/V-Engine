using System;
using System.IO;
using VEngine.Engine.Core;
using VEngine.Engine.Math;
using Xunit;

namespace VEngine.Tests;

// ── SaveData (gap coverage) ──

public class SaveDataGapTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _originalPath;

    public SaveDataGapTests()
    {
        Eng.InitHeadless();
        _originalPath = SaveData.SavesPath;
        _tempDir = Path.Combine(Path.GetTempPath(), $"vengine_test_{Guid.NewGuid():N}");
        SaveData.SavesPath = _tempDir;
    }

    public void Dispose()
    {
        SaveData.SavesPath = _originalPath;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void PathTraversal_Throws()
    {
        Assert.Throws<ArgumentException>(() => SaveData.Save("../../escape.json", "bad"));
    }

    [Fact]
    public void List_FindsSavedFiles()
    {
        SaveData.Save("a.json", 1);
        SaveData.Save("b.json", 2);
        var files = SaveData.List();
        Assert.Contains("a.json", files);
        Assert.Contains("b.json", files);
    }

    [Fact]
    public void List_EmptyWhenNoSaves()
    {
        var files = SaveData.List();
        Assert.Empty(files);
    }

    [Fact]
    public void Delete_NoOp_WhenMissing()
    {
        SaveData.Delete("nonexistent.json"); // should not throw
    }
}

// ── Camera (gap coverage) ──

public class CameraGapTests
{
    public CameraGapTests() => Eng.InitHeadless();

    [Fact]
    public void Zoom_ClampedAboveZero()
    {
        var cam = new Camera();
        cam.Zoom = -5f;
        Assert.True(cam.Zoom > 0, $"Zoom should be positive, got {cam.Zoom}");
    }

    [Fact]
    public void Zoom_DefaultIsOne()
    {
        var cam = new Camera();
        Assert.Equal(1f, cam.Zoom);
    }

    [Fact]
    public void WorldToScreen_AppliesZoom()
    {
        var cam = new Camera();
        cam.Position = Vec2.Zero;
        cam.Zoom = 2f;
        var screen = cam.WorldToScreen(10, 20);
        Assert.Equal(20f, screen.X, 0.01);
        Assert.Equal(40f, screen.Y, 0.01);
    }

    [Fact]
    public void ScreenToWorld_InvertsWithZoom()
    {
        var cam = new Camera();
        cam.Position = new Vec2(30, 40);
        cam.Zoom = 2f;
        var screen = cam.WorldToScreen(100, 200);
        var world = cam.ScreenToWorld(screen.X, screen.Y);
        Assert.Equal(100f, world.X, 0.1);
        Assert.Equal(200f, world.Y, 0.1);
    }

    [Fact]
    public void Shake_DoesNotCrash()
    {
        var cam = new Camera();
        cam.Shake(10f, 0.5f);
        cam.Update(0.016f);
        cam.Update(0.016f);
    }

    [Fact]
    public void ClearBounds_AllowsFreeMovement()
    {
        var cam = new Camera();
        cam.SetBounds(0, 0, 100, 100);
        cam.ClearBounds();
        cam.Position = new Vec2(-999, -999);
        cam.Update(0.016f);
        // Without bounds, position should stay where we set it
        Assert.Equal(-999f, cam.Position.X, 0.1);
    }
}

// ── StateMachine (gap coverage) ──

public class StateMachineGapTests
{
    [Fact]
    public void InitialState_EntersOnFirstUpdate()
    {
        bool entered = false;
        var sm = new StateMachine<string>("idle");
        sm.On("idle").Enter(() => entered = true);
        sm.Update(0.016f);
        Assert.True(entered);
    }

    [Fact]
    public void Set_CallsExitThenEnter()
    {
        var order = new System.Collections.Generic.List<string>();
        var sm = new StateMachine<string>("a");
        sm.On("a").Exit(() => order.Add("exit_a"));
        sm.On("b").Enter(() => order.Add("enter_b"));
        sm.Update(0.016f);
        sm.Set("b");
        Assert.Equal(new[] { "exit_a", "enter_b" }, order);
    }

    [Fact]
    public void Set_SameState_NoOp()
    {
        int enterCount = 0;
        var sm = new StateMachine<string>("idle");
        sm.On("idle").Enter(() => enterCount++);
        sm.Update(0.016f);
        sm.Set("idle");
        Assert.Equal(1, enterCount);
    }

    [Fact]
    public void Duration_ResetsOnTransition()
    {
        var sm = new StateMachine<string>("a");
        sm.On("a");
        sm.On("b");
        sm.Update(0.5f);
        Assert.True(sm.Duration > 0.4f);
        sm.Set("b");
        Assert.Equal(0f, sm.Duration);
    }

    [Fact]
    public void Duration_Accumulates()
    {
        var sm = new StateMachine<string>("idle");
        sm.On("idle");
        sm.Update(0.1f);
        sm.Update(0.2f);
        Assert.Equal(0.3f, sm.Duration, 0.01);
    }

    [Fact]
    public void UnregisteredState_DoesNotCrash()
    {
        var sm = new StateMachine<string>("a");
        sm.Update(0.016f);
        sm.Set("unknown");
        sm.Update(0.016f);
    }
}

// ── Sequence (gap coverage) ──

public class SequenceGapTests
{
    public SequenceGapTests() { Eng.InitHeadless(); Eng.Timers.CancelAll(); Eng.Tweens.CancelAll(); }

    [Fact]
    public void Call_ExecutesAction()
    {
        bool called = false;
        var seq = new Sequence().Call(() => called = true);
        seq.Start();
        Eng.Timers.Update(0.016f);
        Assert.True(called);
    }

    [Fact]
    public void Wait_DelaysNextStep()
    {
        bool called = false;
        var seq = new Sequence().Wait(0.1f).Call(() => called = true);
        seq.Start();
        Eng.Timers.Update(0.05f);
        Assert.False(called);
        Eng.Timers.Update(0.06f);
        Assert.True(called);
    }

    [Fact]
    public void Cancel_StopsExecution()
    {
        bool called = false;
        var seq = new Sequence().Wait(0.1f).Call(() => called = true);
        seq.Start();
        seq.Cancel();
        Eng.Timers.Update(0.2f);
        Assert.False(called);
    }

    [Fact]
    public void OnComplete_FiresAtEnd()
    {
        bool completed = false;
        var seq = new Sequence().Call(() => { }).OnComplete(() => completed = true);
        seq.Start();
        Eng.Timers.Update(0.016f);
        Eng.Timers.Update(0.016f);
        Assert.True(completed);
    }
}
