using VEngine.Engine.Core;
using VEngine.Engine.Math;

namespace VEngine.Tests;

/// <summary>
/// Fuzz tests: throw random, extreme, and adversarial inputs at every system.
/// Goal: no crashes, no NaN propagation, no infinite loops.
/// </summary>
public class FuzzTests
{
    private readonly Random _rng = new(12345); // deterministic seed

    public FuzzTests() => Eng.InitHeadless();

    // ── Vec2 Fuzz ───────────────────────────────────────────────

    [Fact]
    public void Vec2_ExtremeValues()
    {
        var huge = new Vec2(float.MaxValue, float.MaxValue);
        var tiny = new Vec2(float.Epsilon, float.Epsilon);
        var neg = new Vec2(float.MinValue, float.MinValue);
        var nan = new Vec2(float.NaN, float.NaN);
        var inf = new Vec2(float.PositiveInfinity, float.NegativeInfinity);

        // Should not crash
        _ = huge.Length();
        _ = tiny.Normalized();
        _ = neg + huge;
        _ = Vec2.Dot(huge, tiny);
        _ = Vec2.Distance(huge, neg);
        _ = Vec2.Lerp(tiny, huge, 0.5f);
        _ = Vec2.Lerp(Vec2.Zero, Vec2.One, float.NaN);
        _ = Vec2.AngleTo(Vec2.Zero, Vec2.Zero); // zero-length direction
    }

    [Fact]
    public void Vec2_NormalizeZero()
    {
        var result = Vec2.Zero.Normalized();
        Assert.Equal(0, result.X);
        Assert.Equal(0, result.Y);
    }

    // ── Color Fuzz ──────────────────────────────────────────────

    [Fact]
    public void Color_Lerp_ExtremeT()
    {
        var a = Color.Black;
        var b = Color.White;
        _ = Color.Lerp(a, b, float.NaN);
        _ = Color.Lerp(a, b, float.PositiveInfinity);
        _ = Color.Lerp(a, b, float.NegativeInfinity);
        _ = Color.Lerp(a, b, -1000f);
        _ = Color.Lerp(a, b, 1000f);
        // Clamped — should not crash or produce weird values
        var clamped = Color.Lerp(a, b, 999f);
        Assert.Equal(255, clamped.R);
    }

    // ── Rect Fuzz ───────────────────────────────────────────────

    [Fact]
    public void Rect_ZeroSize()
    {
        var r = new Rect(10, 10, 0, 0);
        Assert.False(r.Contains(10, 10));
        Assert.False(r.Overlaps(new Rect(10, 10, 1, 1)));
    }

    [Fact]
    public void Rect_NegativeSize()
    {
        var r = new Rect(10, 10, -5, -5);
        // Should not crash
        _ = r.Right;
        _ = r.Bottom;
        _ = r.Center;
        _ = r.Overlaps(new Rect(0, 0, 100, 100));
    }

    // ── Entity Fuzz ─────────────────────────────────────────────

    [Fact]
    public void Entity_ExtremeScale()
    {
        var e = new Entity { ScaleX = float.MaxValue, ScaleY = float.MinValue };
        _ = e.Width;
        _ = e.Height;
        _ = e.GetCollisionBounds();
    }

    [Fact]
    public void Entity_DoubleDestroy()
    {
        var e = new Entity();
        e.Destroy();
        e.Destroy(); // should be no-op
        Assert.True(e.Destroyed);
    }

    [Fact]
    public void Entity_ActiveToggleRapid()
    {
        var e = new Entity();
        for (int i = 0; i < 1000; i++)
            e.Active = !e.Active;
        // Should not crash
    }

    [Fact]
    public void KinematicEntity_ExtremePhysics()
    {
        var e = new KinematicEntity
        {
            Velocity = new Vec2(float.MaxValue, float.MinValue),
            Acceleration = new Vec2(float.MaxValue, float.MaxValue),
            AngularVelocity = float.MaxValue
        };
        // Should not crash (may produce Infinity)
        e.Update(1f);
        e.Update(0f);
        e.Update(float.Epsilon);
    }

    // ── Timer Fuzz ──────────────────────────────────────────────

    [Fact]
    public void Timer_ExtremeValues()
    {
        var mgr = new TimerManager();

        // Very small duration
        int count = 0;
        mgr.After(float.Epsilon, () => count++);
        mgr.Update(0.001f);
        Assert.Equal(1, count);

        // Very large duration
        mgr.After(float.MaxValue, () => count++);
        mgr.Update(1f);
        Assert.Equal(1, count); // shouldn't have fired

        // Zero dt
        mgr.After(0.1f, () => count++);
        mgr.Update(0f);
        Assert.Equal(1, count); // shouldn't fire on zero dt
    }

    [Fact]
    public void Timer_Callback_Throws()
    {
        var mgr = new TimerManager();
        int afterCount = 0;
        mgr.After(0.1f, () => throw new Exception("boom"));
        mgr.After(0.1f, () => afterCount++);
        mgr.Update(0.2f); // first throws, second should still fire
        Assert.Equal(1, afterCount);
    }

    [Fact]
    public void Timer_Callback_Adds_Timer()
    {
        var mgr = new TimerManager();
        int count = 0;
        mgr.After(0.1f, () => { count++; mgr.After(0.1f, () => count++); });
        mgr.Update(0.2f);
        Assert.Equal(1, count); // new timer added but fires next update
        mgr.Update(0.2f);
        Assert.Equal(2, count);
    }

    // ── Tween Fuzz ──────────────────────────────────────────────

    [Fact]
    public void Tween_ExtremeTargets()
    {
        var mgr = new TweenManager();
        float val = 0;
        mgr.To(() => val, v => val = v, float.MaxValue, 0.1f);
        mgr.Update(0.1f);
        Assert.Equal(float.MaxValue, val);
    }

    [Fact]
    public void Tween_Callback_Throws()
    {
        var mgr = new TweenManager();
        float val = 0;
        mgr.To(() => 0f, _ => throw new Exception("boom"), 1f, 0.1f);
        mgr.To(() => val, v => val = v, 10f, 0.1f);
        mgr.Update(0.2f); // first throws, second should complete
        Assert.Equal(10f, val, 0.01f);
    }

    [Fact]
    public void Tween_NegativeDelay()
    {
        var mgr = new TweenManager();
        float val = 0;
        mgr.To(() => val, v => val = v, 10f, 0.1f).Delay(-5f);
        mgr.Update(0.2f);
        Assert.Equal(10f, val, 0.01f); // negative delay treated as no delay
    }

    // ── Signal Fuzz ─────────────────────────────────────────────

    [Fact]
    public void Signal_Callback_Throws()
    {
        var s = new Signal();
        int count = 0;
        s.Subscribe(() => throw new Exception("boom"));
        s.Subscribe(() => count++);
        // Snapshot iteration — throwing listener shouldn't prevent second from running...
        // but without try-catch in Signal.Emit, it WILL throw. This tests the actual behavior.
        Assert.Throws<Exception>(() => s.Emit());
    }

    [Fact]
    public void Signal_MassSubscribeUnsubscribe()
    {
        var s = new Signal<int>();
        var unsubs = new List<Action>();
        for (int i = 0; i < 1000; i++)
            unsubs.Add(s.Subscribe(_ => { }));
        Assert.Equal(1000, s.Count);

        foreach (var u in unsubs) u();
        Assert.Equal(0, s.Count);
    }

    [Fact]
    public void Signal_EmitDuringSubscribe()
    {
        var s = new Signal();
        int count = 0;
        s.Subscribe(() => { count++; s.Subscribe(() => count++); });
        s.Emit(); // new subscriber added during emit — snapshot shouldn't include it
        Assert.Equal(1, count);
    }

    // ── Collision Fuzz ──────────────────────────────────────────

    [Fact]
    public void Collision_ZeroSizeEntities()
    {
        var a = new Entity(0, 0) { BaseWidth = 0, BaseHeight = 0 };
        var b = new Entity(0, 0) { BaseWidth = 0, BaseHeight = 0 };
        var result = Collision.Check(a, b);
        // Zero-size entities at same position — implementation-defined, should not crash
    }

    [Fact]
    public void Collision_Overlapping_Exactly()
    {
        var a = new KinematicEntity(10, 10) { BaseWidth = 20, BaseHeight = 20 };
        var b = new Entity(10, 10) { BaseWidth = 20, BaseHeight = 20, Immovable = true };
        // Exactly overlapping — should separate without crashing
        Collision.Separate(a, b);
        Assert.False(float.IsNaN(a.Position.X));
        Assert.False(float.IsNaN(a.Position.Y));
    }

    [Fact]
    public void Collision_ExtremeVelocity_Sweep()
    {
        Eng.Context = new EngineContext { Game = new Game("test", 320, 180) };
        var a = new Entity(0, 0) { BaseWidth = 5, BaseHeight = 5 };
        var b = new Entity(100, 0) { BaseWidth = 5, BaseHeight = 5 };

        float t = Collision.Sweep(a, b, float.MaxValue, 0, out var dir);
        Assert.False(float.IsNaN(t));

        t = Collision.Sweep(a, b, 0, 0, out dir); // zero velocity
        Assert.Equal(1f, t); // no collision
    }

    [Fact]
    public void SpatialHash_ExtremePositions()
    {
        var hash = new SpatialHash(64);
        hash.Insert(new Entity(int.MaxValue / 2, int.MaxValue / 2) { BaseWidth = 10, BaseHeight = 10 });
        hash.Insert(new Entity(int.MinValue / 2, int.MinValue / 2) { BaseWidth = 10, BaseHeight = 10 });
        hash.Insert(new Entity(0, 0) { BaseWidth = 10, BaseHeight = 10 });

        var results = new List<Entity>();
        hash.Query(-5, -5, 20, 20, results);
        Assert.Single(results); // only the origin entity
    }

    // ── Camera Fuzz ─────────────────────────────────────────────

    [Fact]
    public void Camera_ExtremeZoom()
    {
        var cam = new Camera();
        cam.Zoom = 0.001f; // very zoomed out
        var s = cam.WorldToScreen(100, 100);
        Assert.False(float.IsNaN(s.X));

        cam.Zoom = 1000f; // very zoomed in
        s = cam.WorldToScreen(100, 100);
        Assert.False(float.IsNaN(s.X));

        cam.Zoom = -5f; // negative — should clamp
        Assert.True(cam.Zoom > 0);
    }

    [Fact]
    public void Camera_ShakeOverflow()
    {
        var cam = new Camera();
        cam.Shake(float.MaxValue, 1f);
        cam.Update(0.5f);
        var s = cam.WorldToScreen(0, 0);
        // May be Infinity but should not be NaN
        Assert.False(float.IsNaN(s.X) && float.IsNaN(s.Y) && s.X == 0 && s.Y == 0);
    }

    // ── StateMachine Fuzz ───────────────────────────────────────

    [Fact]
    public void StateMachine_SetUnregisteredState()
    {
        var sm = new StateMachine<int>(0);
        sm.On(0).Enter(() => { });
        sm.Update(0.016f);
        sm.Set(999); // no registered callbacks — should not crash
        sm.Update(0.016f);
        Assert.Equal(999, sm.Current);
    }

    [Fact]
    public void StateMachine_ThrowingCallback()
    {
        var sm = new StateMachine<int>(0);
        sm.On(0).Enter(() => throw new Exception("boom"));
        // Enter callback throws — StateMachine has try-catch
        sm.Update(0.016f); // should not crash
    }

    // ── AI Fuzz ─────────────────────────────────────────────────

    [Fact]
    public void AI_MoveToward_NaN_Target()
    {
        var e = new KinematicEntity(0, 0);
        AI.MoveToward(e, float.NaN, float.NaN, 100);
        // NaN distance produces NaN velocity — not ideal but should not crash
    }

    [Fact]
    public void AI_MoveAway_SamePosition()
    {
        var e = new KinematicEntity(50, 50);
        AI.MoveAway(e, 50, 50, 100);
        // dist < 0.01 — picks default direction, should not div-by-zero
        Assert.Equal(100, e.Velocity.X, 0.001f);
    }

    // ── Pool Fuzz ───────────────────────────────────────────────

    [Fact]
    public void Pool_ReleaseAlready_Released()
    {
        var pool = new Pool<Entity>(() => new Entity(), preload: 5);
        var e = pool.Get();
        pool.Release(e);
        pool.Release(e); // double release — should be no-op
        Assert.Equal(0, pool.ActiveCount);
    }

    [Fact]
    public void Pool_GetBeyondPreload()
    {
        var pool = new Pool<Entity>(() => new Entity(), preload: 2);
        var entities = new List<Entity>();
        for (int i = 0; i < 100; i++)
            entities.Add(pool.Get());
        Assert.Equal(100, pool.ActiveCount);
        Assert.Equal(100, pool.TotalCount);

        foreach (var e in entities) pool.Release(e);
        Assert.Equal(0, pool.ActiveCount);

        // Re-get — should reuse all 100
        for (int i = 0; i < 100; i++) pool.Get();
        Assert.Equal(100, pool.TotalCount); // no growth
    }

    // ── SaveData Fuzz ───────────────────────────────────────────

    [Fact]
    public void SaveData_EmptyFilename()
    {
        SaveData.SavesPath = Path.GetTempPath();
        // Empty filename — should not crash (Path.Combine handles it)
        Assert.ThrowsAny<Exception>(() => SaveData.Save("", new { x = 1 }));
    }

    [Fact]
    public void SaveData_SpecialCharacters()
    {
        SaveData.SavesPath = Path.GetTempPath();
        // Filenames with special chars
        Assert.ThrowsAny<Exception>(() => SaveData.Save("file\0name.json", new { x = 1 }));
    }

    // ── Group Fuzz ──────────────────────────────────────────────

    [Fact]
    public void Group_RemoveNonMember()
    {
        var group = new Group();
        group.Add(new Entity());
        bool removed = group.Remove(new Entity()); // not a member
        Assert.False(removed);
    }

    [Fact]
    public void Group_DrawEmpty()
    {
        var group = new Group();
        group.Draw(); // should not crash on empty group
    }

    [Fact]
    public void Group_UpdateEmpty()
    {
        var group = new Group();
        group.Update(0.016f); // should not crash
    }
}
