// pathfinder.h — Native A* grid pathfinding for V-Engine
// Binary heap priority queue, cache-friendly flat grid, diagonal support.

#pragma once

#ifdef _WIN32
    #ifdef PATHFINDER_EXPORTS
        #define PF_API __declspec(dllexport)
    #else
        #define PF_API __declspec(dllimport)
    #endif
#else
    #define PF_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct PathfinderImpl* PathfinderHandle;

// Create a pathfinder for a grid of width x height.
// walkable: flat array [width * height], 1 = passable, 0 = blocked.
PF_API PathfinderHandle pf_create(int width, int height, const int* walkable);
PF_API void pf_destroy(PathfinderHandle pf);

// Update the walkability grid (e.g., after tilemap changes).
PF_API void pf_update_grid(PathfinderHandle pf, const int* walkable);

// Set a single cell's walkability.
PF_API void pf_set_cell(PathfinderHandle pf, int x, int y, int walkable);

// Find a path from (sx,sy) to (ex,ey).
// allow_diagonal: 1 = 8-directional, 0 = 4-directional.
// max_search: maximum nodes to expand (0 = unlimited).
// Returns path length (number of waypoints), or 0 if no path.
// After calling, use pf_get_path_x/y to read waypoints.
PF_API int pf_find_path(
    PathfinderHandle pf,
    int sx, int sy, int ex, int ey,
    int allow_diagonal, int max_search
);

// Get the X coordinates of the last computed path. Array has pf_find_path() elements.
PF_API const int* pf_get_path_x(PathfinderHandle pf);

// Get the Y coordinates of the last computed path.
PF_API const int* pf_get_path_y(PathfinderHandle pf);

// Query if a cell is walkable.
PF_API int pf_is_walkable(PathfinderHandle pf, int x, int y);

// Grid dimensions.
PF_API int pf_width(PathfinderHandle pf);
PF_API int pf_height(PathfinderHandle pf);

#ifdef __cplusplus
}
#endif
