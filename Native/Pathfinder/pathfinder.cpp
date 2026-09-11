// pathfinder.cpp — Native A* grid pathfinding for V-Engine
// Binary min-heap, flat grid, 4/8-directional, weighted diagonals.

#include "pathfinder.h"
#include <cmath>
#include <cstring>
#include <vector>
#include <algorithm>

static constexpr float SQRT2 = 1.41421356f;

// ── Binary min-heap on f-cost ───────────────────────────────

struct Node {
    int index;     // grid cell index (y * width + x)
    float f;       // g + h
};

struct MinHeap {
    std::vector<Node> data;
    std::vector<int> posInHeap; // grid index -> position in heap (-1 = not in heap)
    int size;

    void init(int capacity) {
        data.resize(capacity);
        posInHeap.assign(capacity, -1);
        size = 0;
    }

    void clear() {
        for (int i = 0; i < size; i++)
            posInHeap[data[i].index] = -1;
        size = 0;
    }

    bool empty() const { return size == 0; }

    void push(int index, float f) {
        data[size] = {index, f};
        posInHeap[index] = size;
        siftUp(size);
        size++;
    }

    Node pop() {
        Node top = data[0];
        posInHeap[top.index] = -1;
        size--;
        if (size > 0) {
            data[0] = data[size];
            posInHeap[data[0].index] = 0;
            siftDown(0);
        }
        return top;
    }

    void decreaseKey(int index, float newF) {
        int pos = posInHeap[index];
        if (pos < 0) return;
        data[pos].f = newF;
        siftUp(pos);
    }

    bool contains(int index) const {
        return posInHeap[index] >= 0;
    }

private:
    void siftUp(int i) {
        while (i > 0) {
            int parent = (i - 1) / 2;
            if (data[i].f < data[parent].f) {
                std::swap(data[i], data[parent]);
                posInHeap[data[i].index] = i;
                posInHeap[data[parent].index] = parent;
                i = parent;
            } else break;
        }
    }

    void siftDown(int i) {
        while (true) {
            int best = i;
            int l = 2 * i + 1, r = 2 * i + 2;
            if (l < size && data[l].f < data[best].f) best = l;
            if (r < size && data[r].f < data[best].f) best = r;
            if (best == i) break;
            std::swap(data[i], data[best]);
            posInHeap[data[i].index] = i;
            posInHeap[data[best].index] = best;
            i = best;
        }
    }
};

// ── Pathfinder ──────────────────────────────────────────────

struct PathfinderImpl {
    int width, height;
    std::vector<int> walkable;  // flat grid: 1 = passable
    std::vector<float> gCost;
    std::vector<int> cameFrom;
    std::vector<int> closed;
    MinHeap open;

    // Result path
    std::vector<int> pathX, pathY;
};

static inline float heuristic(int ax, int ay, int bx, int by, bool diag) {
    if (diag) {
        // Octile distance
        int dx = abs(bx - ax), dy = abs(by - ay);
        return (dx + dy) + (SQRT2 - 2.0f) * std::min(dx, dy);
    }
    return (float)(abs(bx - ax) + abs(by - ay)); // Manhattan
}

// Neighbor offsets: 4-dir then 4 diagonals
static const int DX8[] = { 0, 1, 0, -1, 1, 1, -1, -1 };
static const int DY8[] = { -1, 0, 1, 0, -1, 1, 1, -1 };
static const float DCOST8[] = { 1, 1, 1, 1, SQRT2, SQRT2, SQRT2, SQRT2 };

PF_API PathfinderHandle pf_create(int width, int height, const int* walkableData) {
    auto* pf = new PathfinderImpl();
    pf->width = width;
    pf->height = height;
    int n = width * height;
    pf->walkable.resize(n);
    pf->gCost.resize(n);
    pf->cameFrom.resize(n);
    pf->closed.resize(n);
    if (walkableData)
        memcpy(pf->walkable.data(), walkableData, n * sizeof(int));
    else
        std::fill(pf->walkable.begin(), pf->walkable.end(), 1);
    pf->open.init(n);
    return pf;
}

PF_API void pf_destroy(PathfinderHandle pf) {
    delete pf;
}

PF_API void pf_update_grid(PathfinderHandle pf, const int* walkableData) {
    memcpy(pf->walkable.data(), walkableData, pf->width * pf->height * sizeof(int));
}

PF_API void pf_set_cell(PathfinderHandle pf, int x, int y, int walkable) {
    if (x >= 0 && x < pf->width && y >= 0 && y < pf->height)
        pf->walkable[y * pf->width + x] = walkable;
}

PF_API int pf_find_path(
    PathfinderHandle pf,
    int sx, int sy, int ex, int ey,
    int allowDiag, int maxSearch
) {
    int w = pf->width, h = pf->height;
    int n = w * h;

    pf->pathX.clear();
    pf->pathY.clear();

    // Bounds check
    if (sx < 0 || sx >= w || sy < 0 || sy >= h) return 0;
    if (ex < 0 || ex >= w || ey < 0 || ey >= h) return 0;

    int startIdx = sy * w + sx;
    int endIdx = ey * w + ex;

    if (!pf->walkable[endIdx]) return 0;

    // Reset
    std::fill(pf->gCost.begin(), pf->gCost.begin() + n, 1e18f);
    std::fill(pf->cameFrom.begin(), pf->cameFrom.begin() + n, -1);
    std::fill(pf->closed.begin(), pf->closed.begin() + n, 0);
    pf->open.clear();

    pf->gCost[startIdx] = 0;
    pf->open.push(startIdx, heuristic(sx, sy, ex, ey, allowDiag != 0));

    int dirs = allowDiag ? 8 : 4;
    int expanded = 0;
    int limit = maxSearch > 0 ? maxSearch : n;

    while (!pf->open.empty() && expanded < limit) {
        Node cur = pf->open.pop();
        int ci = cur.index;
        if (ci == endIdx) break;

        pf->closed[ci] = 1;
        expanded++;

        int cx = ci % w, cy = ci / w;

        for (int d = 0; d < dirs; d++) {
            int nx = cx + DX8[d], ny = cy + DY8[d];
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

            int ni = ny * w + nx;
            if (pf->closed[ni] || !pf->walkable[ni]) continue;

            // Diagonal: require both adjacent cardinals to be walkable (no corner cutting)
            if (d >= 4) {
                if (!pf->walkable[cy * w + nx] || !pf->walkable[ny * w + cx])
                    continue;
            }

            float ng = pf->gCost[ci] + DCOST8[d];
            if (ng < pf->gCost[ni]) {
                pf->gCost[ni] = ng;
                pf->cameFrom[ni] = ci;
                float f = ng + heuristic(nx, ny, ex, ey, allowDiag != 0);
                if (pf->open.contains(ni))
                    pf->open.decreaseKey(ni, f);
                else
                    pf->open.push(ni, f);
            }
        }
    }

    // Reconstruct path
    if (pf->cameFrom[endIdx] == -1 && startIdx != endIdx)
        return 0; // No path found

    // Trace back
    std::vector<int> trace;
    int ci = endIdx;
    while (ci != -1) {
        trace.push_back(ci);
        ci = pf->cameFrom[ci];
    }

    // Reverse into result
    int pathLen = (int)trace.size();
    pf->pathX.resize(pathLen);
    pf->pathY.resize(pathLen);
    for (int i = 0; i < pathLen; i++) {
        int idx = trace[pathLen - 1 - i];
        pf->pathX[i] = idx % w;
        pf->pathY[i] = idx / w;
    }

    return pathLen;
}

PF_API const int* pf_get_path_x(PathfinderHandle pf) { return pf->pathX.data(); }
PF_API const int* pf_get_path_y(PathfinderHandle pf) { return pf->pathY.data(); }

PF_API int pf_is_walkable(PathfinderHandle pf, int x, int y) {
    if (x < 0 || x >= pf->width || y < 0 || y >= pf->height) return 0;
    return pf->walkable[y * pf->width + x];
}

PF_API int pf_width(PathfinderHandle pf) { return pf->width; }
PF_API int pf_height(PathfinderHandle pf) { return pf->height; }
