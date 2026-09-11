using VEngine.Engine.Math;
using VEngine.Engine.Physics.Collision;

namespace VEngine.Engine.Physics.Shapes;

/// <summary>
/// Convenience box collider shape. Internally creates a PolygonShape with 4 vertices.
/// </summary>
public class BoxShape : PolygonShape
{
    /// <summary>Half-width of the box.</summary>
    public float HalfWidth { get; }

    /// <summary>Half-height of the box.</summary>
    public float HalfHeight { get; }

    /// <summary>Create a box shape centered at the body origin.</summary>
    public BoxShape(float width, float height)
    {
        HalfWidth = width * 0.5f;
        HalfHeight = height * 0.5f;
        SetAsBox(HalfWidth, HalfHeight);
    }

    /// <summary>Create a box shape with center offset and rotation.</summary>
    public BoxShape(float width, float height, Vec2 center, float angle = 0)
    {
        HalfWidth = width * 0.5f;
        HalfHeight = height * 0.5f;
        SetAsBox(HalfWidth, HalfHeight, center, angle);
    }

    public override Shape Clone() => new BoxShape(HalfWidth * 2, HalfHeight * 2);
}
