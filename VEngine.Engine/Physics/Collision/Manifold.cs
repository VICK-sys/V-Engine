using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Collision;

/// <summary>
/// A single contact point in a collision manifold.
/// </summary>
public struct ContactPoint
{
    /// <summary>World-space contact position.</summary>
    public Vec2 Position;

    /// <summary>Overlap depth along the manifold normal.</summary>
    public float Penetration;
}

/// <summary>
/// Collision manifold describing the contact between two shapes.
/// Contains 1 or 2 contact points and a shared normal.
/// </summary>
public struct Manifold
{
    /// <summary>World-space collision normal pointing from body A to body B.</summary>
    public Vec2 Normal;

    /// <summary>Contact points (up to 2).</summary>
    public ContactPoint Contact0;
    public ContactPoint Contact1;

    /// <summary>Number of contact points (0 = no collision, 1 or 2).</summary>
    public int ContactCount;

    public ContactPoint GetContact(int index) => index == 0 ? Contact0 : Contact1;
}
