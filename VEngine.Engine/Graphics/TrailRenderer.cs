using System;
using VEngine.Engine.Core;
using VEngine.Engine.Math;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Renders a persistent trail behind moving entities. Add to a scene, call Follow()
/// or manually AddPoint(), and the trail renders as a tapering, fading polyline strip.
///
/// Usage:
///   var trail = new TrailRenderer();
///   trail.Follow(player);
///   trail.Thickness = 6;
///   trail.ColorStart = Color.Cyan;
///   scene.Add(trail);
/// </summary>
public class TrailRenderer : Entity
{
    private struct TrailPoint
    {
        public float X, Y, Age;
    }

    private readonly TrailPoint[] _points;
    private int _head;
    private int _count;
    private Entity? _target;
    private float _lastX, _lastY;
    private bool _hasLast;

    // ── Configuration ─────────────────────────────────────────

    /// <summary>How long each trail point persists (seconds).</summary>
    public float MaxLife = 0.5f;

    /// <summary>Trail width at the head (newest point, near the entity).</summary>
    public float Thickness = 4f;

    /// <summary>Trail width at the tail (oldest point). 0 = taper to nothing.</summary>
    public float ThicknessEnd = 0f;

    /// <summary>Minimum world-space distance between consecutive trail points.</summary>
    public float MinDistance = 2f;

    /// <summary>Color at the head (newest).</summary>
    public Color ColorStart = Color.White;

    /// <summary>Color at the tail (oldest).</summary>
    public Color ColorEnd = Color.White;

    /// <summary>Alpha at the head (0-1).</summary>
    public float AlphaStart = 1f;

    /// <summary>Alpha at the tail (0-1).</summary>
    public float AlphaEnd = 0f;

    /// <summary>Offset from the followed entity's center.</summary>
    public Vec2 FollowOffset;

    // ── Properties ─────────────────────────────────────────────

    /// <summary>Number of active trail points.</summary>
    public int Count => _count;

    /// <summary>Maximum number of trail points (ring buffer capacity).</summary>
    public int MaxPoints => _points.Length;

    // ── Constructor ────────────────────────────────────────────

    /// <summary>Create a trail renderer with a fixed-size ring buffer.</summary>
    /// <param name="maxPoints">Maximum trail points. Higher = longer trail at cost of memory.</param>
    public TrailRenderer(int maxPoints = 64) : base(0, 0)
    {
        _points = new TrailPoint[System.Math.Max(maxPoints, 2)];
    }

    // ── Public API ─────────────────────────────────────────────

    /// <summary>Auto-track an entity's center position each frame.</summary>
    public void Follow(Entity target)
    {
        _target = target;
        _hasLast = false;
    }

    /// <summary>Stop auto-tracking. Existing trail points fade out naturally.</summary>
    public void Unfollow()
    {
        _target = null;
    }

    /// <summary>Manually add a trail point at a world position.</summary>
    public void AddPoint(float x, float y)
    {
        if (_hasLast)
        {
            float dx = x - _lastX;
            float dy = y - _lastY;
            if (dx * dx + dy * dy < MinDistance * MinDistance) return;
        }

        _points[_head] = new TrailPoint { X = x, Y = y, Age = 0 };
        _head = (_head + 1) % _points.Length;
        if (_count < _points.Length) _count++;

        _lastX = x;
        _lastY = y;
        _hasLast = true;
    }

    /// <summary>Remove all trail points immediately.</summary>
    public void Clear()
    {
        _count = 0;
        _head = 0;
        _hasLast = false;
    }

    // ── Lifecycle ──────────────────────────────────────────────

    public override void Update(float dt)
    {
        base.Update(dt);

        // Auto-unfollow destroyed/inactive targets
        if (_target != null && (_target.Destroyed || !_target.Active))
            _target = null;

        // Sample target position
        if (_target != null)
        {
            float cx = _target.Position.X + _target.Width * 0.5f + FollowOffset.X;
            float cy = _target.Position.Y + _target.Height * 0.5f + FollowOffset.Y;
            AddPoint(cx, cy);
        }

        // Age all points
        int start = TailIndex();
        for (int i = 0; i < _count; i++)
        {
            int idx = (start + i) % _points.Length;
            _points[idx].Age += dt;
        }

        // Trim expired points from oldest end
        while (_count > 0)
        {
            int tailIdx = TailIndex();
            if (_points[tailIdx].Age >= MaxLife)
                _count--;
            else
                break;
        }
    }

    public override void Draw()
    {
        if (_count < 2) return;

        var cam = Eng.Camera;
        int start = TailIndex();
        int len = _points.Length;
        float countF = _count - 1f;

        // Render each segment as a quad with per-vertex color/width
        for (int i = 0; i < _count - 1; i++)
        {
            int idx0 = (start + i) % len;
            int idx1 = (start + i + 1) % len;

            // Normalized position: 0 = oldest/tail, 1 = newest/head
            float t0 = i / countF;
            float t1 = (i + 1) / countF;

            // Interpolate width (scaled by camera zoom)
            float halfW0 = (ThicknessEnd + (Thickness - ThicknessEnd) * t0) * 0.5f * cam.Zoom;
            float halfW1 = (ThicknessEnd + (Thickness - ThicknessEnd) * t1) * 0.5f * cam.Zoom;

            // Interpolate color and alpha
            var c0 = Color.Lerp(ColorEnd, ColorStart, t0);
            var c1 = Color.Lerp(ColorEnd, ColorStart, t1);
            float a0 = AlphaEnd + (AlphaStart - AlphaEnd) * t0;
            float a1 = AlphaEnd + (AlphaStart - AlphaEnd) * t1;

            // Screen-space positions (respects ScrollFactor)
            var s0 = cam.Transform(_points[idx0].X, _points[idx0].Y, ScrollFactor);
            var s1 = cam.Transform(_points[idx1].X, _points[idx1].Y, ScrollFactor);

            // Segment direction and perpendicular normal
            float dx = s1.X - s0.X;
            float dy = s1.Y - s0.Y;
            float segLen = MathF.Sqrt(dx * dx + dy * dy);
            if (segLen < 0.001f) continue;

            float nx = -dy / segLen;
            float ny = dx / segLen;

            // Miter at p0 (blend with previous segment normal)
            float nx0 = nx, ny0 = ny;
            if (i > 0)
            {
                var sp = cam.Transform(_points[(start + i - 1) % len].X,
                                       _points[(start + i - 1) % len].Y, ScrollFactor);
                Miter(sp, s0, s1, out nx0, out ny0);
            }

            // Miter at p1 (blend with next segment normal)
            float nx1 = nx, ny1 = ny;
            if (i < _count - 2)
            {
                var sn = cam.Transform(_points[(start + i + 2) % len].X,
                                       _points[(start + i + 2) % len].Y, ScrollFactor);
                Miter(s0, s1, sn, out nx1, out ny1);
            }

            // Submit quad with per-vertex gradient
            Eng.GL.Primitives.DrawFilledQuadGradient(
                s0.X + nx0 * halfW0, s0.Y + ny0 * halfW0, c0.R / 255f, c0.G / 255f, c0.B / 255f, a0,
                s1.X + nx1 * halfW1, s1.Y + ny1 * halfW1, c1.R / 255f, c1.G / 255f, c1.B / 255f, a1,
                s1.X - nx1 * halfW1, s1.Y - ny1 * halfW1, c1.R / 255f, c1.G / 255f, c1.B / 255f, a1,
                s0.X - nx0 * halfW0, s0.Y - ny0 * halfW0, c0.R / 255f, c0.G / 255f, c0.B / 255f, a0);
        }
    }

    // ── Internal helpers ───────────────────────────────────────

    /// <summary>Index of the oldest (tail) point in the ring buffer.</summary>
    private int TailIndex() => (_head - _count + _points.Length) % _points.Length;

    /// <summary>
    /// Compute miter normal at the middle point of three consecutive screen-space points.
    /// Averages the normals of the two adjacent segments for smooth joins.
    /// </summary>
    private static void Miter(Vec2 prev, Vec2 mid, Vec2 next, out float mx, out float my)
    {
        float pdx = mid.X - prev.X;
        float pdy = mid.Y - prev.Y;
        float plen = MathF.Sqrt(pdx * pdx + pdy * pdy);

        float ndx = next.X - mid.X;
        float ndy = next.Y - mid.Y;
        float nlen = MathF.Sqrt(ndx * ndx + ndy * ndy);

        if (plen < 0.001f || nlen < 0.001f)
        {
            // Degenerate — use the non-degenerate segment's normal, or fallback to (0,1)
            if (nlen >= 0.001f) { mx = -ndy / nlen; my = ndx / nlen; }
            else if (plen >= 0.001f) { mx = -pdy / plen; my = pdx / plen; }
            else { mx = 0; my = 1; }
            return;
        }

        // Average the perpendicular normals of both segments
        float pnx = -pdy / plen;
        float pny = pdx / plen;
        float nnx = -ndy / nlen;
        float nny = ndx / nlen;
        float avgNx = (pnx + nnx) * 0.5f;
        float avgNy = (pny + nny) * 0.5f;
        float avgLen = MathF.Sqrt(avgNx * avgNx + avgNy * avgNy);

        if (avgLen < 0.001f)
        {
            mx = pnx;
            my = pny;
            return;
        }

        // Normalize; clamp miter scale to prevent spikes at sharp angles
        float scale = MathF.Min(1f / avgLen, 2f);
        mx = avgNx * scale;
        my = avgNy * scale;
    }
}
