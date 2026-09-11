namespace VEngine.Engine.Math;

/// <summary>
/// Axis-aligned rectangle. Used for collision bounds, hitboxes, and layout.
/// </summary>
public struct Rect
{
    public float X, Y, W, H;

    public Rect(float x, float y, float w, float h)
    {
        X = x; Y = y; W = w; H = h;
    }

    /// <summary>Right edge (X + W).</summary>
    public float Right => X + W;

    /// <summary>Bottom edge (Y + H).</summary>
    public float Bottom => Y + H;

    /// <summary>Center point.</summary>
    public Vec2 Center => new(X + W * 0.5f, Y + H * 0.5f);

    /// <summary>Check if this rect overlaps another.</summary>
    public bool Overlaps(Rect other) =>
        X < other.X + other.W && X + W > other.X &&
        Y < other.Y + other.H && Y + H > other.Y;

    /// <summary>Check if a point is inside this rect.</summary>
    public bool Contains(float px, float py) =>
        px >= X && px < X + W && py >= Y && py < Y + H;

    /// <summary>Implicit conversion from collision bounds tuple.</summary>
    public static implicit operator Rect((float X, float Y, float W, float H) t) =>
        new(t.X, t.Y, t.W, t.H);

    /// <summary>Implicit conversion to collision bounds tuple.</summary>
    public static implicit operator (float X, float Y, float W, float H)(Rect r) =>
        (r.X, r.Y, r.W, r.H);

    public override string ToString() => $"Rect({X:F1}, {Y:F1}, {W:F1}, {H:F1})";
}
