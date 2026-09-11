using System;

namespace VEngine.Engine.Core;

/// <summary>
/// Base class for scene transition effects.
/// Subclass and override Draw() for custom transitions.
/// </summary>
public abstract class Transition
{
    /// <summary>Total transition duration in seconds (out + in).</summary>
    public float Duration { get; }

    protected Transition(float duration)
    {
        if (duration <= 0) throw new ArgumentException("Transition duration must be positive", nameof(duration));
        Duration = duration;
    }

    /// <summary>
    /// Draw the transition overlay.
    /// Progress goes from 0 to 1. Scene swap happens at 0.5.
    /// </summary>
    public abstract void Draw(float progress);

    /// <summary>Fade to/from a solid color (default black).</summary>
    public static FadeTransition Fade(float duration = 0.6f, byte r = 0, byte g = 0, byte b = 0)
        => new(duration, r, g, b);

    /// <summary>Wipe: a solid rect sweeps across the screen.</summary>
    public static WipeTransition Wipe(float duration = 0.6f, WipeDir direction = WipeDir.Left)
        => new(duration, direction);

    /// <summary>Circle wipe (Zelda-style): circle closes to a point, then opens. Center defaults to screen center.</summary>
    public static CircleWipeTransition CircleWipe(float duration = 0.6f, float? centerX = null, float? centerY = null)
        => new(duration, centerX ?? Eng.Width / 2f, centerY ?? Eng.Height / 2f);

    /// <summary>Diamond wipe: diamond closes to a point, then opens. Center defaults to screen center.</summary>
    public static DiamondWipeTransition DiamondWipe(float duration = 0.6f, float? centerX = null, float? centerY = null)
        => new(duration, centerX ?? Eng.Width / 2f, centerY ?? Eng.Height / 2f);

    /// <summary>Pixelate dissolve: random blocks fill/clear in a pixel-grid pattern.</summary>
    public static PixelateTransition Pixelate(float duration = 0.8f, int blockSize = 8)
        => new(duration, blockSize);
}

/// <summary>Wipe direction.</summary>
public enum WipeDir { Left, Right, Up, Down }

/// <summary>
/// Fades the screen to a solid color and back.
/// </summary>
public class FadeTransition : Transition
{
    private readonly byte _r, _g, _b;

    public FadeTransition(float duration, byte r = 0, byte g = 0, byte b = 0) : base(duration)
    {
        _r = r; _g = g; _b = b;
    }

    public override void Draw(float progress)
    {
        float alpha = progress < 0.5f
            ? progress * 2f
            : (1f - progress) * 2f;

        float a = System.Math.Clamp(alpha, 0f, 1f);
        Eng.GL.FillRect(0, 0, Eng.Width, Eng.Height,
            _r / 255f, _g / 255f, _b / 255f, a);
    }
}

/// <summary>
/// A solid rect sweeps across the screen to cover, then retreats to reveal.
/// </summary>
public class WipeTransition : Transition
{
    private readonly WipeDir _direction;

    public WipeTransition(float duration, WipeDir direction = WipeDir.Left) : base(duration)
    {
        _direction = direction;
    }

    public override void Draw(float progress)
    {
        float t = progress < 0.5f
            ? progress * 2f
            : (1f - progress) * 2f;

        int w = Eng.Width, h = Eng.Height;
        float rx, ry, rw, rh;
        switch (_direction)
        {
            case WipeDir.Left:
                rx = 0; ry = 0; rw = w * t; rh = h;
                break;
            case WipeDir.Right:
                rw = w * t; rx = w - rw; ry = 0; rh = h;
                break;
            case WipeDir.Up:
                rx = 0; ry = 0; rw = w; rh = h * t;
                break;
            case WipeDir.Down:
                rh = h * t; rx = 0; ry = h - rh; rw = w;
                break;
            default:
                return;
        }

        Eng.GL.FillRect(rx, ry, rw, rh, 0, 0, 0, 1);
    }
}

/// <summary>
/// Zelda-style circle wipe: a circle closes to a point, then opens to reveal the new scene.
/// </summary>
public class CircleWipeTransition : Transition
{
    private readonly float _cx, _cy;
    private readonly float _maxR;
    private const int Segments = 48;

    public CircleWipeTransition(float duration, float centerX, float centerY) : base(duration)
    {
        _cx = centerX;
        _cy = centerY;
        float farX = MathF.Max(_cx, Eng.Width - _cx);
        float farY = MathF.Max(_cy, Eng.Height - _cy);
        _maxR = MathF.Sqrt(farX * farX + farY * farY);
    }

    public override void Draw(float progress)
    {
        float t = progress < 0.5f ? progress * 2f : (1f - progress) * 2f;
        float innerR = _maxR * (1f - t);
        float outerR = _maxR + Eng.Width + Eng.Height; // guaranteed to cover corners

        float step = MathF.PI * 2f / Segments;
        var prims = Eng.GL.Primitives;

        for (int i = 0; i < Segments; i++)
        {
            float a0 = i * step;
            float a1 = (i + 1) * step;
            float cos0 = MathF.Cos(a0), sin0 = MathF.Sin(a0);
            float cos1 = MathF.Cos(a1), sin1 = MathF.Sin(a1);

            prims.DrawFilledQuad(
                _cx + cos0 * innerR, _cy + sin0 * innerR,
                _cx + cos1 * innerR, _cy + sin1 * innerR,
                _cx + cos1 * outerR, _cy + sin1 * outerR,
                _cx + cos0 * outerR, _cy + sin0 * outerR,
                0, 0, 0, 1);
        }
    }
}

/// <summary>
/// Diamond wipe: a diamond shape closes to a point, then opens to reveal the new scene.
/// </summary>
public class DiamondWipeTransition : Transition
{
    private readonly float _cx, _cy;
    private readonly float _maxR;

    public DiamondWipeTransition(float duration, float centerX, float centerY) : base(duration)
    {
        _cx = centerX;
        _cy = centerY;
        // Manhattan distance to farthest corner
        _maxR = MathF.Max(_cx, Eng.Width - _cx) + MathF.Max(_cy, Eng.Height - _cy);
    }

    public override void Draw(float progress)
    {
        float t = progress < 0.5f ? progress * 2f : (1f - progress) * 2f;
        float r = _maxR * (1f - t);
        float R = _maxR + Eng.Width + Eng.Height; // outer radius

        // 4 diamond vertices (inner and outer)
        ReadOnlySpan<float> ix = [_cx, _cx + r, _cx, _cx - r];
        ReadOnlySpan<float> iy = [_cy - r, _cy, _cy + r, _cy];
        ReadOnlySpan<float> ox = [_cx, _cx + R, _cx, _cx - R];
        ReadOnlySpan<float> oy = [_cy - R, _cy, _cy + R, _cy];

        var prims = Eng.GL.Primitives;
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            prims.DrawFilledQuad(
                ix[i], iy[i], ix[j], iy[j],
                ox[j], oy[j], ox[i], oy[i],
                0, 0, 0, 1);
        }
    }
}

/// <summary>
/// Pixelate dissolve: random blocks fill to black, then clear to reveal the new scene.
/// </summary>
public class PixelateTransition : Transition
{
    private readonly int _blockSize;
    private readonly int _cols, _rows;
    private readonly int[] _order; // shuffled cell indices

    public PixelateTransition(float duration, int blockSize = 8) : base(duration)
    {
        _blockSize = System.Math.Max(1, blockSize);
        _cols = (Eng.Width + _blockSize - 1) / _blockSize;
        _rows = (Eng.Height + _blockSize - 1) / _blockSize;

        // Fisher-Yates shuffle with fixed seed for deterministic transitions
        int total = _cols * _rows;
        _order = new int[total];
        for (int i = 0; i < total; i++) _order[i] = i;
        var rng = new Random(42);
        for (int i = total - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }
    }

    public override void Draw(float progress)
    {
        float t = progress < 0.5f ? progress * 2f : (1f - progress) * 2f;
        int count = (int)(_order.Length * t);

        for (int i = 0; i < count; i++)
        {
            int idx = _order[i];
            int col = idx % _cols;
            int row = idx / _cols;
            Eng.GL.FillRect(col * _blockSize, row * _blockSize, _blockSize, _blockSize, 0, 0, 0, 1);
        }
    }
}
