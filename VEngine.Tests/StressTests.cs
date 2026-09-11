using VEngine.Engine.Core;
using VEngine.Engine.Math;

namespace VEngine.Tests;

public class StressTests
{
    public StressTests() => Eng.InitHeadless();

    // ── Entity/Group stress ─────────────────────────────────────

    [Fact]
    public void Group_10000_Entities_Update()
    {
        var group = new Group();
        for (int i = 0; i < 10_000; i++)
            group.Add(new KinematicEntity(i, i) { Velocity = new Vec2(1, 0) });

        group.Update(0.016f);
        var last = (KinematicEntity)group.Members[9999];
        Assert.True(last.Position.X > 9999);
    }

    [Fact]
    public void Group_MassRemoval_During_Update()
    {
        var group = new Group();
        var removers = new List<Entity>();
        for (int i = 0; i < 500; i++)
        {
            var e = new SelfRemovingEntity(group);
            group.Add(e);
            removers.Add(e);
        }
        // Every entity removes itself during Update — should not crash
        group.Update(0.016f);
        Assert.Empty(group.Members);
    }

    [Fact]
    public void Group_AddDuring_Update()
    {
        var group = new Group();
        var spawner = new SpawnerEntity(group, spawnCount: 100);
        group.Add(spawner);
        group.Update(0.016f);
        Assert.True(group.Members.Count >= 101); // spawner + 100 spawned
    }

    // ── Pool stress ─────────────────────────────────────────────

    [Fact]
    public void Pool_RapidGetRelease_1000()
    {
        var pool = new Pool<KinematicEntity>(() => new KinematicEntity(), preload: 100);
        var active = new List<KinematicEntity>();

        for (int cycle = 0; cycle < 10; cycle++)
        {
            // Get 100
            for (int i = 0; i < 100; i++)
                active.Add(pool.Get(i, cycle));
            Assert.Equal(100, pool.ActiveCount);

            // Release all
            foreach (var e in active) pool.Release(e);
            active.Clear();
            Assert.Equal(0, pool.ActiveCount);
        }

        // Total should not grow beyond preloaded (reuse works)
        Assert.Equal(100, pool.TotalCount);
    }

    // ── Timer stress ────────────────────────────────────────────

    [Fact]
    public void Timer_1000_Simultaneous()
    {
        var mgr = new TimerManager();
        int count = 0;
        for (int i = 0; i < 1000; i++)
            mgr.After(0.1f, () => Interlocked.Increment(ref count));

        mgr.Update(0.2f); // all fire
        Assert.Equal(1000, count);
        Assert.Equal(0, mgr.Count);
    }

    [Fact]
    public void Timer_CancelAll_From_Callback()
    {
        var mgr = new TimerManager();
        int count = 0;
        mgr.After(0.1f, () => { count++; mgr.CancelAll(); });
        for (int i = 0; i < 99; i++)
            mgr.After(0.1f, () => count++);

        mgr.Update(0.2f); // first callback cancels all — should not crash
        Assert.True(count >= 1);
    }

    [Fact]
    public void Timer_Repeating_HighFrequency()
    {
        var mgr = new TimerManager();
        int count = 0;
        mgr.Every(0.001f, () => count++); // 1ms interval
        mgr.Update(1f); // 1 second = 1000 fires
        Assert.True(count >= 999);
    }

    // ── Tween stress ────────────────────────────────────────────

    [Fact]
    public void Tween_500_Simultaneous()
    {
        var mgr = new TweenManager();
        float[] values = new float[500];
        for (int i = 0; i < 500; i++)
        {
            int idx = i;
            mgr.To(() => values[idx], v => values[idx] = v, 100f, 1f);
        }

        mgr.Update(1f);
        foreach (var v in values)
            Assert.Equal(100f, v, 0.01f);
    }

    [Fact]
    public void Tween_CancelAll_From_OnComplete()
    {
        var mgr = new TweenManager();
        int completeCount = 0;
        mgr.To(() => 0f, _ => { }, 1f, 0.1f).OnComplete(() => { completeCount++; mgr.CancelAll(); });
        for (int i = 0; i < 50; i++)
            mgr.To(() => 0f, _ => { }, 1f, 0.1f).OnComplete(() => completeCount++);

        mgr.Update(0.2f); // all complete, first cancels all — should not crash
        Assert.True(completeCount >= 1);
    }

    // ── Signal stress ───────────────────────────────────────────

    [Fact]
    public void Signal_10000_Listeners()
    {
        var signal = new Signal<int>();
        int total = 0;
        for (int i = 0; i < 10_000; i++)
            signal.Subscribe(v => Interlocked.Add(ref total, v));

        signal.Emit(1);
        Assert.Equal(10_000, total);
    }

    [Fact]
    public void Signal_Unsubscribe_During_Emit()
    {
        var signal = new Signal();
        int count = 0;
        Action? unsub2 = null;
        signal.Subscribe(() => { count++; unsub2?.Invoke(); }); // listener 1 removes listener 2
        unsub2 = signal.Subscribe(() => count++);
        signal.Subscribe(() => count++);

        signal.Emit();
        // Snapshot iteration: all 3 fire because snapshot was taken before removal
        Assert.Equal(3, count);
    }

    // ── Collision stress ────────────────────────────────────────

    [Fact]
    public void SpatialHash_1000_Entities()
    {
        var hash = new SpatialHash(64);
        var rng = new Random(42);
        var entities = new List<Entity>();

        for (int i = 0; i < 1000; i++)
        {
            var e = new Entity(rng.Next(-1000, 1000), rng.Next(-1000, 1000))
                { BaseWidth = 10, BaseHeight = 10 };
            entities.Add(e);
            hash.Insert(e);
        }

        // Query near origin — should find nearby entities without scanning all 1000
        var results = new List<Entity>();
        hash.Query(-50, -50, 100, 100, results);
        Assert.True(results.Count < 1000); // spatial filtering worked
        Assert.True(results.Count >= 0);
    }

    [Fact]
    public void Collision_Separate_1000_Pairs()
    {
        Eng.Context = new EngineContext { Game = new Game("test", 320, 180) };
        var ground = new Entity(0, 100) { BaseWidth = 10000, BaseHeight = 100, Immovable = true };
        var entities = new List<KinematicEntity>();

        for (int i = 0; i < 1000; i++)
        {
            var e = new KinematicEntity(i * 5, 92) { BaseWidth = 4, BaseHeight = 10, Velocity = new Vec2(0, 50) };
            entities.Add(e);
        }

        // Separate all against ground — should not crash
        foreach (var e in entities)
            Collision.Separate(e, ground);

        // All should have velocity.Y zeroed
        foreach (var e in entities)
            Assert.Equal(0, e.Velocity.Y, 0.001f);
    }

    // ── StateMachine stress ─────────────────────────────────────

    [Fact]
    public void StateMachine_RapidTransitions()
    {
        var sm = new StateMachine<int>(0);
        for (int i = 0; i < 100; i++)
            sm.On(i).Enter(() => { }).Update(_ => { }).Exit(() => { });

        sm.Update(0.016f); // enter state 0
        for (int i = 1; i < 100; i++)
        {
            sm.Set(i);
            sm.Update(0.016f);
        }
        Assert.Equal(99, sm.Current);
    }

    // ── Behavior stress ─────────────────────────────────────────

    [Fact]
    public void Entity_100_Behaviors()
    {
        var entity = new Entity();
        var counters = new int[100];
        for (int i = 0; i < 100; i++)
        {
            int idx = i;
            entity.AddBehavior(new CountBehavior(() => counters[idx]++));
        }

        entity.Update(0.016f);
        foreach (var c in counters)
            Assert.Equal(1, c);

        entity.Destroy();
    }

    // ── Edge cases ──────────────────────────────────────────────

    [Fact]
    public void Effects_Freeze_Zero_Duration_NoOp()
    {
        var fx = new Effects();
        fx.Freeze(0); // should be no-op (guard added)
        Assert.Equal(1f, fx.TimeScale);
    }

    [Fact]
    public void Camera_Shake_Zero_Duration_NoOp()
    {
        var cam = new Camera();
        cam.Shake(10f, 0f); // should be no-op (guard added)
        cam.Update(0.016f); // should not produce NaN
        var screen = cam.WorldToScreen(100, 100);
        Assert.False(float.IsNaN(screen.X));
    }

    [Fact]
    public void SaveData_PathTraversal_AllVariants()
    {
        SaveData.SavesPath = Path.GetTempPath();
        Assert.Throws<ArgumentException>(() => SaveData.Save("../escape.json", 1));
        Assert.Throws<ArgumentException>(() => SaveData.Save("..\\escape.json", 1));
        Assert.Throws<ArgumentException>(() => SaveData.Load<int>("../../../etc/passwd"));
        Assert.Throws<ArgumentException>(() => SaveData.Delete("../important.db"));
    }

    [Fact]
    public void AI_MoveToward_ZeroDistance()
    {
        var e = new KinematicEntity(50, 50);
        bool arrived = AI.MoveToward(e, 50, 50, 100, arriveDistance: 0);
        Assert.True(arrived); // dist < 0.001 guard
        Assert.Equal(0, e.Velocity.X, 0.001f);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private class SelfRemovingEntity : Entity
    {
        private readonly Group _group;
        public SelfRemovingEntity(Group g) { _group = g; }
        public override void Update(float dt) { base.Update(dt); _group.Remove(this, destroy: false); }
    }

    private class SpawnerEntity : Entity
    {
        private readonly Group _group;
        private readonly int _count;
        private bool _spawned;
        public SpawnerEntity(Group g, int spawnCount) { _group = g; _count = spawnCount; }
        public override void Update(float dt)
        {
            base.Update(dt);
            if (_spawned) return;
            _spawned = true;
            for (int i = 0; i < _count; i++) _group.Add(new Entity());
        }
    }

    private class CountBehavior : Behavior
    {
        private readonly Action _onUpdate;
        public CountBehavior(Action onUpdate) { _onUpdate = onUpdate; }
        public override void Update(float dt) => _onUpdate();
    }
}
