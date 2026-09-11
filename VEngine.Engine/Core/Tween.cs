using System;
using System.Collections.Generic;
using VEngine.Engine.Math;

namespace VEngine.Engine.Core;

/// <summary>
/// Standard easing curves. See easings.net for visual reference.
/// </summary>
public enum Ease
{
    Linear,
    InQuad, OutQuad, InOutQuad,
    InCubic, OutCubic, InOutCubic,
    InQuart, OutQuart, InOutQuart,
    InQuint, OutQuint, InOutQuint,
    InSine, OutSine, InOutSine,
    InExpo, OutExpo, InOutExpo,
    InCirc, OutCirc, InOutCirc,
    InBack, OutBack, InOutBack,
    InBounce, OutBounce, InOutBounce,
    InElastic, OutElastic, InOutElastic,
}

/// <summary>
/// Easing functions. Maps a linear 0-1 progress to a curved 0-1 value.
/// </summary>
public static class Easing
{
    private const float HalfPI = MathF.PI / 2f;
    private const float C1 = 1.70158f;
    private const float C2 = C1 * 1.525f;
    private const float C3 = C1 + 1f;
    private const float C4 = 2f * MathF.PI / 3f;
    private const float C5 = 2f * MathF.PI / 4.5f;

    public static float Apply(Ease ease, float t) => ease switch
    {
        Ease.Linear => t,

        Ease.InQuad => t * t,
        Ease.OutQuad => 1f - (1f - t) * (1f - t),
        Ease.InOutQuad => t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f,

        Ease.InCubic => t * t * t,
        Ease.OutCubic => 1f - MathF.Pow(1f - t, 3f),
        Ease.InOutCubic => t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f,

        Ease.InQuart => t * t * t * t,
        Ease.OutQuart => 1f - MathF.Pow(1f - t, 4f),
        Ease.InOutQuart => t < 0.5f ? 8f * t * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 4f) / 2f,

        Ease.InQuint => t * t * t * t * t,
        Ease.OutQuint => 1f - MathF.Pow(1f - t, 5f),
        Ease.InOutQuint => t < 0.5f ? 16f * t * t * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 5f) / 2f,

        Ease.InSine => 1f - MathF.Cos(t * HalfPI),
        Ease.OutSine => MathF.Sin(t * HalfPI),
        Ease.InOutSine => -(MathF.Cos(MathF.PI * t) - 1f) / 2f,

        Ease.InExpo => t == 0f ? 0f : MathF.Pow(2f, 10f * t - 10f),
        Ease.OutExpo => t == 1f ? 1f : 1f - MathF.Pow(2f, -10f * t),
        Ease.InOutExpo => t == 0f ? 0f : t == 1f ? 1f : t < 0.5f
            ? MathF.Pow(2f, 20f * t - 10f) / 2f
            : (2f - MathF.Pow(2f, -20f * t + 10f)) / 2f,

        Ease.InCirc => 1f - MathF.Sqrt(1f - t * t),
        Ease.OutCirc => MathF.Sqrt(1f - (t - 1f) * (t - 1f)),
        Ease.InOutCirc => t < 0.5f
            ? (1f - MathF.Sqrt(1f - MathF.Pow(2f * t, 2f))) / 2f
            : (MathF.Sqrt(1f - MathF.Pow(-2f * t + 2f, 2f)) + 1f) / 2f,

        Ease.InBack => C3 * t * t * t - C1 * t * t,
        Ease.OutBack => 1f + C3 * MathF.Pow(t - 1f, 3f) + C1 * MathF.Pow(t - 1f, 2f),
        Ease.InOutBack => t < 0.5f
            ? MathF.Pow(2f * t, 2f) * ((C2 + 1f) * 2f * t - C2) / 2f
            : (MathF.Pow(2f * t - 2f, 2f) * ((C2 + 1f) * (2f * t - 2f) + C2) + 2f) / 2f,

        Ease.InBounce => 1f - BounceOut(1f - t),
        Ease.OutBounce => BounceOut(t),
        Ease.InOutBounce => t < 0.5f
            ? (1f - BounceOut(1f - 2f * t)) / 2f
            : (1f + BounceOut(2f * t - 1f)) / 2f,

        Ease.InElastic => t == 0f ? 0f : t == 1f ? 1f
            : -MathF.Pow(2f, 10f * t - 10f) * MathF.Sin((10f * t - 10.75f) * C4),
        Ease.OutElastic => t == 0f ? 0f : t == 1f ? 1f
            : MathF.Pow(2f, -10f * t) * MathF.Sin((10f * t - 0.75f) * C4) + 1f,
        Ease.InOutElastic => t == 0f ? 0f : t == 1f ? 1f : t < 0.5f
            ? -(MathF.Pow(2f, 20f * t - 10f) * MathF.Sin((20f * t - 11.125f) * C5)) / 2f
            : MathF.Pow(2f, -20f * t + 10f) * MathF.Sin((20f * t - 11.125f) * C5) / 2f + 1f,

        _ => throw new ArgumentException($"Unknown ease type: {ease}", nameof(ease)),
    };

    private static float BounceOut(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;
        if (t < 1f / d1) return n1 * t * t;
        if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
        if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
        t -= 2.625f / d1;
        return n1 * t * t + 0.984375f;
    }
}

/// <summary>
/// Manages active tweens. Access via Eng.Tweens.
/// </summary>
public class TweenManager
{
    private readonly List<TweenHandle> _tweens = new();

    /// <summary>
    /// Tween a float value from its current value to a target.
    /// The getter reads the current value at creation time to capture the start.
    /// The setter is called each frame with the interpolated value.
    /// </summary>
    public TweenHandle To(Func<float> getter, Action<float> setter, float to, float duration, Ease ease = Ease.Linear)
    {
        if (duration <= 0) throw new ArgumentException("Tween duration must be positive", nameof(duration));
        float from = getter();
        var handle = new TweenHandle(duration, ease, t => setter(from + (to - from) * t));
        _tweens.Add(handle);
        return handle;
    }

    /// <summary>
    /// Tween a Vec2 value from its current value to a target.
    /// </summary>
    public TweenHandle To(Func<Vec2> getter, Action<Vec2> setter, Vec2 to, float duration, Ease ease = Ease.Linear)
    {
        if (duration <= 0) throw new ArgumentException("Tween duration must be positive", nameof(duration));
        var from = getter();
        var handle = new TweenHandle(duration, ease, t => setter(Vec2.Lerp(from, to, t)));
        _tweens.Add(handle);
        return handle;
    }

    /// <summary>
    /// Tween a Color value from its current value to a target.
    /// </summary>
    public TweenHandle To(Func<Math.Color> getter, Action<Math.Color> setter, Math.Color to, float duration, Ease ease = Ease.Linear)
    {
        if (duration <= 0) throw new ArgumentException("Tween duration must be positive", nameof(duration));
        var from = getter();
        var handle = new TweenHandle(duration, ease, t => setter(Math.Color.Lerp(from, to, t)));
        _tweens.Add(handle);
        return handle;
    }

    /// <summary>
    /// Tween a float value with a custom easing function (maps 0-1 to 0-1).
    /// </summary>
    public TweenHandle To(Func<float> getter, Action<float> setter, float to, float duration, Func<float, float> easingFunc)
    {
        if (duration <= 0) throw new ArgumentException("Tween duration must be positive", nameof(duration));
        float from = getter();
        var handle = new TweenHandle(duration, Ease.Linear, t => setter(from + (to - from) * t), easingFunc);
        _tweens.Add(handle);
        return handle;
    }

    /// <summary>Cancel all active tweens.</summary>
    public void CancelAll()
    {
        foreach (var t in _tweens)
            t.Cancel();
        _tweens.Clear();
    }

    /// <summary>Number of active tweens.</summary>
    public int Count => _tweens.Count;

    internal void Update(float dt)
    {
        for (int i = _tweens.Count - 1; i >= 0; i--)
        {
            if (i >= _tweens.Count) continue; // Guard: CancelAll() may have cleared list
            var t = _tweens[i];
            if (!t.IsActive)
            {
                _tweens.RemoveAt(i);
                continue;
            }

            if (t.HasDelay) { t.TickDelay(dt); continue; }

            t.AddElapsed(dt);
            bool completed = t.IsComplete;
            float progress = completed ? 1f : t.Progress;
            if (completed) t.Deactivate();

            try { t.Apply(progress); }
            catch (Exception ex) { Console.WriteLine($"[Tween] Apply error: {ex.Message}"); }

            if (i >= _tweens.Count) continue; // CancelAll() in callback
            if (!t.IsActive)
            {
                if (completed)
                {
                    try { t.Complete(); }
                    catch (Exception ex) { Console.WriteLine($"[Tween] OnComplete error: {ex.Message}"); }
                }
                if (i < _tweens.Count) _tweens.RemoveAt(i);
            }
        }
    }
}

/// <summary>
/// Handle to an active tween. Use Cancel() to stop it, OnComplete() to chain.
/// </summary>
public class TweenHandle
{
    private readonly Action<float> _apply;
    private readonly float _duration;
    private readonly Ease _ease;
    private readonly Func<float, float>? _customEase;
    private float _delay;
    private float _elapsed;
    private bool _active = true;
    private Action? _onComplete;

    /// <summary>Whether this tween is still active.</summary>
    public bool Active => _active;

    /// <summary>Progress from 0 (start) to 1 (end).</summary>
    public float Progress => System.Math.Clamp(_elapsed / _duration, 0f, 1f);

    /// <summary>Seconds elapsed.</summary>
    public float Elapsed => _elapsed;

    /// <summary>Total duration in seconds.</summary>
    public float Duration => _duration;

    internal TweenHandle(float duration, Ease ease, Action<float> apply, Func<float, float>? customEase = null)
    {
        _duration = duration;
        _ease = ease;
        _customEase = customEase;
        _apply = apply;
    }

    /// <summary>Stop this tween. OnComplete will NOT fire.</summary>
    public void Cancel() => _active = false;

    /// <summary>
    /// Add a delay before the tween starts. Returns this handle for chaining.
    /// Usage: Eng.Tweens.To(...).Delay(0.5f).OnComplete(...)
    /// </summary>
    public TweenHandle Delay(float seconds)
    {
        _delay = seconds;
        return this;
    }

    /// <summary>
    /// Set a callback to run when the tween finishes naturally.
    /// Does not fire on Cancel(). Returns this handle for chaining.
    /// </summary>
    public TweenHandle OnComplete(Action callback)
    {
        _onComplete = callback;
        return this;
    }

    // Internal accessors for TweenManager
    internal bool IsActive => _active;
    internal bool HasDelay => _delay > 0;
    internal void TickDelay(float dt) => _delay -= dt;
    internal void AddElapsed(float dt) => _elapsed += dt;
    internal bool IsComplete => _elapsed >= _duration;
    internal void Apply(float progress)
    {
        float eased = _customEase != null ? _customEase(progress) : Easing.Apply(_ease, progress);
        _apply(eased);
    }
    internal void Complete() { _onComplete?.Invoke(); }
    internal void Deactivate() => _active = false;
}
