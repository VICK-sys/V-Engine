using System;
using System.Collections.Generic;

namespace VEngine.Engine.Core;

/// <summary>
/// Manages timed callbacks. Access via Eng.Timers.
/// </summary>
public class TimerManager
{
    private readonly List<TimerHandle> _timers = new();

    /// <summary>
    /// Call a function once after a delay (in seconds).
    /// Returns a handle that can be used to cancel or inspect the timer.
    /// </summary>
    public TimerHandle After(float delay, Action callback)
    {
        if (delay <= 0) throw new ArgumentException("Timer delay must be positive", nameof(delay));
        var handle = new TimerHandle(delay, callback, false);
        _timers.Add(handle);
        return handle;
    }

    /// <summary>
    /// Call a function repeatedly at a fixed interval (in seconds).
    /// Returns a handle that can be used to cancel or inspect the timer.
    /// </summary>
    public TimerHandle Every(float interval, Action callback)
    {
        if (interval <= 0) throw new ArgumentException("Timer interval must be positive", nameof(interval));
        var handle = new TimerHandle(interval, callback, true);
        _timers.Add(handle);
        return handle;
    }

    /// <summary>Cancel all active timers.</summary>
    public void CancelAll()
    {
        foreach (var t in _timers)
            t.Cancel();
        _timers.Clear();
    }

    /// <summary>Number of active timers.</summary>
    public int Count => _timers.Count;

    internal void Update(float dt)
    {
        for (int i = _timers.Count - 1; i >= 0; i--)
        {
            // Guard: list may have been cleared by CancelAll() in a callback
            if (i >= _timers.Count) continue;
            var t = _timers[i];
            if (!t.IsActive)
            {
                _timers.RemoveAt(i);
                continue;
            }

            if (t.IsPaused) continue;
            t.AddElapsed(dt);
            while (t.IsReady && t.IsActive)
            {
                try { t.Fire(); }
                catch (Exception ex) { Console.WriteLine($"[Timer] Callback error: {ex.Message}"); }
                if (i >= _timers.Count) break; // CancelAll() was called in callback
                if (t.IsRepeating && t.IsActive)
                    t.SubtractDuration();
                else
                {
                    t.Deactivate();
                    break;
                }
            }

            if (i < _timers.Count && !t.IsActive)
                _timers.RemoveAt(i);
        }
    }
}

/// <summary>
/// Handle to an active timer. Use Cancel() to stop it.
/// </summary>
public class TimerHandle
{
    private float _duration;
    private float _elapsed;
    private readonly Action _callback;
    private readonly bool _repeating;
    private bool _active = true;
    private bool _paused;

    /// <summary>Whether this timer is still active (not cancelled).</summary>
    public bool Active => _active;

    /// <summary>Whether this timer is paused.</summary>
    public bool Paused => _paused;

    /// <summary>Time remaining until next fire (seconds).</summary>
    public float Remaining => MathF.Max(0, _duration - _elapsed);

    /// <summary>Time elapsed since last fire (seconds).</summary>
    public float Elapsed => _elapsed;

    internal TimerHandle(float duration, Action callback, bool repeating)
    {
        _duration = duration;
        _callback = callback;
        _repeating = repeating;
    }

    /// <summary>Stop this timer permanently. Cannot be resumed.</summary>
    public void Cancel() => _active = false;

    /// <summary>Pause this timer. Elapsed time stops accumulating until Resume().</summary>
    public void Pause() => _paused = true;

    /// <summary>Resume a paused timer.</summary>
    public void Resume() => _paused = false;

    // Internal accessors for TimerManager — keeps fields private
    internal bool IsActive => _active;
    internal bool IsPaused => _paused;
    internal bool IsRepeating => _repeating;
    internal float Duration => _duration;
    internal void AddElapsed(float dt) => _elapsed += dt;
    internal bool IsReady => _elapsed >= _duration;
    internal void Fire() => _callback();
    internal void SubtractDuration() => _elapsed -= _duration;
    internal void Deactivate() => _active = false;
}
