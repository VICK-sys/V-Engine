using System;
using System.Collections.Generic;
using VEngine.Engine.Math;

namespace VEngine.Engine.Core;

/// <summary>
/// Scripted sequence of timed actions for cutscenes and scripted events.
/// Composes Tweens and Timers into a declarative, chainable API.
///
/// Usage:
///   new Sequence()
///       .Call(() => player.Active = false)
///       .TweenFloat(() => Eng.Camera.Position.X, v => Eng.Camera.Position.X = v, 500, 1f, Ease.InOutQuad)
///       .Wait(0.5f)
///       .Call(() => Eng.Effects.Flash(255, 255, 255, 0.3f))
///       .Wait(0.3f)
///       .Call(() => player.Active = true)
///       .Start();
/// </summary>
public class Sequence
{
    // Each step is a closure that executes and calls onComplete when done
    private readonly List<Action<Action>> _steps = new();
    private int _current;
    private bool _running;
    private Action? _onComplete;

    // Active handles for cancellation
    private readonly List<TimerHandle> _activeTimers = new();
    private readonly List<TweenHandle> _activeTweens = new();
    private readonly List<Sequence> _activeSubs = new();

    // ── Fluent Step API ───────────────────────────────────────

    /// <summary>Execute a callback immediately, then advance.</summary>
    public Sequence Call(Action callback)
    {
        _steps.Add(done => { callback(); done(); });
        return this;
    }

    /// <summary>Wait for a duration (seconds) before advancing.</summary>
    public Sequence Wait(float seconds)
    {
        _steps.Add(done =>
        {
            if (seconds <= 0) { done(); return; }
            _activeTimers.Add(Eng.Timers.After(seconds, done));
        });
        return this;
    }

    /// <summary>Tween a float value, advance when complete.</summary>
    public Sequence TweenFloat(Func<float> getter, Action<float> setter,
        float target, float duration, Ease ease = Ease.Linear)
    {
        _steps.Add(done =>
            _activeTweens.Add(Eng.Tweens.To(getter, setter, target, duration, ease).OnComplete(done)));
        return this;
    }

    /// <summary>Tween a Vec2 value, advance when complete.</summary>
    public Sequence TweenVec2(Func<Vec2> getter, Action<Vec2> setter,
        Vec2 target, float duration, Ease ease = Ease.Linear)
    {
        _steps.Add(done =>
            _activeTweens.Add(Eng.Tweens.To(getter, setter, target, duration, ease).OnComplete(done)));
        return this;
    }

    /// <summary>Tween a Color value, advance when complete.</summary>
    public Sequence TweenColor(Func<Color> getter, Action<Color> setter,
        Color target, float duration, Ease ease = Ease.Linear)
    {
        _steps.Add(done =>
            _activeTweens.Add(Eng.Tweens.To(getter, setter, target, duration, ease).OnComplete(done)));
        return this;
    }

    /// <summary>
    /// Run multiple sub-sequences in parallel. Advances when ALL finish.
    /// Each builder receives a fresh Sequence to populate with steps.
    /// </summary>
    public Sequence Together(params Action<Sequence>[] builders)
    {
        _steps.Add(done =>
        {
            if (builders.Length == 0) { done(); return; }
            int remaining = builders.Length;
            foreach (var build in builders)
            {
                var sub = new Sequence();
                build(sub);
                sub._onComplete = () => { if (--remaining == 0) done(); };
                _activeSubs.Add(sub);
                sub.Start();
            }
        });
        return this;
    }

    /// <summary>Set a callback to fire when the entire sequence completes.</summary>
    public Sequence OnComplete(Action callback)
    {
        _onComplete = callback;
        return this;
    }

    // ── Control ───────────────────────────────────────────────

    /// <summary>Whether the sequence is currently running.</summary>
    public bool Running => _running;

    /// <summary>Current step index (0-based). -1 if not started.</summary>
    public int CurrentStep => _running ? _current - 1 : -1;

    /// <summary>Total number of steps.</summary>
    public int StepCount => _steps.Count;

    /// <summary>Start (or restart) the sequence from the beginning.</summary>
    public Sequence Start()
    {
        CancelActive();
        _current = 0;
        _running = true;
        Advance();
        return this;
    }

    /// <summary>Cancel the sequence. Active tweens/timers are stopped. OnComplete does not fire.</summary>
    public void Cancel()
    {
        CancelActive();
        _running = false;
    }

    // ── Internal ──────────────────────────────────────────────

    private void Advance()
    {
        // Clean up handles from previous step
        _activeTimers.Clear();
        _activeTweens.Clear();
        _activeSubs.Clear();

        if (!_running || _current >= _steps.Count)
        {
            _running = false;
            _onComplete?.Invoke();
            return;
        }

        _steps[_current++](() => Advance());
    }

    private void CancelActive()
    {
        foreach (var t in _activeTimers) t.Cancel();
        foreach (var tw in _activeTweens) tw.Cancel();
        foreach (var sub in _activeSubs) sub.Cancel();
        _activeTimers.Clear();
        _activeTweens.Clear();
        _activeSubs.Clear();
    }
}
