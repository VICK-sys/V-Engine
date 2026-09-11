// fluid_solver.cpp — Native PBF fluid solver for V-Engine
// SOA layout for cache efficiency. No SIMD intrinsics (compiler auto-vectorizes with /O2 /arch:AVX2).

#include "fluid_solver.h"

#include <cmath>
#include <cstdlib>
#include <cstring>
#include <algorithm>
#include <unordered_map>
#include <vector>

static constexpr float PI = 3.14159265358979323846f;
static constexpr int MAX_NEIGHBORS = 48;

// ── Kernel precomputed coefficients ─────────────────────────

struct KernelCoeffs {
    float h, h2;
    float poly6_coeff;
    float spiky_grad_coeff;
    float visc_lap_coeff;
};

static void init_kernels(KernelCoeffs& k, float h) {
    k.h = h;
    k.h2 = h * h;
    float h4 = k.h2 * k.h2;
    float h5 = h4 * h;
    float h8 = h4 * h4;
    k.poly6_coeff = 4.0f / (PI * h8);
    k.spiky_grad_coeff = -30.0f / (PI * h5);
    k.visc_lap_coeff = 40.0f / (PI * h5);
}

static inline float poly6(const KernelCoeffs& k, float r2) {
    if (r2 >= k.h2) return 0.0f;
    float diff = k.h2 - r2;
    return k.poly6_coeff * diff * diff * diff;
}

static inline void spiky_gradient(const KernelCoeffs& k, float rx, float ry, float r,
                                  float& gx, float& gy) {
    if (r >= k.h || r < 1e-6f) { gx = gy = 0; return; }
    float diff = k.h - r;
    float scale = k.spiky_grad_coeff * diff * diff;
    gx = rx * scale;  // rx is already normalized (rDir)
    gy = ry * scale;
}

// ── Spatial Hash ────────────────────────────────────────────

struct SpatialHash {
    float inv_cell;
    std::unordered_map<int64_t, std::vector<int>> cells;

    void init(float cell_size) {
        inv_cell = 1.0f / cell_size;
    }

    void clear() {
        for (auto& kv : cells) kv.second.clear();
    }

    int64_t cell_key(int col, int row) const {
        int64_t c = (int64_t)col + 0x80000000LL;
        int64_t r = (int64_t)row + 0x80000000LL;
        return (c << 32) | (r & 0xFFFFFFFFLL);
    }

    void insert(int idx, float x, float y) {
        int col = (int)floorf(x * inv_cell);
        int row = (int)floorf(y * inv_cell);
        cells[cell_key(col, row)].push_back(idx);
    }

    void query(float x, float y, std::vector<int>& out) const {
        int col = (int)floorf(x * inv_cell);
        int row = (int)floorf(y * inv_cell);
        for (int dr = -1; dr <= 1; dr++) {
            for (int dc = -1; dc <= 1; dc++) {
                auto it = cells.find(cell_key(col + dc, row + dr));
                if (it != cells.end()) {
                    const auto& list = it->second;
                    out.insert(out.end(), list.begin(), list.end());
                }
            }
        }
    }
};

// ── Solver ──────────────────────────────────────────────────

struct FluidSolverHandle {
    int capacity;
    int count; // number of particles uploaded

    // SOA particle data
    float* px; float* py;     // position
    float* vx; float* vy;     // velocity
    float* weight;            // merge weight
    int*   active;            // active flag

    // Working arrays
    float* pred_x; float* pred_y;  // predicted position
    float* lambda;                  // constraint multiplier
    float* delta_x; float* delta_y; // position correction
    float* density;
    int*   neighbor_count;

    // Neighbor cache (flat: [i * MAX_NEIGHBORS + n])
    int*   neighbors;

    // Active index list
    int*   active_indices;
    int    active_count;

    // Divergence/vorticity
    float* divergence;
    float* vorticity;

    KernelCoeffs kern;
    SpatialHash hash;

    // Parameters
    float particle_mass, rest_density, relaxation;
    float surface_tension_k, xsph_viscosity;
    float gx, gy; // gravity
    float max_speed;
    float vorticity_strength;

    // Bounds
    float bounds_left, bounds_top, bounds_right, bounds_bottom;
    float boundary_damping;
};

// ── Lifecycle ───────────────────────────────────────────────

FLUID_API FluidSolver fluid_create(int capacity, float smoothing_radius) {
    auto* s = new FluidSolverHandle();
    s->capacity = capacity;
    s->count = 0;

    // Allocate SOA arrays (aligned for potential SIMD)
    auto alloc_f = [&](int n) { return (float*)calloc(n, sizeof(float)); };
    auto alloc_i = [&](int n) { return (int*)calloc(n, sizeof(int)); };

    s->px = alloc_f(capacity); s->py = alloc_f(capacity);
    s->vx = alloc_f(capacity); s->vy = alloc_f(capacity);
    s->weight = alloc_f(capacity);
    s->active = alloc_i(capacity);

    s->pred_x = alloc_f(capacity); s->pred_y = alloc_f(capacity);
    s->lambda = alloc_f(capacity);
    s->delta_x = alloc_f(capacity); s->delta_y = alloc_f(capacity);
    s->density = alloc_f(capacity);
    s->neighbor_count = alloc_i(capacity);
    s->neighbors = alloc_i(capacity * MAX_NEIGHBORS);
    s->active_indices = alloc_i(capacity);
    s->divergence = alloc_f(capacity);
    s->vorticity = alloc_f(capacity);

    s->active_count = 0;

    init_kernels(s->kern, smoothing_radius);
    s->hash.init(smoothing_radius);

    // Default params
    s->particle_mass = 1.0f;
    s->rest_density = 1.0f;
    s->relaxation = 300.0f;
    s->surface_tension_k = 0.1f;
    s->xsph_viscosity = 0.07f;
    s->gx = 0; s->gy = 400.0f;
    s->max_speed = 800.0f;
    s->vorticity_strength = 0.15f;
    s->boundary_damping = 0.3f;

    return s;
}

FLUID_API void fluid_destroy(FluidSolver s) {
    if (!s) return;
    free(s->px); free(s->py);
    free(s->vx); free(s->vy);
    free(s->weight); free(s->active);
    free(s->pred_x); free(s->pred_y);
    free(s->lambda);
    free(s->delta_x); free(s->delta_y);
    free(s->density); free(s->neighbor_count);
    free(s->neighbors); free(s->active_indices);
    free(s->divergence); free(s->vorticity);
    delete s;
}

// ── Configuration ───────────────────────────────────────────

FLUID_API void fluid_set_params(
    FluidSolver s,
    float particle_mass, float rest_density, float relaxation,
    float surface_tension_k, float xsph_viscosity,
    float gravity_x, float gravity_y,
    float max_speed, float vorticity_strength)
{
    s->particle_mass = particle_mass;
    s->rest_density = rest_density > 0 ? rest_density : 1.0f;
    s->relaxation = relaxation;
    s->surface_tension_k = surface_tension_k;
    s->xsph_viscosity = xsph_viscosity;
    s->gx = gravity_x; s->gy = gravity_y;
    s->max_speed = max_speed;
    s->vorticity_strength = vorticity_strength;
}

FLUID_API void fluid_set_bounds(FluidSolver s,
    float left, float top, float right, float bottom, float damping)
{
    s->bounds_left = left; s->bounds_top = top;
    s->bounds_right = right; s->bounds_bottom = bottom;
    s->boundary_damping = damping;
}

// ── Upload/Download ─────────────────────────────────────────

FLUID_API void fluid_upload_particles(FluidSolver s, int count,
    const float* pos_x, const float* pos_y,
    const float* vel_x, const float* vel_y,
    const float* w, const int* act)
{
    int n = count < s->capacity ? count : s->capacity;
    s->count = n;
    memcpy(s->px, pos_x, n * sizeof(float));
    memcpy(s->py, pos_y, n * sizeof(float));
    memcpy(s->vx, vel_x, n * sizeof(float));
    memcpy(s->vy, vel_y, n * sizeof(float));
    memcpy(s->weight, w, n * sizeof(float));
    memcpy(s->active, act, n * sizeof(int));
}

FLUID_API void fluid_download_particles(FluidSolver s,
    float* pos_x, float* pos_y,
    float* vel_x, float* vel_y,
    float* dens, int* nc)
{
    int n = s->count;
    memcpy(pos_x, s->px, n * sizeof(float));
    memcpy(pos_y, s->py, n * sizeof(float));
    memcpy(vel_x, s->vx, n * sizeof(float));
    memcpy(vel_y, s->vy, n * sizeof(float));
    memcpy(dens, s->density, n * sizeof(float));
    memcpy(nc, s->neighbor_count, n * sizeof(int));
}

FLUID_API int fluid_get_capacity(FluidSolver s) { return s->capacity; }

// ── Solver internals ────────────────────────────────────────

static void build_active_list(FluidSolverHandle* s) {
    s->active_count = 0;
    for (int i = 0; i < s->count; i++) {
        if (s->active[i])
            s->active_indices[s->active_count++] = i;
    }
}

static void predict_positions(FluidSolverHandle* s, float dt) {
    float gx = s->gx * dt, gy = s->gy * dt;
    for (int a = 0; a < s->active_count; a++) {
        int i = s->active_indices[a];
        s->vx[i] += gx;
        s->vy[i] += gy;
        s->pred_x[i] = s->px[i] + s->vx[i] * dt;
        s->pred_y[i] = s->py[i] + s->vy[i] * dt;
    }
}

static void hash_particles(FluidSolverHandle* s) {
    s->hash.clear();
    for (int a = 0; a < s->active_count; a++) {
        int i = s->active_indices[a];
        s->hash.insert(i, s->pred_x[i], s->pred_y[i]);
    }
}

static void build_neighbor_cache(FluidSolverHandle* s) {
    float h2 = s->kern.h2;
    std::vector<int> temp;
    temp.reserve(64);

    for (int a = 0; a < s->active_count; a++) {
        int i = s->active_indices[a];
        temp.clear();
        s->hash.query(s->pred_x[i], s->pred_y[i], temp);

        int offset = i * MAX_NEIGHBORS;
        int count = 0;
        float xi = s->pred_x[i], yi = s->pred_y[i];

        for (int n = 0; n < (int)temp.size() && count < MAX_NEIGHBORS; n++) {
            int j = temp[n];
            if (!s->active[j]) continue;
            float dx = xi - s->pred_x[j];
            float dy = yi - s->pred_y[j];
            if (dx * dx + dy * dy < h2)
                s->neighbors[offset + count++] = j;
        }
        s->neighbor_count[i] = count;
    }
}

static void compute_lambda(FluidSolverHandle* s) {
    const auto& k = s->kern;
    float pm = s->particle_mass;
    float rd = s->rest_density;
    float relax = s->relaxation;

    for (int a = 0; a < s->active_count; a++) {
        int i = s->active_indices[a];
        int offset = i * MAX_NEIGHBORS;
        int nc = s->neighbor_count[i];
        float xi = s->pred_x[i], yi = s->pred_y[i];

        // Compute density
        float dens = 0;
        for (int n = 0; n < nc; n++) {
            int j = s->neighbors[offset + n];
            float dx = xi - s->pred_x[j], dy = yi - s->pred_y[j];
            float sw = sqrtf(s->weight[j]);
            dens += pm * sw * poly6(k, dx * dx + dy * dy);
        }
        s->density[i] = dens > 1e-6f ? dens : 1e-6f;

        float constraint = dens / rd - 1.0f;
        if (constraint < 0) constraint = 0;

        // Gradient sum for denominator
        float grad_sum_sq = 0;
        float gi_x = 0, gi_y = 0;

        for (int n = 0; n < nc; n++) {
            int j = s->neighbors[offset + n];
            if (j == i) continue;

            float dx = xi - s->pred_x[j], dy = yi - s->pred_y[j];
            float r2 = dx * dx + dy * dy;
            if (r2 < 1e-6f) continue;
            float r = sqrtf(r2);
            float inv_r = 1.0f / r;
            float rx = dx * inv_r, ry = dy * inv_r;
            float sw = sqrtf(s->weight[j]);

            float gx, gy;
            spiky_gradient(k, rx, ry, r, gx, gy);
            float scale = -sw / rd;
            float gjx = gx * scale, gjy = gy * scale;

            grad_sum_sq += gjx * gjx + gjy * gjy;
            gi_x -= gjx;
            gi_y -= gjy;
        }

        grad_sum_sq += gi_x * gi_x + gi_y * gi_y;
        s->lambda[i] = -constraint / (grad_sum_sq + relax);
    }
}

static void compute_position_delta(FluidSolverHandle* s) {
    const auto& k = s->kern;
    float dq = 0.3f * k.h;
    float w_dq = poly6(k, dq * dq);
    float w_dq_safe = w_dq > 1e-12f ? w_dq : 1.0f;
    float stk = s->surface_tension_k;
    float rd = s->rest_density;

    for (int a = 0; a < s->active_count; a++) {
        int i = s->active_indices[a];
        int offset = i * MAX_NEIGHBORS;
        int nc = s->neighbor_count[i];
        float xi = s->pred_x[i], yi = s->pred_y[i];
        float li = s->lambda[i];

        float dx_sum = 0, dy_sum = 0;

        for (int n = 0; n < nc; n++) {
            int j = s->neighbors[offset + n];
            if (j == i) continue;

            float dx = xi - s->pred_x[j], dy = yi - s->pred_y[j];
            float r2 = dx * dx + dy * dy;
            if (r2 < 1e-6f) continue;
            float r = sqrtf(r2);
            float inv_r = 1.0f / r;
            float rx = dx * inv_r, ry = dy * inv_r;

            // Surface tension correction
            float wij = poly6(k, r2);
            float ratio = wij / w_dq_safe;
            float r2_ = ratio * ratio;
            float scorr = -stk * r2_ * r2_;

            float sw = sqrtf(s->weight[j]);
            float coeff = (li + s->lambda[j] + scorr) * sw;

            float gx, gy;
            spiky_gradient(k, rx, ry, r, gx, gy);
            dx_sum += gx * coeff;
            dy_sum += gy * coeff;
        }

        s->delta_x[i] = dx_sum / rd;
        s->delta_y[i] = dy_sum / rd;
    }
}

static void apply_position_delta(FluidSolverHandle* s) {
    for (int a = 0; a < s->active_count; a++) {
        int i = s->active_indices[a];
        s->pred_x[i] += s->delta_x[i];
        s->pred_y[i] += s->delta_y[i];
    }
}

static void update_velocities(FluidSolverHandle* s, float dt) {
    float inv_dt = 1.0f / dt;
    float max_sq = s->max_speed * s->max_speed;

    for (int a = 0; a < s->active_count; a++) {
        int i = s->active_indices[a];
        s->vx[i] = (s->pred_x[i] - s->px[i]) * inv_dt;
        s->vy[i] = (s->pred_y[i] - s->py[i]) * inv_dt;

        float sq = s->vx[i] * s->vx[i] + s->vy[i] * s->vy[i];
        if (sq > max_sq) {
            float scale = s->max_speed / sqrtf(sq);
            s->vx[i] *= scale;
            s->vy[i] *= scale;
        }
    }
}

static void apply_viscosity(FluidSolverHandle* s) {
    float visc = s->xsph_viscosity;
    if (visc <= 0) return;

    const auto& k = s->kern;

    for (int a = 0; a < s->active_count; a++) {
        int i = s->active_indices[a];
        int offset = i * MAX_NEIGHBORS;
        int nc = s->neighbor_count[i];
        float xi = s->pred_x[i], yi = s->pred_y[i];

        float cx = 0, cy = 0;
        for (int n = 0; n < nc; n++) {
            int j = s->neighbors[offset + n];
            if (j == i) continue;

            float dx = xi - s->pred_x[j], dy = yi - s->pred_y[j];
            float w = poly6(k, dx * dx + dy * dy);
            float rhoj = s->density[j] > 1e-4f ? s->density[j] : 1e-4f;
            float inv_rho = w / rhoj;
            cx += (s->vx[j] - s->vx[i]) * inv_rho;
            cy += (s->vy[j] - s->vy[i]) * inv_rho;
        }

        s->vx[i] += cx * visc;
        s->vy[i] += cy * visc;
    }
}

static void commit_positions(FluidSolverHandle* s) {
    for (int a = 0; a < s->active_count; a++) {
        int i = s->active_indices[a];
        s->px[i] = s->pred_x[i];
        s->py[i] = s->pred_y[i];
    }
}

static void resolve_boundaries(FluidSolverHandle* s) {
    float damp = s->boundary_damping;
    for (int a = 0; a < s->active_count; a++) {
        int i = s->active_indices[a];
        if (s->px[i] < s->bounds_left) {
            s->px[i] = s->bounds_left;
            s->vx[i] = fabsf(s->vx[i]) * damp;
        }
        if (s->px[i] > s->bounds_right) {
            s->px[i] = s->bounds_right;
            s->vx[i] = -fabsf(s->vx[i]) * damp;
        }
        if (s->py[i] < s->bounds_top) {
            s->py[i] = s->bounds_top;
            s->vy[i] = fabsf(s->vy[i]) * damp;
        }
        if (s->py[i] > s->bounds_bottom) {
            s->py[i] = s->bounds_bottom;
            s->vy[i] = -fabsf(s->vy[i]) * damp;
        }
    }
}

// ── Public: one substep ─────────────────────────────────────

FLUID_API void fluid_substep(FluidSolver s, float dt,
    int solver_iters, int div_iters,
    int do_viscosity, int do_vorticity)
{
    if (!s || s->active_count == 0) {
        // Rebuild active list on first call
        build_active_list(s);
        if (s->active_count == 0) return;
    }

    build_active_list(s);
    predict_positions(s, dt);
    hash_particles(s);
    build_neighbor_cache(s);

    for (int iter = 0; iter < solver_iters; iter++) {
        compute_lambda(s);
        compute_position_delta(s);
        apply_position_delta(s);
    }

    update_velocities(s, dt);

    if (do_viscosity)
        apply_viscosity(s);

    commit_positions(s);
    resolve_boundaries(s);
}
