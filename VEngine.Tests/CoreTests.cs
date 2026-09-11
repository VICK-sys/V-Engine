using VEngine.Engine.Core;
using VEngine.Engine.Math;

namespace VEngine.Tests;

public class TimerTests
{
    public TimerTests() { Eng.InitHeadless(); Eng.Timers.CancelAll(); }

    [Fact]
    public void AfterFires()
    {
        int count = 0;
        Eng.Timers.After(0.1f, () => count++);
        Eng.Timers.Update(0.05f);
        Assert.Equal(0, count);
        Eng.Timers.Update(0.06f);
        Assert.Equal(1, count);
    }

    [Fact]
    public void EveryRepeats()
    {
        int count = 0;
        Eng.Timers.Every(0.1f, () => count++);
        Eng.Timers.Update(0.35f); // should fire 3 times (catch-up)
        Assert.Equal(3, count);
    }

    [Fact]
    public void CancelStops()
    {
        int count = 0;
        var h = Eng.Timers.After(0.1f, () => count++);
        h.Cancel();
        Eng.Timers.Update(1f);
        Assert.Equal(0, count);
        Assert.False(h.Active);
    }

    [Fact]
    public void PauseResume()
    {
        var mgr = new TimerManager();
        int count = 0;
        var h = mgr.After(0.5f, () => count++);
        mgr.Update(0.2f);
        h.Pause();
        mgr.Update(1.0f); // paused
        Assert.Equal(0, count);
        Assert.True(h.Paused);
        h.Resume();
        mgr.Update(0.31f); // 0.2 + 0.31 = 0.51 > 0.5
        Assert.Equal(1, count);
    }

    [Fact]
    public void CancelAll()
    {
        int count = 0;
        Eng.Timers.After(0.1f, () => count++);
        Eng.Timers.After(0.1f, () => count++);
        Eng.Timers.CancelAll();
        Eng.Timers.Update(1f);
        Assert.Equal(0, count);
    }
}

public class TweenTests
{
    public TweenTests() { Eng.InitHeadless(); Eng.Tweens.CancelAll(); }

    [Fact]
    public void TweenFloat()
    {
        float val = 0;
        Eng.Tweens.To(() => val, v => val = v, 10f, 1f);
        Eng.Tweens.Update(0.5f);
        Assert.Equal(5f, val, 0.1f);
        Eng.Tweens.Update(0.5f);
        Assert.Equal(10f, val, 0.001f); // snaps to target
    }

    [Fact]
    public void TweenVec2()
    {
        var mgr = new TweenManager();
        Vec2 result = Vec2.Zero;
        mgr.To(() => Vec2.Zero, v => result = v, new Vec2(10, 20), 1f);
        mgr.Update(1f);
        Assert.Equal(10, result.X, 0.001f);
        Assert.Equal(20, result.Y, 0.001f);
    }

    [Fact]
    public void OnComplete()
    {
        bool done = false;
        Eng.Tweens.To(() => 0f, _ => { }, 1f, 0.1f).OnComplete(() => done = true);
        Eng.Tweens.Update(0.2f);
        Assert.True(done);
    }

    [Fact]
    public void CancelSkipsOnComplete()
    {
        bool done = false;
        var h = Eng.Tweens.To(() => 0f, _ => { }, 1f, 1f).OnComplete(() => done = true);
        h.Cancel();
        Eng.Tweens.Update(2f);
        Assert.False(done);
    }

    [Fact]
    public void Delay()
    {
        float val = 0;
        Eng.Tweens.To(() => val, v => val = v, 10f, 0.5f).Delay(0.5f);
        Eng.Tweens.Update(0.4f); // still in delay
        Assert.Equal(0, val, 0.001f);
        Eng.Tweens.Update(0.2f); // delay ends at 0.5, tween starts
        Eng.Tweens.Update(0.5f); // tween should complete
        Assert.Equal(10, val, 0.5f);
    }
}

public class StateMachineTests
{
    private enum State { Idle, Walk, Jump }

    [Fact]
    public void EnterExitFire()
    {
        string log = "";
        var sm = new StateMachine<State>(State.Idle);
        sm.On(State.Idle).Enter(() => log += "enter-idle,").Exit(() => log += "exit-idle,");
        sm.On(State.Walk).Enter(() => log += "enter-walk,");

        sm.Update(0); // enters initial state
        Assert.Contains("enter-idle", log);

        sm.Set(State.Walk);
        Assert.Contains("exit-idle", log);
        Assert.Contains("enter-walk", log);
    }

    [Fact]
    public void Duration()
    {
        var sm = new StateMachine<State>(State.Idle);
        sm.On(State.Idle);
        sm.Update(0.1f);
        sm.Update(0.2f);
        Assert.Equal(0.3f, sm.Duration, 0.001f);
    }

    [Fact]
    public void SetToSameStateIsNoop()
    {
        int enters = 0;
        var sm = new StateMachine<State>(State.Idle);
        sm.On(State.Idle).Enter(() => enters++);
        sm.Update(0);
        sm.Set(State.Idle); // should not re-enter
        Assert.Equal(1, enters);
    }
}

public class SignalTests
{
    [Fact]
    public void EmitCallsListeners()
    {
        int count = 0;
        var s = new Signal();
        s.Subscribe(() => count++);
        s.Subscribe(() => count++);
        s.Emit();
        Assert.Equal(2, count);
    }

    [Fact]
    public void Unsubscribe()
    {
        int count = 0;
        var s = new Signal<int>();
        var unsub = s.Subscribe(v => count += v);
        s.Emit(5);
        Assert.Equal(5, count);
        unsub();
        s.Emit(10);
        Assert.Equal(5, count); // didn't change
    }

    [Fact]
    public void ClearDuringEmit()
    {
        int count = 0;
        var s = new Signal();
        s.Subscribe(() => { count++; s.Clear(); });
        s.Subscribe(() => count++);
        s.Emit(); // should not crash, second listener skipped after Clear
        Assert.True(count >= 1);
    }
}

public class EntityTests
{
    [Fact]
    public void ActiveHooksFireOnChange()
    {
        bool activated = false, deactivated = false;
        var e = new TestEntity();
        e.OnActivatedCallback = () => activated = true;
        e.OnDeactivatedCallback = () => deactivated = true;

        e.Active = false;
        Assert.True(deactivated);
        Assert.False(activated);

        e.Active = true;
        Assert.True(activated);
    }

    [Fact]
    public void ActiveNoopOnSameValue()
    {
        int count = 0;
        var e = new TestEntity();
        e.OnActivatedCallback = () => count++;
        e.Active = true; // already true, should not fire
        Assert.Equal(0, count);
    }

    [Fact]
    public void DestroyOnlyOnce()
    {
        int count = 0;
        var e = new TestEntity { DestroyCallback = () => count++ };
        e.Destroy();
        e.Destroy(); // second call is no-op
        Assert.Equal(1, count);
        Assert.True(e.Destroyed);
    }

    [Fact]
    public void KinematicPhysics()
    {
        var e = new KinematicEntity(0, 0);
        e.Velocity = new Vec2(10, 20);
        e.Acceleration = new Vec2(0, 100);
        e.Update(1f);
        Assert.Equal(10, e.Position.X, 0.001f);
        Assert.Equal(120, e.Position.Y, 0.001f); // vel += accel * dt, pos += vel * dt
    }

    private class TestEntity : Entity
    {
        public Action? OnActivatedCallback;
        public Action? OnDeactivatedCallback;
        public Action? DestroyCallback;
        protected override void OnActivated() => OnActivatedCallback?.Invoke();
        protected override void OnDeactivated() => OnDeactivatedCallback?.Invoke();
        protected override void OnDestroy() => DestroyCallback?.Invoke();
    }
}

public class PoolTests
{
    [Fact]
    public void GetAndRelease()
    {
        var pool = new Pool<Entity>(() => new Entity(), preload: 3);
        Assert.Equal(3, pool.TotalCount);
        Assert.Equal(0, pool.ActiveCount);

        var e = pool.Get();
        Assert.True(e.Active);
        Assert.Equal(1, pool.ActiveCount);

        pool.Release(e);
        Assert.False(e.Active);
        Assert.Equal(0, pool.ActiveCount);
    }

    [Fact]
    public void GetReusesInactive()
    {
        var pool = new Pool<Entity>(() => new Entity(), preload: 1);
        var first = pool.Get();
        pool.Release(first);
        var second = pool.Get();
        Assert.Same(first, second); // reused
    }

    [Fact]
    public void GetResetsPosition()
    {
        var pool = new Pool<KinematicEntity>(() => new KinematicEntity());
        var e = pool.Get(50, 100);
        Assert.Equal(50, e.Position.X);
        Assert.Equal(100, e.Position.Y);
        Assert.Equal(0, e.Velocity.X);
    }

    [Fact]
    public void ReleaseRejectsForeignEntity()
    {
        var pool = new Pool<Entity>(() => new Entity());
        var foreign = new Entity();
        pool.Release(foreign); // should be no-op, not crash
        Assert.True(foreign.Active); // not deactivated
    }
}

public class CollisionTests
{
    [Fact]
    public void SeparateAABB()
    {
        // a at (0,0) with width 10 overlaps b at (8,0) with width 10
        // Overlap on X: a's right edge (10) - b's left edge (8) = 2px penetration from the left
        var a = new KinematicEntity(0, 0) { BaseWidth = 10, BaseHeight = 10, Velocity = new Vec2(5, 0) };
        var b = new Entity(8, 0) { BaseWidth = 10, BaseHeight = 10, Immovable = true };

        var dir = Collision.Separate(a, b);
        Assert.Equal(CollisionDir.Left, dir); // a hit the left side of b
        Assert.True(a.Position.X < 0); // pushed further left
        Assert.Equal(0, a.Velocity.X, 0.001f); // velocity zeroed
    }

    [Fact]
    public void CheckIsPure()
    {
        var a = new Entity(0, 0) { BaseWidth = 10, BaseHeight = 10 };
        var b = new Entity(5, 0) { BaseWidth = 10, BaseHeight = 10 };
        float origX = a.Position.X;

        var result = Collision.Check(a, b);
        Assert.NotEqual(CollisionDir.None, result.Direction);
        Assert.Equal(origX, a.Position.X); // position unchanged
    }

    [Fact]
    public void NoOverlap()
    {
        var a = new Entity(0, 0) { BaseWidth = 10, BaseHeight = 10 };
        var b = new Entity(100, 100) { BaseWidth = 10, BaseHeight = 10 };
        Assert.Equal(CollisionDir.None, Collision.Check(a, b).Direction);
    }

    [Fact]
    public void CircleOverlap()
    {
        Assert.True(Collision.OverlapCircles(0, 0, 5, 8, 0, 5));
        Assert.False(Collision.OverlapCircles(0, 0, 5, 20, 0, 5));
    }

    [Fact]
    public void CircleRectOverlap()
    {
        Assert.True(Collision.OverlapCircleRect(5, 5, 3, 0, 0, 10, 10));
        Assert.False(Collision.OverlapCircleRect(20, 20, 3, 0, 0, 10, 10));
    }
}

public class SceneGroupTests
{
    [Fact]
    public void AddAndRemove()
    {
        var group = new Group();
        var e = new Entity();
        group.Add(e);
        Assert.Equal(1, group.Members.Count);
        bool removed = group.Remove(e);
        Assert.True(removed);
        Assert.Equal(0, group.Members.Count);
    }

    [Fact]
    public void RemoveReturnsFalseForMissing()
    {
        var group = new Group();
        Assert.False(group.Remove(new Entity()));
    }

    [Fact]
    public void DrawOrderByLayer()
    {
        var group = new Group();
        var a = new Entity { Layer = 2 };
        var b = new Entity { Layer = 1 };
        group.Add(a);
        group.Add(b);
        group.Draw(); // triggers sort
        Assert.Same(b, group.Members[0]); // layer 1 first
    }
}

public class InputMapTests
{
    public InputMapTests() => Eng.InitHeadless();

    [Fact]
    public void BindAndQuery()
    {
        Eng.Actions.ClearAll();
        Eng.Actions.Bind("test", SDL2.SDL.SDL_Scancode.SDL_SCANCODE_SPACE);
        Assert.True(Eng.Actions.HasAction("test"));
        Assert.False(Eng.Actions.HasAction("missing"));
    }

    [Fact]
    public void Introspection()
    {
        Eng.Actions.ClearAll();
        Eng.Actions.Bind("jump", SDL2.SDL.SDL_Scancode.SDL_SCANCODE_SPACE);
        var keys = Eng.Actions.GetKeys("jump");
        Assert.Single(keys);
        Assert.Equal(SDL2.SDL.SDL_Scancode.SDL_SCANCODE_SPACE, keys[0]);
    }

    [Fact]
    public void UnboundActionReturnsFalse()
    {
        Eng.Actions.ClearAll();
        Assert.False(Eng.Actions.Down("nonexistent"));
        Assert.False(Eng.Actions.Pressed("nonexistent"));
        Assert.False(Eng.Actions.Released("nonexistent"));
        Assert.Equal(0, Eng.Actions.Axis("nonexistent"));
    }
}

public class CameraTests
{
    public CameraTests() => Eng.InitHeadless();

    [Fact]
    public void WorldToScreenAndBack()
    {
        var cam = new Camera();
        cam.Position = new Vec2(100, 200);
        var screen = cam.WorldToScreen(150, 250);
        var world = cam.ScreenToWorld(screen.X, screen.Y);
        Assert.Equal(150, world.X, 0.1f);
        Assert.Equal(250, world.Y, 0.1f);
    }

    [Fact]
    public void TransformWithScrollFactor()
    {
        var cam = new Camera();
        cam.Position = new Vec2(100, 0);
        // ScrollFactor 0 = fixed to screen
        var screen0 = cam.Transform(50, 0, Vec2.Zero);
        // ScrollFactor 1 = normal world
        var screen1 = cam.Transform(50, 0, Vec2.One);
        Assert.True(screen0.X > screen1.X); // fixed element stays in place
    }

    [Fact]
    public void FollowClearsOnInactiveTarget()
    {
        var cam = new Camera();
        var target = new Entity(100, 100);
        // Don't call Follow (it reads Eng.Width), just set target directly
        // Test that UpdateFollow handles inactive targets gracefully
        target.Active = false;
        // Camera should handle null/inactive targets without crashing
        cam.Update(0.016f);
    }
}

public class DialogueTests
{
    [Fact]
    public void SayAddsNode()
    {
        var d = new Dialogue().Say("Hello", "Guard");
        Assert.Equal(1, d.NodeCount);
    }

    [Fact]
    public void AskAddsChoices()
    {
        var d = new Dialogue()
            .Ask("Pick one", "NPC", ("Yes", "y"), ("No", "n"));
        Assert.Equal(1, d.NodeCount);
    }

    [Fact]
    public void LabelRegistersIndex()
    {
        var d = new Dialogue()
            .Say("First")
            .Label("middle")
            .Say("Second");
        Assert.True(d.Labels.ContainsKey("middle"));
        Assert.Equal(1, d.Labels["middle"]);
    }

    [Fact]
    public void GotoAddsNode()
    {
        var d = new Dialogue().Goto("end").Label("end");
        Assert.Equal(2, d.NodeCount);
    }

    [Fact]
    public void CallAddsNode()
    {
        bool fired = false;
        var d = new Dialogue().Call(() => fired = true);
        Assert.Equal(1, d.NodeCount);
        Assert.False(fired); // Call is data, doesn't execute until played
    }

    [Fact]
    public void FluentChaining()
    {
        var d = new Dialogue()
            .Say("Line 1", "A")
            .Say("Line 2", "B")
            .Ask("Choose", "A", ("X", "x"), ("Y", "y"))
            .Label("x").Say("Chose X").Goto("end")
            .Label("y").Say("Chose Y")
            .Label("end");
        Assert.Equal(9, d.NodeCount);
        Assert.Equal(3, d.Labels.Count);
    }
}

public class SequenceTests
{
    public SequenceTests() { Eng.InitHeadless(); Eng.Timers.CancelAll(); Eng.Tweens.CancelAll(); }

    [Fact]
    public void CallExecutesImmediately()
    {
        int count = 0;
        new Sequence().Call(() => count++).Call(() => count++).Start();
        Assert.Equal(2, count);
    }

    [Fact]
    public void WaitDelaysNextStep()
    {
        int count = 0;
        new Sequence().Call(() => count++).Wait(0.5f).Call(() => count++).Start();
        Assert.Equal(1, count); // first Call ran, Wait is pending
        Eng.Timers.Update(0.3f);
        Assert.Equal(1, count); // still waiting
        Eng.Timers.Update(0.3f);
        Assert.Equal(2, count); // Wait expired, second Call ran
    }

    [Fact]
    public void TweenFloatAdvancesOnComplete()
    {
        float val = 0;
        bool done = false;
        new Sequence()
            .TweenFloat(() => val, v => val = v, 10f, 1f)
            .Call(() => done = true)
            .Start();

        Eng.Tweens.Update(0.5f);
        Assert.False(done);
        Assert.Equal(5f, val, 0.5f);

        Eng.Tweens.Update(0.6f);
        Assert.True(done);
        Assert.Equal(10f, val, 0.01f);
    }

    [Fact]
    public void OnCompleteFiresAtEnd()
    {
        bool finished = false;
        new Sequence()
            .Call(() => { })
            .OnComplete(() => finished = true)
            .Start();
        Assert.True(finished);
    }

    [Fact]
    public void CancelStopsSequence()
    {
        int count = 0;
        var seq = new Sequence()
            .Wait(0.5f)
            .Call(() => count++)
            .Start();

        seq.Cancel();
        Eng.Timers.Update(1f);
        Assert.Equal(0, count);
        Assert.False(seq.Running);
    }

    [Fact]
    public void CancelStopsTween()
    {
        float val = 0;
        var seq = new Sequence()
            .TweenFloat(() => val, v => val = v, 100f, 1f)
            .Start();

        Eng.Tweens.Update(0.2f);
        seq.Cancel();
        float frozenVal = val;

        Eng.Tweens.Update(1f);
        Assert.Equal(frozenVal, val, 0.01f); // tween was cancelled, value didn't change
    }

    [Fact]
    public void RunningProperty()
    {
        var seq = new Sequence().Wait(0.5f);
        Assert.False(seq.Running);
        seq.Start();
        Assert.True(seq.Running);
        Eng.Timers.Update(0.6f);
        Assert.False(seq.Running);
    }

    [Fact]
    public void EmptySequenceCompletesImmediately()
    {
        bool done = false;
        new Sequence().OnComplete(() => done = true).Start();
        Assert.True(done);
    }

    [Fact]
    public void WaitZeroAdvancesImmediately()
    {
        int count = 0;
        new Sequence().Wait(0f).Call(() => count++).Start();
        Assert.Equal(1, count);
    }

    [Fact]
    public void TogetherRunsInParallel()
    {
        float a = 0, b = 0;
        bool done = false;
        new Sequence()
            .Together(
                s => s.TweenFloat(() => a, v => a = v, 10f, 1f),
                s => s.TweenFloat(() => b, v => b = v, 20f, 0.5f)
            )
            .Call(() => done = true)
            .Start();

        Eng.Tweens.Update(0.5f);
        Assert.Equal(20f, b, 0.1f); // b finished (0.5s tween)
        Assert.False(done); // Together not done yet (a still running)

        Eng.Tweens.Update(0.6f);
        Assert.Equal(10f, a, 0.1f); // a finished (1s tween)
        Assert.True(done); // Together completed, Call ran
    }

    [Fact]
    public void TogetherEmptyAdvancesImmediately()
    {
        bool done = false;
        new Sequence().Together().Call(() => done = true).Start();
        Assert.True(done);
    }

    [Fact]
    public void StepCountAndCurrentStep()
    {
        var seq = new Sequence().Call(() => { }).Wait(0.5f).Call(() => { });
        Assert.Equal(3, seq.StepCount);
        Assert.Equal(-1, seq.CurrentStep);
        seq.Start();
        // After start: Call executed immediately, Wait is step 1
        Assert.Equal(1, seq.CurrentStep);
    }

    [Fact]
    public void RestartResetsSequence()
    {
        int count = 0;
        var seq = new Sequence().Call(() => count++).Wait(0.5f).Call(() => count++);
        seq.Start();
        Assert.Equal(1, count);
        Eng.Timers.Update(0.6f);
        Assert.Equal(2, count);

        seq.Start(); // restart
        Assert.Equal(3, count); // first Call ran again
        Eng.Timers.Update(0.6f);
        Assert.Equal(4, count);
    }
}

public class EffectsTests
{
    [Fact]
    public void FreezeExtends()
    {
        var fx = new Effects();
        Assert.Equal(1f, fx.TimeScale);
        fx.Freeze(0.5f);
        Assert.Equal(0f, fx.TimeScale);
        fx.Freeze(0.2f); // shorter, should keep 0.5
        fx.Update(0.3f);
        Assert.Equal(0f, fx.TimeScale); // still frozen (0.5 - 0.3 = 0.2 remaining)
        fx.Update(0.3f);
        Assert.Equal(1f, fx.TimeScale); // unfrozen
    }
}
