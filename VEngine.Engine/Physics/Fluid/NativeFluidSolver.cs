using System;
using System.Runtime.InteropServices;

namespace VEngine.Engine.Physics.Fluid;

/// <summary>
/// P/Invoke wrapper for the native C++ fluid solver DLL.
/// Offloads the PBF hot path (predict, hash, neighbors, solve, velocity update)
/// to native code for ~5-10x speedup on large particle counts.
///
/// The C# FluidSystem still owns particle lifecycle, rendering, body interaction,
/// and pouring. Only the per-substep solver loop is delegated.
/// </summary>
internal static class NativeFluidSolver
{
    private const string DLL = "fluid_solver";

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr fluid_create(int capacity, float smoothing_radius);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void fluid_destroy(IntPtr solver);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void fluid_set_params(
        IntPtr solver,
        float particle_mass, float rest_density, float relaxation,
        float surface_tension_k, float xsph_viscosity,
        float gravity_x, float gravity_y,
        float max_speed, float vorticity_strength);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void fluid_set_bounds(
        IntPtr solver,
        float left, float top, float right, float bottom,
        float damping);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void fluid_upload_particles(
        IntPtr solver, int count,
        float[] pos_x, float[] pos_y,
        float[] vel_x, float[] vel_y,
        float[] weight, int[] active);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void fluid_download_particles(
        IntPtr solver,
        float[] pos_x, float[] pos_y,
        float[] vel_x, float[] vel_y,
        float[] density, int[] neighbor_count);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void fluid_substep(
        IntPtr solver, float dt,
        int solver_iterations, int divergence_iterations,
        int do_viscosity, int do_vorticity);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int fluid_get_capacity(IntPtr solver);

    /// <summary>Check if the native DLL is available.</summary>
    public static bool IsAvailable()
    {
        try
        {
            var handle = fluid_create(1, 16f);
            if (handle != IntPtr.Zero)
            {
                fluid_destroy(handle);
                return true;
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return false;
    }
}
