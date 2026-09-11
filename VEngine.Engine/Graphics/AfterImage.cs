using System;
using VEngine.Engine.Core;
using VEngine.Engine.Math;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Entity that draws fading ghost copies of a target Sprite as it moves.
/// Add to the scene and call SetTarget(sprite). Ghosts capture the full
/// rendering state (texture, frame, origin, flip, scale) so they remain
/// correct even when the sprite switches animations or spritesheets.
/// Place on a layer below the target sprite so ghosts draw behind it.
/// </summary>
public class AfterImage : Entity
{
    /// <summary>Seconds between ghost snapshots. Lower = denser trail.</summary>
    public float Interval = 0.04f;

    /// <summary>How long each ghost lingers before fully fading.</summary>
    public float Duration = 0.25f;

    /// <summary>Maximum ghosts alive at once.</summary>
    public int MaxGhosts = 8;

    /// <summary>Starting alpha for the newest ghost (0-1).</summary>
    public float StartAlpha = 0.5f;

    /// <summary>Tint color for ghost copies.</summary>
    public Color Tint = new(160, 180, 255, 255);

    private Sprite? _target;
    private Ghost[] _ghosts;
    private int _count;
    private float _timer;

    public AfterImage(int maxGhosts = 8)
    {
        MaxGhosts = maxGhosts;
        _ghosts = new Ghost[maxGhosts];
    }

    /// <summary>Set the sprite to track. Pass null to stop.</summary>
    public void SetTarget(Sprite? sprite) => _target = sprite;

    /// <summary>The tracked sprite.</summary>
    public Sprite? Target => _target;

    /// <summary>Number of active ghosts.</summary>
    public int ActiveCount => _count;

    /// <summary>Clear all ghosts immediately.</summary>
    public void Clear() => _count = 0;

    public override void Update(float dt)
    {
        base.Update(dt);

        // Age and remove expired ghosts
        for (int i = _count - 1; i >= 0; i--)
        {
            _ghosts[i].Age += dt;
            if (_ghosts[i].Age >= _ghosts[i].Life)
            {
                _ghosts[i] = _ghosts[_count - 1];
                _count--;
            }
        }

        if (_target == null || !_target.Active || _target.Destroyed || !_target.Visible)
            return;

        // Capture new ghost at interval
        _timer += dt;
        if (_timer >= Interval)
        {
            _timer -= Interval;
            if (_timer > Interval) _timer = 0;

            // Resize pool if MaxGhosts changed
            if (MaxGhosts != _ghosts.Length)
            {
                if (_count > MaxGhosts) _count = MaxGhosts;
                Array.Resize(ref _ghosts, System.Math.Max(1, MaxGhosts));
            }

            if (_count < _ghosts.Length)
            {
                _ghosts[_count++] = new Ghost
                {
                    Snapshot = _target.CaptureGhost(),
                    Life = Duration,
                    Age = 0,
                };
            }
        }
    }

    public override void Draw()
    {
        if (_target == null || _count == 0) return;

        for (int i = 0; i < _count; i++)
        {
            ref var g = ref _ghosts[i];
            float t = g.Age / g.Life;
            float alpha = StartAlpha * (1f - t);
            if (alpha <= 0.01f) continue;

            _target.DrawGhost(in g.Snapshot, alpha, Tint);
        }
    }

    private struct Ghost
    {
        public Sprite.GhostSnapshot Snapshot;
        public float Life, Age;
    }
}
