using System;

namespace VEngine.Engine.Core;

/// <summary>
/// Screen flash and hit freeze effects. Access via Eng.Effects.
/// </summary>
public class Effects
{
    // Flash state
    private byte _flashR, _flashG, _flashB;
    private float _flashStartAlpha;
    private float _flashDuration;
    private float _flashTimer;

    // Freeze state
    private float _freezeTimer;
    private float _preFreezeScale;

    /// <summary>
    /// Global time scale. Affects scene, timers, and tweens.
    /// 1 = normal, 0.5 = half speed, 0 = frozen. Does not affect input or transitions.
    /// </summary>
    public float TimeScale = 1f;

    /// <summary>
    /// Flash the screen with a color that fades out over duration.
    /// Runs in real time (not affected by TimeScale).
    /// </summary>
    public void Flash(byte r, byte g, byte b, float duration = 0.1f, byte alpha = 255)
    {
        _flashR = r;
        _flashG = g;
        _flashB = b;
        _flashStartAlpha = alpha;
        _flashDuration = duration;
        _flashTimer = duration;
    }

    /// <summary>
    /// Freeze the game for a duration (sets TimeScale to 0, restores when done).
    /// Runs in real time. Stacks with current TimeScale.
    /// </summary>
    public void Freeze(float duration)
    {
        if (duration <= 0) return;
        if (_freezeTimer <= 0)
            _preFreezeScale = TimeScale;
        _freezeTimer = MathF.Max(_freezeTimer, duration);
        TimeScale = 0f;
    }

    internal void Update(float realDt)
    {
        if (_flashTimer > 0)
            _flashTimer -= realDt;

        if (_freezeTimer > 0)
        {
            _freezeTimer -= realDt;
            if (_freezeTimer <= 0)
                TimeScale = _preFreezeScale;
        }
    }

    internal void Draw()
    {
        if (_flashTimer <= 0) return;

        float t = System.Math.Clamp(_flashTimer / _flashDuration, 0f, 1f);
        float a = t * _flashStartAlpha / 255f;

        Eng.GL.FillRect(0, 0, Eng.Width, Eng.Height,
            _flashR / 255f, _flashG / 255f, _flashB / 255f, a);
    }
}
