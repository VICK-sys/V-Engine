using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Fluid;

/// <summary>
/// A single SPH fluid particle. Stored in pre-allocated arrays — no GC.
/// </summary>
public struct FluidParticle
{
    public Vec2 Position;
    public Vec2 Velocity;
    public Vec2 Force;

    // SPH computed values (updated each step)
    public float Density;
    public float Pressure;
    public Vec2 NearCenter;     // Weighted center of nearby particles (surface tension)
    public int NeighborCount;

    /// <summary>Merge weight. 1 = normal, >1 = merged particle representing multiple droplets.</summary>
    public float Weight;

    public bool Active;
}
