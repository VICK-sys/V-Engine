// noise.cpp — Native procedural noise for V-Engine
// Classic Perlin, 2D/3D Simplex, and Worley cellular.

#include "noise.h"
#include <cmath>
#include <cstring>
#include <cstdlib>

// ── Permutation table (Ken Perlin's reference) ──────────────

static unsigned char PERM[512];
static bool PERM_INIT = false;

static void init_perm(unsigned int seed) {
    unsigned char base[256];
    for (int i = 0; i < 256; i++) base[i] = (unsigned char)i;

    // Seeded Fisher-Yates shuffle
    unsigned int s = seed ? seed : 12345u;
    for (int i = 255; i > 0; i--) {
        s = s * 1103515245u + 12345u;
        int j = (int)((s >> 16) % (i + 1));
        unsigned char t = base[i]; base[i] = base[j]; base[j] = t;
    }
    for (int i = 0; i < 256; i++) {
        PERM[i] = base[i];
        PERM[i + 256] = base[i];
    }
    PERM_INIT = true;
}

NOISE_API void noise_seed(unsigned int seed) {
    init_perm(seed);
}

static inline void ensure_init() {
    if (!PERM_INIT) init_perm(0);
}

// ── Perlin (classic) ────────────────────────────────────────

static inline float fade(float t) {
    return t * t * t * (t * (t * 6 - 15) + 10);
}
static inline float lerp(float a, float b, float t) { return a + t * (b - a); }
static inline float grad2d(int hash, float x, float y) {
    int h = hash & 7;
    float u = h < 4 ? x : y;
    float v = h < 4 ? y : x;
    return ((h & 1) ? -u : u) + ((h & 2) ? -2.0f * v : 2.0f * v);
}

NOISE_API float noise_perlin_2d(float x, float y) {
    ensure_init();
    int X = (int)floorf(x) & 255;
    int Y = (int)floorf(y) & 255;
    x -= floorf(x);
    y -= floorf(y);
    float u = fade(x), v = fade(y);
    int A = PERM[X] + Y;
    int B = PERM[X + 1] + Y;
    return lerp(
        lerp(grad2d(PERM[A], x, y), grad2d(PERM[B], x - 1, y), u),
        lerp(grad2d(PERM[A + 1], x, y - 1), grad2d(PERM[B + 1], x - 1, y - 1), u),
        v
    ) * 0.5f; // scale roughly to [-1,1]
}

static inline float grad3d(int hash, float x, float y, float z) {
    int h = hash & 15;
    float u = h < 8 ? x : y;
    float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
    return ((h & 1) ? -u : u) + ((h & 2) ? -v : v);
}

NOISE_API float noise_perlin_3d(float x, float y, float z) {
    ensure_init();
    int X = (int)floorf(x) & 255;
    int Y = (int)floorf(y) & 255;
    int Z = (int)floorf(z) & 255;
    x -= floorf(x); y -= floorf(y); z -= floorf(z);
    float u = fade(x), v = fade(y), w = fade(z);
    int A = PERM[X] + Y, AA = PERM[A] + Z, AB = PERM[A + 1] + Z;
    int B = PERM[X + 1] + Y, BA = PERM[B] + Z, BB = PERM[B + 1] + Z;
    return lerp(
        lerp(
            lerp(grad3d(PERM[AA], x, y, z), grad3d(PERM[BA], x - 1, y, z), u),
            lerp(grad3d(PERM[AB], x, y - 1, z), grad3d(PERM[BB], x - 1, y - 1, z), u),
            v
        ),
        lerp(
            lerp(grad3d(PERM[AA + 1], x, y, z - 1), grad3d(PERM[BA + 1], x - 1, y, z - 1), u),
            lerp(grad3d(PERM[AB + 1], x, y - 1, z - 1), grad3d(PERM[BB + 1], x - 1, y - 1, z - 1), u),
            v
        ),
        w
    );
}

NOISE_API float noise_perlin_fbm_2d(float x, float y, int octaves, float lacunarity, float persistence) {
    float total = 0, amplitude = 1, frequency = 1, maxVal = 0;
    if (octaves < 1) octaves = 1;
    if (octaves > 8) octaves = 8;
    for (int i = 0; i < octaves; i++) {
        total += noise_perlin_2d(x * frequency, y * frequency) * amplitude;
        maxVal += amplitude;
        amplitude *= persistence;
        frequency *= lacunarity;
    }
    return maxVal > 0 ? total / maxVal : 0;
}

// ── Simplex 2D ──────────────────────────────────────────────

static const float F2 = 0.3660254f;  // (sqrt(3) - 1) / 2
static const float G2 = 0.2113249f;  // (3 - sqrt(3)) / 6

NOISE_API float noise_simplex_2d(float x, float y) {
    ensure_init();
    float s = (x + y) * F2;
    int i = (int)floorf(x + s);
    int j = (int)floorf(y + s);
    float t = (i + j) * G2;
    float X0 = i - t, Y0 = j - t;
    float x0 = x - X0, y0 = y - Y0;

    int i1, j1;
    if (x0 > y0) { i1 = 1; j1 = 0; }
    else { i1 = 0; j1 = 1; }

    float x1 = x0 - i1 + G2;
    float y1 = y0 - j1 + G2;
    float x2 = x0 - 1.0f + 2.0f * G2;
    float y2 = y0 - 1.0f + 2.0f * G2;

    int ii = i & 255, jj = j & 255;
    int gi0 = PERM[ii + PERM[jj]] & 7;
    int gi1 = PERM[ii + i1 + PERM[jj + j1]] & 7;
    int gi2 = PERM[ii + 1 + PERM[jj + 1]] & 7;

    float n0, n1, n2;
    float t0 = 0.5f - x0 * x0 - y0 * y0;
    if (t0 < 0) n0 = 0;
    else { t0 *= t0; n0 = t0 * t0 * grad2d(gi0, x0, y0); }

    float t1 = 0.5f - x1 * x1 - y1 * y1;
    if (t1 < 0) n1 = 0;
    else { t1 *= t1; n1 = t1 * t1 * grad2d(gi1, x1, y1); }

    float t2 = 0.5f - x2 * x2 - y2 * y2;
    if (t2 < 0) n2 = 0;
    else { t2 *= t2; n2 = t2 * t2 * grad2d(gi2, x2, y2); }

    return 40.0f * (n0 + n1 + n2); // scale to ~[-1, 1]
}

NOISE_API float noise_simplex_3d(float x, float y, float z) {
    // Fallback to 3D Perlin for now — full Simplex 3D is lengthy
    return noise_perlin_3d(x, y, z);
}

// ── Worley (cellular) ───────────────────────────────────────

static inline float randCell(int cx, int cy, int channel) {
    // Hash cell coords deterministically
    unsigned int h = (unsigned int)(cx * 374761393u + cy * 668265263u + channel * 2147483647u);
    h = (h ^ (h >> 13)) * 1274126177u;
    h = h ^ (h >> 16);
    return (float)(h & 0xFFFFFF) / (float)0xFFFFFF; // [0,1]
}

NOISE_API float noise_worley_2d(float x, float y, float jitter) {
    int cx = (int)floorf(x);
    int cy = (int)floorf(y);
    float minDist = 1e18f;

    for (int dy = -1; dy <= 1; dy++) {
        for (int dx = -1; dx <= 1; dx++) {
            int ncx = cx + dx, ncy = cy + dy;
            float px = ncx + 0.5f + (randCell(ncx, ncy, 0) - 0.5f) * jitter;
            float py = ncy + 0.5f + (randCell(ncx, ncy, 1) - 0.5f) * jitter;
            float ddx = px - x, ddy = py - y;
            float d = ddx * ddx + ddy * ddy;
            if (d < minDist) minDist = d;
        }
    }
    return sqrtf(minDist);
}

// ── Batch Fill Functions ────────────────────────────────────

NOISE_API void noise_perlin_fill_2d(
    float* output, int width, int height,
    float offsetX, float offsetY, float scale
) {
    ensure_init();
    float invScale = scale != 0 ? 1.0f / scale : 1.0f;
    for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
            output[y * width + x] = noise_perlin_2d(
                (x + offsetX) * invScale,
                (y + offsetY) * invScale
            );
        }
    }
}

NOISE_API void noise_simplex_fill_2d(
    float* output, int width, int height,
    float offsetX, float offsetY, float scale
) {
    ensure_init();
    float invScale = scale != 0 ? 1.0f / scale : 1.0f;
    for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
            output[y * width + x] = noise_simplex_2d(
                (x + offsetX) * invScale,
                (y + offsetY) * invScale
            );
        }
    }
}

NOISE_API void noise_worley_fill_2d(
    float* output, int width, int height,
    float offsetX, float offsetY, float scale, float jitter
) {
    float invScale = scale != 0 ? 1.0f / scale : 1.0f;
    for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
            output[y * width + x] = noise_worley_2d(
                (x + offsetX) * invScale,
                (y + offsetY) * invScale,
                jitter
            );
        }
    }
}
