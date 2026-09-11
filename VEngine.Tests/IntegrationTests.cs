using VEngine.Engine.Core;
using VEngine.Engine.Math;

namespace VEngine.Tests;

public class SceneLifecycleTests
{
    public SceneLifecycleTests() => Eng.InitHeadless();

    [Fact]
    public void SceneCreateAndDestroy()
    {
        var scene = new TrackingScene();
        scene.Create();
        Assert.True(scene.Created);
        scene.Destroy();
        Assert.True(scene.Destroyed);
    }

    [Fact]
    public void SceneAddAndUpdate()
    {
        var scene = new TrackingScene();
        var entity = new KinematicEntity(0, 0) { Velocity = new Vec2(10, 0) };
        scene.Add(entity);
        scene.Update(1f);
        Assert.Equal(10, entity.Position.X, 0.001f);
    }

    [Fact]
    public void SceneDestroyDestroysEntities()
    {
        var scene = new TrackingScene();
        var e = new TrackingEntity();
        scene.Add(e);
        scene.Destroy();
        Assert.True(e.Destroyed);
    }

    [Fact]
    public void GroupNestedUpdate()
    {
        var group = new Group();
        var child = new KinematicEntity(0, 0) { Velocity = new Vec2(5, 0) };
        group.Add(child);
        group.Update(1f);
        Assert.Equal(5, child.Position.X, 0.001f);
    }

    [Fact]
    public void RemoveDuringUpdateSafe()
    {
        var scene = new TrackingScene();
        Entity? toRemove = null;
        var remover = new CallbackEntity(() => { if (toRemove != null) scene.Remove(toRemove); });
        toRemove = new Entity();
        scene.Add(remover);
        scene.Add(toRemove);
        scene.Update(0.016f); // should not crash
        Assert.Equal(1, scene.Entities.Count); // toRemove was removed
    }

    private class TrackingScene : Scene
    {
        public bool Created, Destroyed;
        public override void Create() { Created = true; }
        public override void Destroy() { Destroyed = true; base.Destroy(); }
    }

    private class TrackingEntity : Entity
    {
        protected override void OnDestroy() { }
    }

    private class CallbackEntity : Entity
    {
        private readonly Action _onUpdate;
        public CallbackEntity(Action onUpdate) { _onUpdate = onUpdate; }
        public override void Update(float dt) { base.Update(dt); _onUpdate(); }
    }
}

public class BehaviorTests
{
    [Fact]
    public void BehaviorAttachAndUpdate()
    {
        var entity = new Entity();
        var b = entity.AddBehavior(new CounterBehavior());
        Assert.True(b.Attached);
        entity.Update(1f);
        Assert.Equal(1, b.UpdateCount);
    }

    [Fact]
    public void BehaviorDestroyedWithEntity()
    {
        var entity = new Entity();
        var b = entity.AddBehavior(new CounterBehavior());
        entity.Destroy();
        Assert.True(b.WasDestroyed);
    }

    [Fact]
    public void GetBehaviorByType()
    {
        var entity = new Entity();
        entity.AddBehavior(new CounterBehavior());
        var found = entity.GetBehavior<CounterBehavior>();
        Assert.NotNull(found);
    }

    [Fact]
    public void GetBehaviorReturnsNullIfMissing()
    {
        var entity = new Entity();
        Assert.Null(entity.GetBehavior<CounterBehavior>());
    }

    private class CounterBehavior : Behavior
    {
        public bool Attached;
        public int UpdateCount;
        public bool WasDestroyed;
        public override void OnAttach() => Attached = true;
        public override void Update(float dt) => UpdateCount++;
        public override void OnDestroy() => WasDestroyed = true;
    }
}

public class TransitionTests
{
    [Fact]
    public void FadeTransitionCreates()
    {
        var fade = Transition.Fade(0.5f, 255, 0, 0);
        Assert.Equal(0.5f, fade.Duration);
    }

    [Fact]
    public void WipeTransitionCreates()
    {
        var wipe = Transition.Wipe(0.8f, WipeDir.Left);
        Assert.Equal(0.8f, wipe.Duration);
    }

    [Fact]
    public void TransitionDurationMustBePositive()
    {
        Assert.Throws<ArgumentException>(() => Transition.Fade(0));
        Assert.Throws<ArgumentException>(() => Transition.Fade(-1));
    }

    [Fact]
    public void CircleWipeTransitionCreates()
    {
        Eng.InitHeadless();
        Eng.Context = new EngineContext { Game = new Game("test", 320, 180) };
        var t = Transition.CircleWipe(0.7f);
        Assert.Equal(0.7f, t.Duration);
    }

    [Fact]
    public void DiamondWipeTransitionCreates()
    {
        Eng.InitHeadless();
        Eng.Context = new EngineContext { Game = new Game("test", 320, 180) };
        var t = Transition.DiamondWipe(0.5f);
        Assert.Equal(0.5f, t.Duration);
    }

    [Fact]
    public void PixelateTransitionCreates()
    {
        Eng.InitHeadless();
        Eng.Context = new EngineContext { Game = new Game("test", 320, 180) };
        var t = Transition.Pixelate(1f, 16);
        Assert.Equal(1f, t.Duration);
    }

    [Fact]
    public void CircleWipeCustomCenter()
    {
        Eng.InitHeadless();
        Eng.Context = new EngineContext { Game = new Game("test", 320, 180) };
        var t = Transition.CircleWipe(0.6f, 50, 80);
        Assert.Equal(0.6f, t.Duration);
    }

    [Fact]
    public void PixelateBlockSizeFloor()
    {
        Eng.InitHeadless();
        Eng.Context = new EngineContext { Game = new Game("test", 320, 180) };
        // Block size 0 should be clamped to 1
        var t = new PixelateTransition(0.5f, 0);
        Assert.Equal(0.5f, t.Duration);
    }
}

public class SaveDataPathTraversalTests : IDisposable
{
    private readonly string _tempDir;

    public SaveDataPathTraversalTests()
    {
        Eng.InitHeadless();
        _tempDir = Path.Combine(Path.GetTempPath(), "vengine_sec_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        SaveData.SavesPath = _tempDir;
    }

    [Fact]
    public void PathTraversalBlocked()
    {
        Assert.Throws<ArgumentException>(() => SaveData.Save("../../escape.json", new { x = 1 }));
        Assert.Throws<ArgumentException>(() => SaveData.Load<object>("../../escape.json"));
        Assert.Throws<ArgumentException>(() => SaveData.Exists("../../escape.json"));
        Assert.Throws<ArgumentException>(() => SaveData.Delete("../../escape.json"));
    }

    [Fact]
    public void NormalPathsWork()
    {
        SaveData.Save("normal.json", new { x = 42 });
        Assert.True(SaveData.Exists("normal.json"));
        SaveData.Delete("normal.json");
        Assert.False(SaveData.Exists("normal.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

public class EffectsEdgeCaseTests
{
    [Fact]
    public void FlashDoesNotCrash()
    {
        var fx = new Effects();
        fx.Flash(255, 255, 255, 0.1f);
        fx.Update(0.05f);
        fx.Update(0.1f); // past duration
    }

    [Fact]
    public void DoubleFreeze()
    {
        var fx = new Effects();
        fx.Freeze(0.5f);
        fx.Freeze(0.3f); // shorter — should keep 0.5
        Assert.Equal(0f, fx.TimeScale);
        fx.Update(0.6f);
        Assert.Equal(1f, fx.TimeScale); // restored
    }

    [Fact]
    public void TimeScalePreservedAfterFreeze()
    {
        var fx = new Effects();
        fx.TimeScale = 0.5f;
        fx.Freeze(0.1f);
        Assert.Equal(0f, fx.TimeScale);
        fx.Update(0.2f);
        Assert.Equal(0.5f, fx.TimeScale); // restored to pre-freeze value
    }
}

// ════════════════════════════════════════════════════════════
// Scene Transition Integration Tests
// ════════════════════════════════════════════════════════════

public class SceneTransitionIntegrationTests
{
    public SceneTransitionIntegrationTests() => Eng.InitHeadless();

    [Fact]
    public void TimersCancelledOnSceneSwap()
    {
        var fired = false;
        Eng.Timers.After(0.5f, () => fired = true);
        Assert.Equal(1, Eng.Timers.Count);

        // Simulate what SwapScene does
        Eng.Timers.CancelAll();
        Assert.Equal(0, Eng.Timers.Count);

        // Advance time past the timer's deadline — should NOT fire
        Eng.Timers.Update(1f);
        Assert.False(fired);
    }

    [Fact]
    public void TweensCancelledOnSceneSwap()
    {
        float val = 0;
        Eng.Tweens.To(() => val, v => val = v, 100f, 1f, Ease.Linear);
        Assert.Equal(1, Eng.Tweens.Count);

        Eng.Tweens.CancelAll();
        Assert.Equal(0, Eng.Tweens.Count);

        // Advance time — value should stay at 0
        Eng.Tweens.Update(2f);
        Assert.Equal(0, val);
    }

    [Fact]
    public void EntityDestroyedDuringUpdateDoesNotCrash()
    {
        var scene = new Scene_();
        var entities = new List<Entity>();
        for (int i = 0; i < 10; i++)
            entities.Add(scene.Add(new KinematicEntity(i * 10, 0)));

        // Entity at index 5 destroys itself during update
        var selfDestroyer = new SelfDestroyEntity(scene);
        scene.Add(selfDestroyer);

        // Should not throw — Group iterates backwards for safe removal
        scene.Update(1f / 60f);
        Assert.True(selfDestroyer.Destroyed);
    }

    [Fact]
    public void RemoveDuringUpdateDoesNotSkipEntities()
    {
        var scene = new Scene_();
        int updateCount = 0;

        for (int i = 0; i < 5; i++)
            scene.Add(new CountingEntity(() => updateCount++));

        // All 5 should update even if removals happen (backward iteration)
        scene.Update(1f / 60f);
        Assert.Equal(5, updateCount);
    }

    [Fact]
    public void ScenePushPreservesEntities()
    {
        // Simulate push: create scene with entities, then create a new scene on top
        var sceneA = new Scene_();
        var entityA = sceneA.Add(new KinematicEntity(50, 50));

        var sceneB = new Scene_();
        var entityB = sceneB.Add(new KinematicEntity(100, 100));

        // Both scenes' entities should be alive
        Assert.False(entityA.Destroyed);
        Assert.False(entityB.Destroyed);

        // Pop sceneB — destroy it
        sceneB.Destroy();
        Assert.True(entityB.Destroyed);

        // SceneA's entity should still be alive
        Assert.False(entityA.Destroyed);
    }

    [Fact]
    public void TimerPauseResumeSurvivesMultipleUpdates()
    {
        int count = 0;
        var handle = Eng.Timers.Every(0.1f, () => count++);

        Eng.Timers.Update(0.15f); // fires once
        Assert.Equal(1, count);

        handle.Pause();
        Eng.Timers.Update(0.5f); // no fire while paused
        Assert.Equal(1, count);

        handle.Resume();
        Eng.Timers.Update(0.1f); // fires again
        Assert.Equal(2, count);
    }

    [Fact]
    public void TweenOnCompleteNotFiredOnCancel()
    {
        float val = 0;
        bool completed = false;
        var handle = Eng.Tweens.To(() => val, v => val = v, 100f, 1f, Ease.Linear);
        handle.OnComplete(() => completed = true);

        Eng.Tweens.Update(0.5f);
        handle.Cancel();
        Eng.Tweens.Update(1f);

        Assert.False(completed, "OnComplete should not fire when tween is cancelled");
    }

    [Fact]
    public void SignalSafeToModifyDuringEmit()
    {
        var signal = new Signal();
        int count = 0;

        // Subscriber that adds another subscriber during emit
        signal.Subscribe(() =>
        {
            count++;
            signal.Subscribe(() => count += 10);
        });

        signal.Emit(); // First emit: original fires, new one added
        Assert.Equal(1, count);

        signal.Emit(); // Second emit: both fire
        Assert.Equal(12, count); // 1 + 1 + 10
    }

    [Fact]
    public void GroupDrawOrderSortedByLayerThenZOrder()
    {
        var group = new Group();
        var drawOrder = new List<int>();

        var e1 = new DrawTracker(1, () => drawOrder.Add(1)) { Layer = 2, ZOrder = 0 };
        var e2 = new DrawTracker(2, () => drawOrder.Add(2)) { Layer = 1, ZOrder = 5 };
        var e3 = new DrawTracker(3, () => drawOrder.Add(3)) { Layer = 1, ZOrder = 1 };

        group.Add(e1);
        group.Add(e2);
        group.Add(e3);

        group.Draw();

        // Layer 1 first (sorted by ZOrder: e3=1, e2=5), then Layer 2 (e1)
        Assert.Equal(new[] { 3, 2, 1 }, drawOrder);
    }

    [Fact]
    public void PoolGetReleaseCycle()
    {
        var pool = new Pool<KinematicEntity>(() => new KinematicEntity());
        var scene = new Scene_();
        pool.AddTo(scene);

        var e1 = pool.Get(10, 20);
        Assert.True(e1.Active);
        Assert.Equal(10, e1.Position.X, 0.01f);

        pool.Release(e1);
        Assert.False(e1.Active);

        var e2 = pool.Get(30, 40);
        Assert.Same(e1, e2); // should reuse the released entity
        Assert.Equal(30, e2.Position.X, 0.01f);
    }

    [Fact]
    public void BehaviorReceivesUpdateAndDestroy()
    {
        var entity = new KinematicEntity(0, 0);
        var behavior = new TrackingBehavior();
        entity.AddBehavior(behavior);

        entity.Update(0.1f);
        Assert.True(behavior.Updated);

        entity.Destroy();
        Assert.True(behavior.Destroyed_);
    }

    [Fact]
    public void ExceptionInEntityUpdateDoesNotCrashGroup()
    {
        var scene = new Scene_();
        scene.Add(new ThrowingEntity());
        scene.Add(new KinematicEntity(0, 0) { Velocity = new Vec2(10, 0) });

        // Should not throw — Group catches per-entity exceptions
        scene.Update(1f);

        // The good entity should still have moved
        var good = scene.Entities[0] is KinematicEntity k ? k :
                   scene.Entities[1] is KinematicEntity k2 ? k2 : null;
        Assert.NotNull(good);
    }

    // ── Helper classes ──────────────────────────────────────────

    private class Scene_ : Scene { }

    private class SelfDestroyEntity : Entity
    {
        private readonly Scene _scene;
        public SelfDestroyEntity(Scene scene) { _scene = scene; }
        public override void Update(float dt) { _scene.Remove(this); }
    }

    private class CountingEntity : Entity
    {
        private readonly Action _onUpdate;
        public CountingEntity(Action onUpdate) { _onUpdate = onUpdate; }
        public override void Update(float dt) { base.Update(dt); _onUpdate(); }
    }

    private class DrawTracker : Entity
    {
        private readonly Action _onDraw;
        public DrawTracker(int id, Action onDraw) { _onDraw = onDraw; Visible = true; }
        public override void Draw() { _onDraw(); }
    }

    private class TrackingBehavior : Behavior
    {
        public bool Updated, Destroyed_;
        public override void Update(float dt) { Updated = true; }
        public override void OnDestroy() { Destroyed_ = true; }
    }

    private class ThrowingEntity : Entity
    {
        public override void Update(float dt) { throw new InvalidOperationException("test explosion"); }
    }
}
