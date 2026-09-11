// noise.h — Native procedural noise for V-Engine
// Perlin, Simplex, Worley (cellular) with 2D and 3D variants.

#pragma once

#ifdef _WIN32
    #ifdef NOISE_EXPORTS
        #define NOISE_API __declspec(dllexport)
    #else
        #define NOISE_API __declspec(dllimport)
    #endif
#else
    #define NOISE_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// Set the permutation seed (default 0). Deterministic across calls with same seed.
NOISE_API void noise_seed(unsigned int seed);

// ── Perlin (smooth, range ~[-1, 1]) ──

NOISE_API float noise_perlin_2d(float x, float y);
NOISE_API float noise_perlin_3d(float x, float y, float z);

// Fractal Brownian Motion: multi-octave Perlin sum.
// octaves: number of layers (1-8), lacunarity: frequency multiplier per octave (typ 2),
// persistence: amplitude multiplier per octave (typ 0.5).
NOISE_API float noise_perlin_fbm_2d(
    float x, float y,
    int octaves, float lacunarity, float persistence
);

// ── Simplex (smooth, range ~[-1, 1], faster than Perlin for 3D+) ──

NOISE_API float noise_simplex_2d(float x, float y);
NOISE_API float noise_simplex_3d(float x, float y, float z);

// ── Worley / Cellular (distance to nearest feature point, range ~[0, 1]) ──

// Returns distance to the nearest feature point in a grid of cells.
// jitter: how far feature points wander from cell centers (0 = grid, 1 = fully random).
NOISE_API float noise_worley_2d(float x, float y, float jitter);

// ── Batch API (for filling entire buffers at once, faster) ──

// Fill a buffer with 2D Perlin noise at each cell.
// output: [width * height] floats. scale: units per pixel (larger = smoother).
NOISE_API void noise_perlin_fill_2d(
    float* output, int width, int height,
    float offsetX, float offsetY, float scale
);

NOISE_API void noise_simplex_fill_2d(
    float* output, int width, int height,
    float offsetX, float offsetY, float scale
);

NOISE_API void noise_worley_fill_2d(
    float* output, int width, int height,
    float offsetX, float offsetY, float scale, float jitter
);

#ifdef __cplusplus
}
#endif
