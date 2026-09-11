// tilemap_collision.cpp — Native tilemap collision queries
// DDA raycast, AABB region scan, line-of-sight.

#include "tilemap_collision.h"
#include <cmath>
#include <cstring>
#include <vector>
#include <algorithm>

struct TileColImpl {
    int width, height, tileSize;
    float invTileSize;
    std::vector<int> solid;
};

static inline bool isSolid(TileColImpl* tc, int tx, int ty) {
    if (tx < 0 || tx >= tc->width || ty < 0 || ty >= tc->height) return false;
    return tc->solid[ty * tc->width + tx] != 0;
}

TILECOL_API TileColHandle tilecol_create(int width, int height, int tileSize, const int* solidData) {
    auto* tc = new TileColImpl();
    tc->width = width;
    tc->height = height;
    tc->tileSize = tileSize;
    tc->invTileSize = 1.0f / tileSize;
    tc->solid.resize(width * height);
    if (solidData)
        memcpy(tc->solid.data(), solidData, width * height * sizeof(int));
    return tc;
}

TILECOL_API void tilecol_destroy(TileColHandle tc) { delete tc; }

TILECOL_API void tilecol_update(TileColHandle tc, const int* solidData) {
    memcpy(tc->solid.data(), solidData, tc->width * tc->height * sizeof(int));
}

TILECOL_API void tilecol_set(TileColHandle tc, int tx, int ty, int solid) {
    if (tx >= 0 && tx < tc->width && ty >= 0 && ty < tc->height)
        tc->solid[ty * tc->width + tx] = solid;
}

TILECOL_API int tilecol_aabb_test(TileColHandle tc, float x, float y, float w, float h) {
    int minTX = (int)floorf(x * tc->invTileSize);
    int minTY = (int)floorf(y * tc->invTileSize);
    int maxTX = (int)ceilf((x + w) * tc->invTileSize) - 1;
    int maxTY = (int)ceilf((y + h) * tc->invTileSize) - 1;

    minTX = std::max(0, minTX);
    minTY = std::max(0, minTY);
    maxTX = std::min(tc->width - 1, maxTX);
    maxTY = std::min(tc->height - 1, maxTY);

    for (int ty = minTY; ty <= maxTY; ty++)
        for (int tx = minTX; tx <= maxTX; tx++)
            if (tc->solid[ty * tc->width + tx])
                return 1;
    return 0;
}

TILECOL_API int tilecol_aabb_query(
    TileColHandle tc, float x, float y, float w, float h,
    int* txOut, int* tyOut, int maxResults
) {
    int minTX = std::max(0, (int)floorf(x * tc->invTileSize));
    int minTY = std::max(0, (int)floorf(y * tc->invTileSize));
    int maxTX = std::min(tc->width - 1, (int)ceilf((x + w) * tc->invTileSize) - 1);
    int maxTY = std::min(tc->height - 1, (int)ceilf((y + h) * tc->invTileSize) - 1);

    int count = 0;
    for (int ty = minTY; ty <= maxTY && count < maxResults; ty++)
        for (int tx = minTX; tx <= maxTX && count < maxResults; tx++)
            if (tc->solid[ty * tc->width + tx]) {
                txOut[count] = tx;
                tyOut[count] = ty;
                count++;
            }
    return count;
}

// DDA raycast through the tile grid
TILECOL_API float tilecol_raycast(
    TileColHandle tc,
    float ox, float oy, float dx, float dy, float maxDist,
    int* hitTX, int* hitTY
) {
    float len = sqrtf(dx * dx + dy * dy);
    if (hitTX) *hitTX = -1;
    if (hitTY) *hitTY = -1;
    if (len < 1e-8f || maxDist < 0) return -1;
    dx /= len; dy /= len;

    float entry = 0, exit = maxDist;
    auto clip = [&](float origin, float direction, float size) {
        if (origin >= size && direction >= 0) return false;
        if (origin < 0 && direction <= 0) return false;
        if (direction == 0) return origin >= 0 && origin < size;
        float a = -origin / direction, b = (size - origin) / direction;
        if (a > b) std::swap(a, b);
        entry = std::max(entry, a);
        exit = std::min(exit, b);
        return entry <= exit;
    };
    if (!clip(ox, dx, (float)tc->width * tc->tileSize) ||
        !clip(oy, dy, (float)tc->height * tc->tileSize)) return -1;
    float originalEntry = entry;
    ox += dx * entry;
    oy += dy * entry;
    maxDist = exit - entry;

    float inv = tc->invTileSize;
    int tileX = (int)floorf(ox * inv);
    int tileY = (int)floorf(oy * inv);
    tileX = std::clamp(tileX, 0, tc->width - 1);
    tileY = std::clamp(tileY, 0, tc->height - 1);

    int stepX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
    int stepY = dy > 0 ? 1 : (dy < 0 ? -1 : 0);

    float ts = (float)tc->tileSize;
    float tMaxX = (dx != 0) ? ((stepX > 0 ? (tileX + 1) * ts - ox : tileX * ts - ox) / dx) : 1e18f;
    float tMaxY = (dy != 0) ? ((stepY > 0 ? (tileY + 1) * ts - oy : tileY * ts - oy) / dy) : 1e18f;
    float tDeltaX = (dx != 0) ? fabsf(ts / dx) : 1e18f;
    float tDeltaY = (dy != 0) ? fabsf(ts / dy) : 1e18f;

    float dist = 0;
    while (dist <= maxDist) {
        if (isSolid(tc, tileX, tileY)) {
            if (hitTX) *hitTX = tileX;
            if (hitTY) *hitTY = tileY;
            return originalEntry + dist;
        }

        if (tMaxX < tMaxY) {
            dist = tMaxX;
            tMaxX += tDeltaX;
            tileX += stepX;
        } else {
            dist = tMaxY;
            tMaxY += tDeltaY;
            tileY += stepY;
        }

        if (tileX < 0 || tileX >= tc->width || tileY < 0 || tileY >= tc->height)
            break;
    }
    return -1;
}

TILECOL_API int tilecol_line_of_sight(
    TileColHandle tc,
    float ax, float ay, float bx, float by
) {
    float dx = bx - ax, dy = by - ay;
    float dist = sqrtf(dx * dx + dy * dy);
    if (dist < 1e-6f) return !isSolid(tc, (int)floorf(ax * tc->invTileSize), (int)floorf(ay * tc->invTileSize));

    int hitTX, hitTY;
    float hitDist = tilecol_raycast(tc, ax, ay, dx, dy, dist, &hitTX, &hitTY);
    return hitDist < 0 ? 1 : 0;
}
