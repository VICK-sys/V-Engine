// batch_sorter.h — Native entity sort + frustum cull for V-Engine
// Stable radix sort by (layer, zorder) + AABB frustum cull.
// Returns an ordered index array of visible entities.

#pragma once

#ifdef _WIN32
    #ifdef BATCH_EXPORTS
        #define BATCH_API __declspec(dllexport)
    #else
        #define BATCH_API __declspec(dllimport)
    #endif
#else
    #define BATCH_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct BatchSorterImpl* BatchSorterHandle;

// Create a sorter with initial entity capacity.
BATCH_API BatchSorterHandle batch_create(int capacity);
BATCH_API void batch_destroy(BatchSorterHandle sorter);

// Upload entity sort/cull data. Arrays must have 'count' elements.
// visible: 1 = entity is visible, 0 = skip
// aabb: min/max screen-space bounds (already camera-transformed)
BATCH_API void batch_upload(
    BatchSorterHandle sorter, int count,
    const int* layer,
    const float* zorder,
    const int* original_index,
    const int* visible,
    const float* screen_x, const float* screen_y,
    const float* screen_w, const float* screen_h,
    float viewport_w, float viewport_h
);

// Sort + cull. Returns count of visible entities in draw order.
// After calling, use batch_get_order() to read the result.
BATCH_API int batch_sort_and_cull(BatchSorterHandle sorter);

// Get the sorted+culled index array. Returns pointer to internal buffer (valid until next call).
// Each element is an original_index of an entity that should be drawn, in draw order.
BATCH_API const int* batch_get_order(BatchSorterHandle sorter);

#ifdef __cplusplus
}
#endif
