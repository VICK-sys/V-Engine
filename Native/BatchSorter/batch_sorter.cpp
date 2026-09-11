// batch_sorter.cpp — Native entity sort + frustum cull for V-Engine
// Stable radix sort by (layer, zorder) + AABB frustum cull.

#include "batch_sorter.h"
#include <cstring>
#include <cmath>
#include <vector>
#include <algorithm>

struct EntityEntry {
    int originalIndex;
    int layer;
    float zorder;
    int visible;
    float screenX, screenY, screenW, screenH;
};

struct BatchSorterImpl {
    int capacity;
    std::vector<EntityEntry> entries;
    std::vector<int> result; // sorted+culled indices
    float viewW, viewH;

    void ensureCapacity(int n) {
        if (n > capacity) {
            capacity = n;
            entries.resize(n);
            result.resize(n);
        }
    }
};

// Stable sort comparator
static bool compareEntries(const EntityEntry& a, const EntityEntry& b) {
    if (a.layer != b.layer) return a.layer < b.layer;
    if (a.zorder != b.zorder) return a.zorder < b.zorder;
    return a.originalIndex < b.originalIndex; // stable tiebreaker
}

BATCH_API BatchSorterHandle batch_create(int capacity) {
    auto* s = new BatchSorterImpl();
    s->capacity = 0;
    s->viewW = 0;
    s->viewH = 0;
    s->ensureCapacity(capacity);
    return s;
}

BATCH_API void batch_destroy(BatchSorterHandle sorter) {
    delete sorter;
}

BATCH_API void batch_upload(
    BatchSorterHandle s, int count,
    const int* layer,
    const float* zorder,
    const int* originalIndex,
    const int* visible,
    const float* sx, const float* sy,
    const float* sw, const float* sh,
    float viewW, float viewH
) {
    s->ensureCapacity(count);
    s->viewW = viewW;
    s->viewH = viewH;

    for (int i = 0; i < count; i++) {
        s->entries[i] = {
            originalIndex[i],
            layer[i],
            zorder[i],
            visible[i],
            sx[i], sy[i], sw[i], sh[i]
        };
    }
    // Store count in result vector size
    s->result.resize(count);
}

BATCH_API int batch_sort_and_cull(BatchSorterHandle s) {
    int count = (int)s->result.size();
    if (count == 0) return 0;

    // Sort (std::stable_sort for guaranteed stability)
    std::stable_sort(s->entries.begin(), s->entries.begin() + count, compareEntries);

    // Cull: frustum test + visibility check
    int outCount = 0;
    float vw = s->viewW, vh = s->viewH;

    for (int i = 0; i < count; i++) {
        auto& e = s->entries[i];
        if (!e.visible) continue;

        // Frustum cull: skip entities entirely off-screen
        // Entities with zero size (particles, emitters) always pass
        if (e.screenW > 0 && e.screenH > 0) {
            if (e.screenX + e.screenW < 0 || e.screenX > vw) continue;
            if (e.screenY + e.screenH < 0 || e.screenY > vh) continue;
        }

        s->result[outCount++] = e.originalIndex;
    }

    return outCount;
}

BATCH_API const int* batch_get_order(BatchSorterHandle s) {
    return s->result.data();
}
