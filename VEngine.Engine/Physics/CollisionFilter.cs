namespace VEngine.Engine.Physics;

/// <summary>
/// Collision filtering using category/mask bits and group index.
/// Controls which body pairs can collide.
/// </summary>
public struct CollisionFilter
{
    /// <summary>
    /// What this body IS. A bitmask category. Default 0x0001.
    /// Example: Player=0x0001, Enemy=0x0002, Projectile=0x0004, Wall=0x0008.
    /// </summary>
    public ushort CategoryBits;

    /// <summary>
    /// What this body COLLIDES WITH. Default 0xFFFF (everything).
    /// Example: Projectile.MaskBits = Enemy | Wall (doesn't hit player).
    /// </summary>
    public ushort MaskBits;

    /// <summary>
    /// Override for pair-level filtering. Default 0 (use category/mask).
    /// Positive: bodies with same GroupIndex always collide.
    /// Negative: bodies with same GroupIndex never collide.
    /// </summary>
    public short GroupIndex;

    /// <summary>Default filter: category 1, collides with everything, no group override.</summary>
    public static CollisionFilter Default => new() { CategoryBits = 0x0001, MaskBits = 0xFFFF, GroupIndex = 0 };

    public CollisionFilter()
    {
        CategoryBits = 0x0001;
        MaskBits = 0xFFFF;
        GroupIndex = 0;
    }

    /// <summary>
    /// Test whether two filters allow collision.
    /// </summary>
    public static bool ShouldCollide(CollisionFilter a, CollisionFilter b)
    {
        // Group index override (both must have the same non-zero group)
        if (a.GroupIndex != 0 && a.GroupIndex == b.GroupIndex)
            return a.GroupIndex > 0; // positive = always collide, negative = never

        // Category/mask check: both must match
        return (a.CategoryBits & b.MaskBits) != 0
            && (b.CategoryBits & a.MaskBits) != 0;
    }
}
