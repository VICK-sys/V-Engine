namespace VEngine.Engine.Physics;

/// <summary>
/// A single spring column in the water surface wave simulation.
/// </summary>
public struct WaterColumn
{
    /// <summary>Vertical offset from rest height (positive = displaced downward in Y-down).</summary>
    public float Height;

    /// <summary>Vertical velocity of this column.</summary>
    public float Velocity;
}
