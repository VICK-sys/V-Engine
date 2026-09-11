using SDL2;

namespace VEngine.Engine.Math;

/// <summary>
/// RGBA color with byte channels (0-255). Engine's own color type,
/// decoupled from SDL. Implicit conversion to/from SDL_Color for compat.
/// </summary>
public struct Color
{
    public byte R, G, B, A;

    public Color(byte r, byte g, byte b, byte a = 255)
    {
        R = r; G = g; B = b; A = a;
    }

    // ── Static Presets ──────────────────────────────────────────

    public static readonly Color White = new(255, 255, 255);
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color Transparent = new(0, 0, 0, 0);
    public static readonly Color Red = new(255, 0, 0);
    public static readonly Color Green = new(0, 255, 0);
    public static readonly Color Blue = new(0, 0, 255);
    public static readonly Color Yellow = new(255, 255, 0);
    public static readonly Color Cyan = new(0, 255, 255);
    public static readonly Color Magenta = new(255, 0, 255);
    public static readonly Color Purple = new(124, 58, 237);
    public static readonly Color Orange = new(255, 165, 0);

    // ── Utilities ───────────────────────────────────────────────

    /// <summary>Returns a copy with a different alpha value.</summary>
    public Color WithAlpha(byte a) => new(R, G, B, a);

    /// <summary>Linearly interpolate between two colors.</summary>
    public static Color Lerp(Color a, Color b, float t)
    {
        t = System.Math.Clamp(t, 0f, 1f);
        return new Color(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t),
            (byte)(a.A + (b.A - a.A) * t)
        );
    }

    // ── SDL_Color Conversion ────────────────────────────────────

    public static implicit operator SDL.SDL_Color(Color c) =>
        new() { r = c.R, g = c.G, b = c.B, a = c.A };

    public static implicit operator Color(SDL.SDL_Color c) =>
        new(c.r, c.g, c.b, c.a);

    public override string ToString() => $"Color({R}, {G}, {B}, {A})";
}
