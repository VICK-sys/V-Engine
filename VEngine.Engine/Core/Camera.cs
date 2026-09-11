using System;
using VEngine.Engine.Math;

namespace VEngine.Engine.Core;

public class Camera
{
    /// <summary>Camera position in world space (top-left of the viewport).</summary>
    public Vec2 Position;

    /// <summary>Zoom level. 1 = normal, 2 = 2x zoom in, 0.5 = zoomed out. Minimum 0.01.</summary>
    private float _zoom = 1f;
    public float Zoom
    {
        get => _zoom;
        set => _zoom = System.Math.Max(0.01f, value);
    }

    /// <summary>Camera rotation in degrees (clockwise). Applied to the projection matrix.</summary>
    public float Rotation;

    /// <summary>Entity the camera follows.</summary>
    private Entity? _target;

    /// <summary>Offset from the follow target (useful for centering).</summary>
    public Vec2 FollowOffset;

    /// <summary>
    /// How quickly the camera catches up to the target per frame.
    /// 1 = instant snap, 0.1 = smooth follow (default), lower = more lag.
    /// </summary>
    public float FollowLerp = 0.1f;

    // Shake state
    private float _shakeDuration;
    private float _shakeIntensity;
    private float _shakeTimer;
    private Vec2 _shakeOffset;
    private readonly Random _shakeRng = new(Environment.TickCount);

    // World bounds (optional) -- camera won't scroll past these
    private bool _hasBounds;
    private float _boundsMinX, _boundsMinY, _boundsMaxX, _boundsMaxY;

    /// <summary>Screen width in pixels.</summary>
    public int ScreenWidth => Eng.Width;
    /// <summary>Screen height in pixels.</summary>
    public int ScreenHeight => Eng.Height;

    /// <summary>
    /// Follow an entity. The camera will smoothly track it.
    /// </summary>
    public void Follow(Entity target, float lerp = 0.1f)
    {
        _target = target;
        FollowLerp = lerp;
        // Default offset: center the target on screen
        FollowOffset = new Vec2(ScreenWidth / 2f, ScreenHeight / 2f);
    }

    /// <summary>
    /// Stop following. Camera stays at current position.
    /// </summary>
    public void Unfollow()
    {
        _target = null;
    }

    /// <summary>
    /// Set world bounds the camera cannot scroll past.
    /// </summary>
    public void SetBounds(float minX, float minY, float maxX, float maxY)
    {
        _hasBounds = true;
        _boundsMinX = minX;
        _boundsMinY = minY;
        _boundsMaxX = maxX;
        _boundsMaxY = maxY;
    }

    /// <summary>
    /// Remove world bounds.
    /// </summary>
    public void ClearBounds()
    {
        _hasBounds = false;
    }

    /// <summary>
    /// Snap the camera to center on a position immediately (no lerp).
    /// </summary>
    public void FocusOn(float x, float y)
    {
        Position.X = x - ScreenWidth / (2f * Zoom);
        Position.Y = y - ScreenHeight / (2f * Zoom);
        ClampBounds();
    }

    /// <summary>
    /// Trigger a screen shake.
    /// </summary>
    public void Shake(float intensity = 5f, float duration = 0.3f)
    {
        if (duration <= 0) return;
        float currentIntensity = _shakeTimer > 0 ? _shakeIntensity * (_shakeTimer / _shakeDuration) : 0;
        _shakeIntensity = MathF.Max(currentIntensity, intensity);
        _shakeDuration = duration;
        _shakeTimer = MathF.Max(_shakeTimer, duration);
    }

    internal void Update(float dt)
    {
        UpdateFollow(dt);
        ClampBounds();
        UpdateShake(dt);
    }

    private void UpdateFollow(float dt)
    {
        if (_target == null || !_target.Active || _target.Destroyed) { _target = null; return; }
        float targetX = _target.Position.X + _target.Width / 2f - FollowOffset.X / Zoom;
        float targetY = _target.Position.Y + _target.Height / 2f - FollowOffset.Y / Zoom;
        // Framerate-independent exponential decay: same visual result regardless of TargetFps
        float t = 1f - MathF.Pow(1f - FollowLerp, dt * 60f);
        Position.X += (targetX - Position.X) * t;
        Position.Y += (targetY - Position.Y) * t;
    }

    private void UpdateShake(float dt)
    {
        if (_shakeTimer <= 0)
        {
            _shakeOffset = Vec2.Zero;
            return;
        }
        _shakeTimer -= dt;
        float intensity = _shakeIntensity * (_shakeTimer / _shakeDuration);
        _shakeOffset.X = ((float)_shakeRng.NextDouble() * 2 - 1) * intensity;
        _shakeOffset.Y = ((float)_shakeRng.NextDouble() * 2 - 1) * intensity;
    }

    /// <summary>
    /// Apply scroll factor and convert world position to screen position in one step.
    /// Handles parallax: scrollFactor (1,1) = normal, (0,0) = fixed to screen (HUD).
    /// </summary>
    public Vec2 Transform(float worldX, float worldY, Vec2 scrollFactor)
    {
        float adjustedX = worldX - Position.X * scrollFactor.X + Position.X;
        float adjustedY = worldY - Position.Y * scrollFactor.Y + Position.Y;
        return WorldToScreen(adjustedX, adjustedY);
    }

    /// <summary>
    /// Convert a world position to screen position.
    /// Used by rendering to offset sprites by camera.
    /// </summary>
    public Vec2 WorldToScreen(float worldX, float worldY)
    {
        return new Vec2(
            (worldX - Position.X) * Zoom + _shakeOffset.X,
            (worldY - Position.Y) * Zoom + _shakeOffset.Y
        );
    }

    /// <summary>
    /// Convert a screen position to world position.
    /// Useful for mouse picking.
    /// </summary>
    public Vec2 ScreenToWorld(float screenX, float screenY)
    {
        return new Vec2(
            (screenX - _shakeOffset.X) / Zoom + Position.X,
            (screenY - _shakeOffset.Y) / Zoom + Position.Y
        );
    }

    private void ClampBounds()
    {
        if (!_hasBounds) return;

        float viewW = ScreenWidth / Zoom;
        float viewH = ScreenHeight / Zoom;

        // When viewport is larger than bounds, center instead of oscillating
        float maxX = System.Math.Max(_boundsMinX, _boundsMaxX - viewW);
        float maxY = System.Math.Max(_boundsMinY, _boundsMaxY - viewH);
        Position.X = System.Math.Clamp(Position.X, _boundsMinX, maxX);
        Position.Y = System.Math.Clamp(Position.Y, _boundsMinY, maxY);
    }
}
