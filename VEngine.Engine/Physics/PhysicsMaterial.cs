namespace VEngine.Engine.Physics;

/// <summary>
/// Physical material properties for a rigid body shape.
/// </summary>
public struct PhysicsMaterial
{
    /// <summary>Coefficient of friction (0 = ice, 1 = high grip). Default 0.3.</summary>
    public float Friction;

    /// <summary>Coefficient of restitution / bounciness (0 = no bounce, 1 = perfect bounce). Default 0.</summary>
    public float Restitution;

    /// <summary>Density in mass per unit area. Used to compute mass from shape. Default 1.</summary>
    public float Density;

    public static PhysicsMaterial Default => new() { Friction = 0.3f, Restitution = 0f, Density = 1f };

    public PhysicsMaterial()
    {
        Friction = 0.3f;
        Restitution = 0f;
        Density = 1f;
    }
}
