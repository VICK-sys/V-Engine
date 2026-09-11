// tilemap_collision.h — Native tilemap collision queries for V-Engine
// AABB region queries, raycast, and line-of-sight on solid tile grids.

#pragma once

#ifdef _WIN32
    #ifdef TILECOL_EXPORTS
        #define TILECOL_API __declspec(dllexport)
    #else
        #define TILECOL_API __declspec(dllimport)
    #endif
#else
    #define TILECOL_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct TileColImpl* TileColHandle;

// Create from a flat solid grid (1 = solid, 0 = empty). tileSize in world pixels.
TILECOL_API TileColHandle tilecol_create(int width, int height, int tileSize, const int* solid);
TILECOL_API void tilecol_destroy(TileColHandle tc);

// Update the solid grid.
TILECOL_API void tilecol_update(TileColHandle tc, const int* solid);

// Set a single tile's solidity.
TILECOL_API void tilecol_set(TileColHandle tc, int tx, int ty, int solid);

// Query: does an AABB (world coords) overlap any solid tile?
// Returns 1 if overlap, 0 if clear.
TILECOL_API int tilecol_aabb_test(TileColHandle tc, float x, float y, float w, float h);

// Query: find all solid tiles overlapping an AABB. Returns count.
// Writes tile coords to tx_out/ty_out arrays (caller provides, max max_results).
TILECOL_API int tilecol_aabb_query(
    TileColHandle tc, float x, float y, float w, float h,
    int* tx_out, int* ty_out, int max_results
);

// Raycast: cast from (ox,oy) in direction (dx,dy) for maxDist world pixels.
// Returns distance to first solid tile hit, or -1 if no hit.
// Writes hit tile coords to hit_tx/hit_ty.
TILECOL_API float tilecol_raycast(
    TileColHandle tc,
    float ox, float oy, float dx, float dy, float maxDist,
    int* hit_tx, int* hit_ty
);

// Line of sight: can a straight line from (ax,ay) to (bx,by) pass without hitting solid?
// Returns 1 if clear, 0 if blocked.
TILECOL_API int tilecol_line_of_sight(
    TileColHandle tc,
    float ax, float ay, float bx, float by
);

#ifdef __cplusplus
}
#endif
