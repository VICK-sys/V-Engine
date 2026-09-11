// physics_solver.h — Native 2D physics solver for V-Engine
// Handles broad phase (spatial hash), narrow phase (SAT), and sequential impulse solving.
// SOA layout for cache efficiency. Called via P/Invoke from C#.

#pragma once

#ifdef _WIN32
    #ifdef PHYSICS_EXPORTS
        #define PHYSICS_API __declspec(dllexport)
    #else
        #define PHYSICS_API __declspec(dllimport)
    #endif
#else
    #define PHYSICS_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// ── Shape types ─────────────────────────────────────────────

#define SHAPE_CIRCLE  0
#define SHAPE_POLYGON 1
#define MAX_POLY_VERTS 8
#define MAX_CONTACTS_PER_PAIR 2

// ── Body data (SOA, uploaded from C#) ───────────────────────

typedef struct {
    // Per-body arrays (count = body_count)
    int count;
    int capacity;

    // Position / angle
    float* pos_x;
    float* pos_y;
    float* angle;

    // Velocity
    float* vel_x;
    float* vel_y;
    float* ang_vel;

    // Inverse mass / inertia
    float* inv_mass;
    float* inv_inertia;

    // Material
    float* friction;
    float* restitution;

    // AABB (precomputed)
    float* aabb_min_x;
    float* aabb_min_y;
    float* aabb_max_x;
    float* aabb_max_y;

    // Shape type + data
    int* shape_type;          // SHAPE_CIRCLE or SHAPE_POLYGON
    float* circle_radius;     // valid when shape_type == SHAPE_CIRCLE
    float* circle_cx;         // circle center offset X
    float* circle_cy;         // circle center offset Y

    // Polygon data (flattened: body i starts at i * MAX_POLY_VERTS)
    float* poly_verts_x;      // [capacity * MAX_POLY_VERTS]
    float* poly_verts_y;
    float* poly_normals_x;
    float* poly_normals_y;
    int*   poly_count;         // vertex count per polygon

    // Body type: 0=static, 1=dynamic, 2=kinematic
    int* body_type;

    // Flags
    int* is_sleeping;
    int* is_trigger;
} BodySOA;

// ── Contact output ──────────────────────────────────────────

typedef struct {
    int body_a;
    int body_b;
    float normal_x, normal_y;
    float point_x[MAX_CONTACTS_PER_PAIR];
    float point_y[MAX_CONTACTS_PER_PAIR];
    float penetration[MAX_CONTACTS_PER_PAIR];
    float normal_impulse[MAX_CONTACTS_PER_PAIR];
    float tangent_impulse[MAX_CONTACTS_PER_PAIR];
    int point_count;
} ContactNative;

// ── Solver handle ───────────────────────────────────────────

typedef struct PhysicsSolverImpl* PhysicsSolverHandle;

// ── Lifecycle ───────────────────────────────────────────────

PHYSICS_API PhysicsSolverHandle physics_create(int body_capacity, float cell_size);
PHYSICS_API void physics_destroy(PhysicsSolverHandle solver);

// ── Upload body data from C# ────────────────────────────────

PHYSICS_API void physics_upload_bodies(
    PhysicsSolverHandle solver, int count,
    const float* pos_x, const float* pos_y, const float* angle,
    const float* vel_x, const float* vel_y, const float* ang_vel,
    const float* inv_mass, const float* inv_inertia,
    const float* friction, const float* restitution,
    const float* aabb_min_x, const float* aabb_min_y,
    const float* aabb_max_x, const float* aabb_max_y,
    const int* shape_type,
    const float* circle_radius, const float* circle_cx, const float* circle_cy,
    const float* poly_verts_x, const float* poly_verts_y,
    const float* poly_normals_x, const float* poly_normals_y,
    const int* poly_count,
    const int* body_type, const int* is_sleeping, const int* is_trigger
);

// ── Solve ───────────────────────────────────────────────────

// Run broad phase + narrow phase + velocity/position iterations.
// Writes results back to internal body arrays.
PHYSICS_API void physics_solve(
    PhysicsSolverHandle solver,
    float dt,
    float gravity_x, float gravity_y,
    int velocity_iterations,
    int position_iterations
);

// ── Download results ────────────────────────────────────────

// Download solved positions and velocities back to C# arrays.
PHYSICS_API void physics_download_bodies(
    PhysicsSolverHandle solver,
    float* pos_x, float* pos_y, float* angle,
    float* vel_x, float* vel_y, float* ang_vel
);

// Get contact count after solve.
PHYSICS_API int physics_contact_count(PhysicsSolverHandle solver);

// Download contacts (caller provides array of ContactNative, returns count written).
PHYSICS_API int physics_download_contacts(
    PhysicsSolverHandle solver,
    ContactNative* contacts, int max_contacts
);

#ifdef __cplusplus
}
#endif
