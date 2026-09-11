using System;
using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Fluid;

/// <summary>
/// SPH kernel functions for 2D fluid simulation.
/// Call SetSmoothingRadius() once before use — coefficients are precomputed.
/// </summary>
public static class FluidKernels
{
    public static float H;
    public static float H2;

    private static float _poly6Coeff;
    private static float _spikyGradCoeff;
    private static float _viscLapCoeff;

    /// <summary>Precompute all kernel coefficients for the given smoothing radius.</summary>
    public static void SetSmoothingRadius(float h)
    {
        H = h;
        H2 = h * h;
        float h4 = H2 * H2;
        float h5 = h4 * h;
        float h8 = h4 * h4;

        _poly6Coeff = 4f / (MathF.PI * h8);
        _spikyGradCoeff = -30f / (MathF.PI * h5);
        _viscLapCoeff = 40f / (MathF.PI * h5);
    }

    /// <summary>
    /// Poly6 kernel for density computation. Takes r² to avoid sqrt.
    /// W(r,h) = coeff * (h² - r²)³ for r² &lt; h²
    /// </summary>
    public static float Poly6(float r2)
    {
        if (r2 >= H2) return 0f;
        float diff = H2 - r2;
        return _poly6Coeff * diff * diff * diff;
    }

    /// <summary>
    /// Spiky kernel gradient for pressure forces.
    /// ∇W = coeff * (h - r)² * r̂, where r̂ = rDir (must be normalized).
    /// </summary>
    public static Vec2 SpikyGradient(Vec2 rDir, float r)
    {
        if (r >= H || r < 1e-6f) return Vec2.Zero;
        float diff = H - r;
        return rDir * (_spikyGradCoeff * diff * diff);
    }

    /// <summary>
    /// Viscosity kernel Laplacian.
    /// ∇²W = coeff * (h - r) for r &lt; h
    /// </summary>
    public static float ViscosityLaplacian(float r)
    {
        if (r >= H) return 0f;
        return _viscLapCoeff * (H - r);
    }
}
