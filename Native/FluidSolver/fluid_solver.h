// fluid_solver.h — Native PBF fluid solver for V-Engine
// SOA layout, SIMD-friendly, called via P/Invoke from C#.

#pragma once

#ifdef _WIN32
    #ifdef FLUID_EXPORTS
        #define FLUID_API __declspec(dllexport)
    #else
        #define FLUID_API __declspec(dllimport)
    #endif
#else
    #define FLUID_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// ── Solver handle ───────────────────────────────────────────

typedef struct FluidSolverHandle* FluidSolver;

// ── Lifecycle ───────────────────────────────────────────────

FLUID_API FluidSolver fluid_create(int capacity, float smoothing_radius);
FLUID_API void        fluid_destroy(FluidSolver solver);

// ── Configuration ───────────────────────────────────────────

FLUID_API void fluid_set_params(
    FluidSolver solver,
    float particle_mass,
    float rest_density,
    float relaxation,
    float surface_tension_k,
    float xsph_viscosity,
    float gravity_x, float gravity_y,
    float max_speed,
    float vorticity_strength
);

FLUID_API void fluid_set_bounds(
    FluidSolver solver,
    float left, float top, float right, float bottom,
    float damping
);

// ── Particle management ─────────────────────────────────────

// Upload particle data from C# arrays (SOA: separate x/y/vx/vy/weight/active arrays)
FLUID_API void fluid_upload_particles(
    FluidSolver solver,
    int count,
    const float* pos_x, const float* pos_y,
    const float* vel_x, const float* vel_y,
    const float* weight,
    const int*   active
);

// Download results back to C# arrays
FLUID_API void fluid_download_particles(
    FluidSolver solver,
    float* pos_x, float* pos_y,
    float* vel_x, float* vel_y,
    float* density,
    int*   neighbor_count
);

// ── Solve ───────────────────────────────────────────────────

// Run one complete substep: predict → hash → neighbors → N solver iters → velocity update → viscosity → boundaries
FLUID_API void fluid_substep(
    FluidSolver solver,
    float dt,
    int solver_iterations,
    int divergence_iterations,
    int do_viscosity,
    int do_vorticity
);

// ── Query ───────────────────────────────────────────────────

FLUID_API int fluid_get_capacity(FluidSolver solver);

#ifdef __cplusplus
}
#endif
