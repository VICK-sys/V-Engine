// physics_solver.cpp — Native 2D physics solver for V-Engine
// Broad phase (spatial hash) + Narrow phase (SAT) + Sequential impulse solver.

#include "physics_solver.h"
#include <cmath>
#include <cstring>
#include <vector>
#include <unordered_map>
#include <unordered_set>
#include <algorithm>

// ── Vec2 helpers ────────────────────────────────────────────

struct Vec2 { float x, y; };

static inline Vec2 v2(float x, float y) { return {x, y}; }
static inline Vec2 v2add(Vec2 a, Vec2 b) { return {a.x+b.x, a.y+b.y}; }
static inline Vec2 v2sub(Vec2 a, Vec2 b) { return {a.x-b.x, a.y-b.y}; }
static inline Vec2 v2scale(Vec2 a, float s) { return {a.x*s, a.y*s}; }
static inline Vec2 v2neg(Vec2 a) { return {-a.x, -a.y}; }
static inline float v2dot(Vec2 a, Vec2 b) { return a.x*b.x + a.y*b.y; }
static inline float v2cross(Vec2 a, Vec2 b) { return a.x*b.y - a.y*b.x; }
static inline Vec2 v2cross_sv(float s, Vec2 v) { return {-s*v.y, s*v.x}; }
static inline float v2cross_vs(Vec2 v, float s) { return v.x*s; }  // unused but matches pattern
static inline float v2lensq(Vec2 a) { return a.x*a.x + a.y*a.y; }
static inline float v2len(Vec2 a) { return sqrtf(v2lensq(a)); }
static inline Vec2 v2norm(Vec2 a) { float l = v2len(a); return l > 1e-6f ? v2scale(a, 1.0f/l) : v2(0,0); }
static inline Vec2 v2rotate(Vec2 v, float angle) {
    float c = cosf(angle), s = sinf(angle);
    return {v.x*c - v.y*s, v.x*s + v.y*c};
}

// ── Contact point ───────────────────────────────────────────

struct SolverContact {
    int bodyA, bodyB;
    Vec2 normal;
    Vec2 tangent;
    struct Point {
        Vec2 position;
        Vec2 relA, relB;
        float penetration;
        float normalMass, tangentMass;
        float normalImpulse, tangentImpulse;
        float velocityBias;
    } points[MAX_CONTACTS_PER_PAIR];
    int pointCount;
    float friction, restitution;
    bool isTrigger;
};

// ── Solver implementation ───────────────────────────────────

struct PhysicsSolverImpl {
    int bodyCapacity;
    float cellSize;
    float invCellSize;

    // SOA body data (owned copies)
    int bodyCount;
    std::vector<float> posX, posY, angle;
    std::vector<float> velX, velY, angVel;
    std::vector<float> invMass, invInertia;
    std::vector<float> frict, restit;
    std::vector<float> aabbMinX, aabbMinY, aabbMaxX, aabbMaxY;
    std::vector<int> shapeType;
    std::vector<float> circleR, circleCX, circleCY;
    std::vector<float> polyVX, polyVY, polyNX, polyNY;
    std::vector<int> polyCount;
    std::vector<int> bodyType;
    std::vector<int> isSleeping, isTrigger;

    // Broad phase
    std::unordered_map<int64_t, std::vector<int>> cells;

    // Contacts
    std::vector<SolverContact> contacts;

    void ensureCapacity(int n) {
        if (n <= bodyCapacity) return;
        bodyCapacity = n;
        posX.resize(n); posY.resize(n); angle.resize(n);
        velX.resize(n); velY.resize(n); angVel.resize(n);
        invMass.resize(n); invInertia.resize(n);
        frict.resize(n); restit.resize(n);
        aabbMinX.resize(n); aabbMinY.resize(n);
        aabbMaxX.resize(n); aabbMaxY.resize(n);
        shapeType.resize(n);
        circleR.resize(n); circleCX.resize(n); circleCY.resize(n);
        polyVX.resize(n * MAX_POLY_VERTS); polyVY.resize(n * MAX_POLY_VERTS);
        polyNX.resize(n * MAX_POLY_VERTS); polyNY.resize(n * MAX_POLY_VERTS);
        polyCount.resize(n);
        bodyType.resize(n);
        isSleeping.resize(n); isTrigger.resize(n);
    }
};

// ── Broad Phase ─────────────────────────────────────────────

static int64_t cellKey(int col, int row) {
    int64_t c = (int64_t)(col - INT32_MIN);
    int64_t r = (int64_t)(row - INT32_MIN);
    return (c << 32) | (r & 0xFFFFFFFFLL);
}

static void broadPhase(PhysicsSolverImpl* s, std::vector<std::pair<int,int>>& pairs) {
    s->cells.clear();
    float inv = s->invCellSize;

    for (int i = 0; i < s->bodyCount; i++) {
        if (s->bodyType[i] == 0 && !s->isTrigger[i]) continue; // skip non-trigger statics for insertion (they're collision targets but don't move)

        int minC = (int)floorf(s->aabbMinX[i] * inv);
        int minR = (int)floorf(s->aabbMinY[i] * inv);
        int maxC = (int)floorf(s->aabbMaxX[i] * inv);
        int maxR = (int)floorf(s->aabbMaxY[i] * inv);

        for (int r = minR; r <= maxR; r++)
            for (int c = minC; c <= maxC; c++)
                s->cells[cellKey(c, r)].push_back(i);
    }

    // Also insert statics (needed for collision)
    for (int i = 0; i < s->bodyCount; i++) {
        if (s->bodyType[i] != 0 || s->isTrigger[i]) continue;
        int minC = (int)floorf(s->aabbMinX[i] * inv);
        int minR = (int)floorf(s->aabbMinY[i] * inv);
        int maxC = (int)floorf(s->aabbMaxX[i] * inv);
        int maxR = (int)floorf(s->aabbMaxY[i] * inv);
        for (int r = minR; r <= maxR; r++)
            for (int c = minC; c <= maxC; c++)
                s->cells[cellKey(c, r)].push_back(i);
    }

    std::unordered_set<int64_t> seen;
    pairs.clear();

    for (auto& [key, list] : s->cells) {
        int n = (int)list.size();
        for (int i = 0; i < n; i++) {
            for (int j = i+1; j < n; j++) {
                int a = list[i], b = list[j];
                // Skip static-static
                if (s->bodyType[a] == 0 && s->bodyType[b] == 0) continue;
                // Skip sleeping pairs
                if (s->isSleeping[a] && s->isSleeping[b]) continue;

                int lo = a < b ? a : b;
                int hi = a < b ? b : a;
                int64_t pk = ((int64_t)lo << 32) | (int64_t)hi;
                if (!seen.insert(pk).second) continue;

                // AABB overlap
                if (s->aabbMaxX[a] < s->aabbMinX[b] || s->aabbMinX[a] > s->aabbMaxX[b]) continue;
                if (s->aabbMaxY[a] < s->aabbMinY[b] || s->aabbMinY[a] > s->aabbMaxY[b]) continue;

                pairs.emplace_back(a, b);
            }
        }
    }
}

// ── Narrow Phase: Circle vs Circle ──────────────────────────

static bool circleVsCircle(PhysicsSolverImpl* s, int a, int b, SolverContact& out) {
    Vec2 cA = v2add({s->posX[a], s->posY[a]}, v2rotate({s->circleCX[a], s->circleCY[a]}, s->angle[a]));
    Vec2 cB = v2add({s->posX[b], s->posY[b]}, v2rotate({s->circleCX[b], s->circleCY[b]}, s->angle[b]));
    Vec2 d = v2sub(cB, cA);
    float distSq = v2lensq(d);
    float rSum = s->circleR[a] + s->circleR[b];
    if (distSq > rSum * rSum) return false;

    float dist = sqrtf(distSq);
    out.pointCount = 1;
    if (dist < 1e-6f) {
        out.normal = {0, 1};
        out.points[0].position = cA;
        out.points[0].penetration = rSum;
    } else {
        out.normal = v2scale(d, 1.0f/dist);
        out.points[0].position = v2add(cA, v2scale(out.normal, s->circleR[a]));
        out.points[0].penetration = rSum - dist;
    }
    return true;
}

// ── Narrow Phase: Circle vs Polygon ─────────────────────────

static bool circleVsPolygon(PhysicsSolverImpl* s, int ci, int pi, SolverContact& out, bool flip) {
    Vec2 worldCenter = v2add({s->posX[ci], s->posY[ci]}, v2rotate({s->circleCX[ci], s->circleCY[ci]}, s->angle[ci]));
    float radius = s->circleR[ci];
    float pAngle = s->angle[pi];
    Vec2 pPos = {s->posX[pi], s->posY[pi]};

    // Transform circle center to polygon local space
    float ca = cosf(-pAngle), sa = sinf(-pAngle);
    Vec2 diff = v2sub(worldCenter, pPos);
    Vec2 local = {diff.x*ca - diff.y*sa, diff.x*sa + diff.y*ca};

    int pCount = s->polyCount[pi];
    int base = pi * MAX_POLY_VERTS;

    // Find closest face
    int normalIdx = 0;
    float separation = -1e18f;
    for (int i = 0; i < pCount; i++) {
        Vec2 v = {s->polyVX[base+i], s->polyVY[base+i]};
        Vec2 n = {s->polyNX[base+i], s->polyNY[base+i]};
        float sep = v2dot(n, v2sub(local, v));
        if (sep > radius) return false;
        if (sep > separation) { separation = sep; normalIdx = i; }
    }

    int v1 = normalIdx, v2i = (normalIdx+1) % pCount;
    Vec2 ev1 = {s->polyVX[base+v1], s->polyVY[base+v1]};
    Vec2 ev2 = {s->polyVX[base+v2i], s->polyVY[base+v2i]};
    Vec2 edge = v2sub(ev2, ev1);
    float u1 = v2dot(v2sub(local, ev1), edge);
    float u2 = v2dot(v2sub(local, ev2), v2neg(edge));

    Vec2 normal;
    float penetration;

    if (u1 <= 0) {
        Vec2 dd = v2sub(local, ev1);
        float dSq = v2lensq(dd);
        if (dSq > radius*radius) return false;
        float dist = sqrtf(dSq);
        Vec2 localN = dist > 1e-6f ? v2scale(dd, 1.0f/dist) : Vec2{s->polyNX[base+normalIdx], s->polyNY[base+normalIdx]};
        normal = v2rotate(localN, pAngle);
        penetration = radius - dist;
    } else if (u2 <= 0) {
        Vec2 dd = v2sub(local, ev2);
        float dSq = v2lensq(dd);
        if (dSq > radius*radius) return false;
        float dist = sqrtf(dSq);
        Vec2 localN = dist > 1e-6f ? v2scale(dd, 1.0f/dist) : Vec2{s->polyNX[base+normalIdx], s->polyNY[base+normalIdx]};
        normal = v2rotate(localN, pAngle);
        penetration = radius - dist;
    } else {
        normal = v2rotate({s->polyNX[base+normalIdx], s->polyNY[base+normalIdx]}, pAngle);
        penetration = radius - separation;
    }

    out.pointCount = 1;
    out.normal = flip ? v2neg(normal) : normal;
    out.points[0].position = v2sub(worldCenter, v2scale(normal, radius));
    out.points[0].penetration = penetration;
    return true;
}

// ── Narrow Phase: Polygon vs Polygon (SAT) ──────────────────

static float findMinSep(PhysicsSolverImpl* s, int a, int b, int& faceIdx) {
    int countA = s->polyCount[a], countB = s->polyCount[b];
    int baseA = a * MAX_POLY_VERTS, baseB = b * MAX_POLY_VERTS;
    float angA = s->angle[a], angB = s->angle[b];
    Vec2 pA = {s->posX[a], s->posY[a]}, pB = {s->posX[b], s->posY[b]};

    float maxSep = -1e18f;
    faceIdx = 0;

    for (int i = 0; i < countA; i++) {
        Vec2 n = v2rotate({s->polyNX[baseA+i], s->polyNY[baseA+i]}, angA);
        Vec2 v = v2add(pA, v2rotate({s->polyVX[baseA+i], s->polyVY[baseA+i]}, angA));

        float minDot = 1e18f;
        for (int j = 0; j < countB; j++) {
            Vec2 bv = v2add(pB, v2rotate({s->polyVX[baseB+j], s->polyVY[baseB+j]}, angB));
            float dot = v2dot(n, v2sub(bv, v));
            if (dot < minDot) minDot = dot;
        }
        if (minDot > maxSep) { maxSep = minDot; faceIdx = i; }
    }
    return maxSep;
}

static int clipSegment(Vec2 v1, Vec2 v2i, Vec2 n, float offset, Vec2* out) {
    int count = 0;
    float d1 = v2dot(n, v1) - offset;
    float d2 = v2dot(n, v2i) - offset;
    if (d1 <= 0) out[count++] = v1;
    if (d2 <= 0) out[count++] = v2i;
    if (d1 * d2 < 0) {
        float t = d1 / (d1 - d2);
        out[count++] = v2add(v1, v2scale(v2sub(v2i, v1), t));
    }
    return count;
}

static bool polyVsPoly(PhysicsSolverImpl* s, int a, int b, SolverContact& out) {
    int faceA, faceB;
    float penA = findMinSep(s, a, b, faceA);
    if (penA > 0) return false;
    float penB = findMinSep(s, b, a, faceB);
    if (penB > 0) return false;

    int refIdx, incIdx, refFace;
    bool flip;
    const float relTol = 0.95f, absTol = 0.01f;

    if (penB > penA * relTol + absTol) {
        refIdx = b; incIdx = a; refFace = faceB; flip = true;
    } else {
        refIdx = a; incIdx = b; refFace = faceA; flip = false;
    }

    int refBase = refIdx * MAX_POLY_VERTS;
    int incBase = incIdx * MAX_POLY_VERTS;
    float refAng = s->angle[refIdx], incAng = s->angle[incIdx];
    Vec2 refPos = {s->posX[refIdx], s->posY[refIdx]};
    Vec2 incPos = {s->posX[incIdx], s->posY[incIdx]};
    int incCount = s->polyCount[incIdx];

    // Reference face normal in world space
    Vec2 refNormal = v2rotate({s->polyNX[refBase+refFace], s->polyNY[refBase+refFace]}, refAng);

    // Find incident face
    int incFace = 0;
    float minDot = 1e18f;
    for (int i = 0; i < incCount; i++) {
        Vec2 n = v2rotate({s->polyNX[incBase+i], s->polyNY[incBase+i]}, incAng);
        float dot = v2dot(n, refNormal);
        if (dot < minDot) { minDot = dot; incFace = i; }
    }

    // Reference face vertices
    int refNext = (refFace+1) % s->polyCount[refIdx];
    Vec2 rv1 = v2add(refPos, v2rotate({s->polyVX[refBase+refFace], s->polyVY[refBase+refFace]}, refAng));
    Vec2 rv2 = v2add(refPos, v2rotate({s->polyVX[refBase+refNext], s->polyVY[refBase+refNext]}, refAng));

    // Incident edge vertices
    int incNext = (incFace+1) % incCount;
    Vec2 iv1 = v2add(incPos, v2rotate({s->polyVX[incBase+incFace], s->polyVY[incBase+incFace]}, incAng));
    Vec2 iv2 = v2add(incPos, v2rotate({s->polyVX[incBase+incNext], s->polyVY[incBase+incNext]}, incAng));

    // Clip
    Vec2 tangent = v2norm(v2sub(rv2, rv1));
    float negSide = -v2dot(tangent, rv1);
    float posSide = v2dot(tangent, rv2);

    Vec2 c1[2], c2[2];
    if (clipSegment(iv1, iv2, v2neg(tangent), negSide, c1) < 2) return false;
    if (clipSegment(c1[0], c1[1], tangent, posSide, c2) < 2) return false;

    float refDist = v2dot(refNormal, rv1);
    out.pointCount = 0;
    out.normal = flip ? v2neg(refNormal) : refNormal;

    for (int i = 0; i < 2; i++) {
        float sep = v2dot(refNormal, c2[i]) - refDist;
        if (sep <= 0 && out.pointCount < MAX_CONTACTS_PER_PAIR) {
            auto& p = out.points[out.pointCount++];
            p.position = c2[i];
            p.penetration = -sep;
        }
    }
    return out.pointCount > 0;
}

// ── Narrow Phase Dispatch ───────────────────────────────────

static bool narrowPhase(PhysicsSolverImpl* s, int a, int b, SolverContact& out) {
    int tA = s->shapeType[a], tB = s->shapeType[b];

    if (tA == SHAPE_CIRCLE && tB == SHAPE_CIRCLE)
        return circleVsCircle(s, a, b, out);
    if (tA == SHAPE_CIRCLE && tB == SHAPE_POLYGON)
        return circleVsPolygon(s, a, b, out, true);
    if (tA == SHAPE_POLYGON && tB == SHAPE_CIRCLE)
        return circleVsPolygon(s, b, a, out, false);
    if (tA == SHAPE_POLYGON && tB == SHAPE_POLYGON)
        return polyVsPoly(s, a, b, out);
    return false;
}

// ── Sequential Impulse Solver ───────────────────────────────

static const float BAUMGARTE = 0.2f;
static const float LINEAR_SLOP = 0.005f;
static const float MAX_CORRECTION = 0.2f;

static Vec2 relativeVelocity(PhysicsSolverImpl* s, int a, int b, Vec2 rA, Vec2 rB) {
    Vec2 vA = {s->velX[a], s->velY[a]};
    Vec2 vB = {s->velX[b], s->velY[b]};
    return v2sub(
        v2add(vB, v2cross_sv(s->angVel[b], rB)),
        v2add(vA, v2cross_sv(s->angVel[a], rA))
    );
}

static void preSolve(PhysicsSolverImpl* s, SolverContact& cc, float dt) {
    int a = cc.bodyA, b = cc.bodyB;
    float imA = s->invMass[a], imB = s->invMass[b];
    float iiA = s->invInertia[a], iiB = s->invInertia[b];
    cc.tangent = {cc.normal.y, -cc.normal.x}; // perp

    for (int i = 0; i < cc.pointCount; i++) {
        auto& p = cc.points[i];
        p.relA = v2sub(p.position, {s->posX[a], s->posY[a]});
        p.relB = v2sub(p.position, {s->posX[b], s->posY[b]});

        float rnA = v2cross(p.relA, cc.normal);
        float rnB = v2cross(p.relB, cc.normal);
        float kN = imA + imB + rnA*rnA*iiA + rnB*rnB*iiB;
        p.normalMass = kN > 0 ? 1.0f/kN : 0;

        float rtA = v2cross(p.relA, cc.tangent);
        float rtB = v2cross(p.relB, cc.tangent);
        float kT = imA + imB + rtA*rtA*iiA + rtB*rtB*iiB;
        p.tangentMass = kT > 0 ? 1.0f/kT : 0;

        Vec2 rv = relativeVelocity(s, a, b, p.relA, p.relB);
        float vn = v2dot(rv, cc.normal);
        p.velocityBias = vn < -1.0f ? -cc.restitution * vn : 0;

        // Warm start
        Vec2 impulse = v2add(v2scale(cc.normal, p.normalImpulse), v2scale(cc.tangent, p.tangentImpulse));
        s->velX[a] -= impulse.x * imA; s->velY[a] -= impulse.y * imA;
        s->angVel[a] -= v2cross(p.relA, impulse) * iiA;
        s->velX[b] += impulse.x * imB; s->velY[b] += impulse.y * imB;
        s->angVel[b] += v2cross(p.relB, impulse) * iiB;
    }
}

static void solveVelocity(PhysicsSolverImpl* s, SolverContact& cc) {
    int a = cc.bodyA, b = cc.bodyB;
    float imA = s->invMass[a], imB = s->invMass[b];
    float iiA = s->invInertia[a], iiB = s->invInertia[b];

    for (int i = 0; i < cc.pointCount; i++) {
        auto& p = cc.points[i];
        Vec2 rv = relativeVelocity(s, a, b, p.relA, p.relB);

        // Friction
        float vt = v2dot(rv, cc.tangent);
        float tLambda = -vt * p.tangentMass;
        float maxF = cc.friction * p.normalImpulse;
        float newT = std::clamp(p.tangentImpulse + tLambda, -maxF, maxF);
        tLambda = newT - p.tangentImpulse;
        p.tangentImpulse = newT;

        Vec2 fImp = v2scale(cc.tangent, tLambda);
        s->velX[a] -= fImp.x * imA; s->velY[a] -= fImp.y * imA;
        s->angVel[a] -= v2cross(p.relA, fImp) * iiA;
        s->velX[b] += fImp.x * imB; s->velY[b] += fImp.y * imB;
        s->angVel[b] += v2cross(p.relB, fImp) * iiB;

        // Normal
        rv = relativeVelocity(s, a, b, p.relA, p.relB);
        float vn = v2dot(rv, cc.normal);
        float nLambda = -(vn - p.velocityBias) * p.normalMass;
        float newN = std::max(p.normalImpulse + nLambda, 0.0f);
        nLambda = newN - p.normalImpulse;
        p.normalImpulse = newN;

        Vec2 nImp = v2scale(cc.normal, nLambda);
        s->velX[a] -= nImp.x * imA; s->velY[a] -= nImp.y * imA;
        s->angVel[a] -= v2cross(p.relA, nImp) * iiA;
        s->velX[b] += nImp.x * imB; s->velY[b] += nImp.y * imB;
        s->angVel[b] += v2cross(p.relB, nImp) * iiB;
    }
}

static bool solvePosition(PhysicsSolverImpl* s, SolverContact& cc) {
    int a = cc.bodyA, b = cc.bodyB;
    float imA = s->invMass[a], imB = s->invMass[b];
    float iiA = s->invInertia[a], iiB = s->invInertia[b];
    float minSep = 0;

    for (int i = 0; i < cc.pointCount; i++) {
        auto& p = cc.points[i];
        Vec2 rA = v2sub(p.position, {s->posX[a], s->posY[a]});
        Vec2 rB = v2sub(p.position, {s->posX[b], s->posY[b]});
        Vec2 diff = v2sub(v2add({s->posX[b], s->posY[b]}, rB), v2add({s->posX[a], s->posY[a]}, rA));
        float sep = v2dot(diff, cc.normal) - p.penetration;
        if (sep < minSep) minSep = sep;

        float correction = std::clamp(BAUMGARTE * (sep + LINEAR_SLOP), -MAX_CORRECTION, 0.0f);
        float rnA = v2cross(rA, cc.normal);
        float rnB = v2cross(rB, cc.normal);
        float kN = imA + imB + rnA*rnA*iiA + rnB*rnB*iiB;
        float impulse = kN > 0 ? -correction / kN : 0;

        s->posX[a] -= cc.normal.x * impulse * imA;
        s->posY[a] -= cc.normal.y * impulse * imA;
        s->angle[a] -= v2cross(rA, v2scale(cc.normal, impulse)) * iiA;
        s->posX[b] += cc.normal.x * impulse * imB;
        s->posY[b] += cc.normal.y * impulse * imB;
        s->angle[b] += v2cross(rB, v2scale(cc.normal, impulse)) * iiB;
    }
    return minSep >= -3.0f * LINEAR_SLOP;
}

// ── API Implementation ──────────────────────────────────────

PHYSICS_API PhysicsSolverHandle physics_create(int capacity, float cellSize) {
    auto* s = new PhysicsSolverImpl();
    s->bodyCapacity = 0;
    s->bodyCount = 0;
    s->cellSize = cellSize;
    s->invCellSize = 1.0f / cellSize;
    s->ensureCapacity(capacity);
    return s;
}

PHYSICS_API void physics_destroy(PhysicsSolverHandle solver) {
    delete solver;
}

PHYSICS_API void physics_upload_bodies(
    PhysicsSolverHandle s, int count,
    const float* px, const float* py, const float* ang,
    const float* vx, const float* vy, const float* av,
    const float* im, const float* ii,
    const float* fr, const float* re,
    const float* aminx, const float* aminy, const float* amaxx, const float* amaxy,
    const int* st,
    const float* cr, const float* ccx, const float* ccy,
    const float* pvx, const float* pvy, const float* pnx, const float* pny,
    const int* pc,
    const int* bt, const int* isl, const int* itr
) {
    s->ensureCapacity(count);
    s->bodyCount = count;
    memcpy(s->posX.data(), px, count*sizeof(float));
    memcpy(s->posY.data(), py, count*sizeof(float));
    memcpy(s->angle.data(), ang, count*sizeof(float));
    memcpy(s->velX.data(), vx, count*sizeof(float));
    memcpy(s->velY.data(), vy, count*sizeof(float));
    memcpy(s->angVel.data(), av, count*sizeof(float));
    memcpy(s->invMass.data(), im, count*sizeof(float));
    memcpy(s->invInertia.data(), ii, count*sizeof(float));
    memcpy(s->frict.data(), fr, count*sizeof(float));
    memcpy(s->restit.data(), re, count*sizeof(float));
    memcpy(s->aabbMinX.data(), aminx, count*sizeof(float));
    memcpy(s->aabbMinY.data(), aminy, count*sizeof(float));
    memcpy(s->aabbMaxX.data(), amaxx, count*sizeof(float));
    memcpy(s->aabbMaxY.data(), amaxy, count*sizeof(float));
    memcpy(s->shapeType.data(), st, count*sizeof(int));
    memcpy(s->circleR.data(), cr, count*sizeof(float));
    memcpy(s->circleCX.data(), ccx, count*sizeof(float));
    memcpy(s->circleCY.data(), ccy, count*sizeof(float));
    memcpy(s->polyVX.data(), pvx, count*MAX_POLY_VERTS*sizeof(float));
    memcpy(s->polyVY.data(), pvy, count*MAX_POLY_VERTS*sizeof(float));
    memcpy(s->polyNX.data(), pnx, count*MAX_POLY_VERTS*sizeof(float));
    memcpy(s->polyNY.data(), pny, count*MAX_POLY_VERTS*sizeof(float));
    memcpy(s->polyCount.data(), pc, count*sizeof(int));
    memcpy(s->bodyType.data(), bt, count*sizeof(int));
    memcpy(s->isSleeping.data(), isl, count*sizeof(int));
    memcpy(s->isTrigger.data(), itr, count*sizeof(int));
}

PHYSICS_API void physics_solve(
    PhysicsSolverHandle s,
    float dt,
    float gx, float gy,
    int velIters, int posIters
) {
    int n = s->bodyCount;

    // 1. Integrate velocities
    for (int i = 0; i < n; i++) {
        if (s->bodyType[i] != 1 || s->isSleeping[i]) continue; // dynamic only
        s->velX[i] += gx * dt;
        s->velY[i] += gy * dt;
    }

    // 2. Broad phase
    std::vector<std::pair<int,int>> pairs;
    broadPhase(s, pairs);

    // 3. Narrow phase
    s->contacts.clear();
    for (auto& [a, b] : pairs) {
        SolverContact cc = {};
        cc.bodyA = a; cc.bodyB = b;
        cc.friction = sqrtf(s->frict[a] * s->frict[b]);
        cc.restitution = std::max(s->restit[a], s->restit[b]);
        cc.isTrigger = s->isTrigger[a] || s->isTrigger[b];

        if (narrowPhase(s, a, b, cc)) {
            s->contacts.push_back(cc);
        }
    }

    // 4. Pre-solve
    for (auto& cc : s->contacts) {
        if (!cc.isTrigger) preSolve(s, cc, dt);
    }

    // 5. Velocity iterations
    for (int iter = 0; iter < velIters; iter++) {
        for (auto& cc : s->contacts) {
            if (!cc.isTrigger) solveVelocity(s, cc);
        }
    }

    // 6. Integrate positions
    for (int i = 0; i < n; i++) {
        if (s->bodyType[i] == 0 || s->isSleeping[i]) continue;
        s->posX[i] += s->velX[i] * dt;
        s->posY[i] += s->velY[i] * dt;
        s->angle[i] += s->angVel[i] * dt;
    }

    // 7. Position iterations
    for (int iter = 0; iter < posIters; iter++) {
        bool allSolved = true;
        for (auto& cc : s->contacts) {
            if (!cc.isTrigger) {
                if (!solvePosition(s, cc)) allSolved = false;
            }
        }
        if (allSolved) break;
    }
}

PHYSICS_API void physics_download_bodies(
    PhysicsSolverHandle s,
    float* px, float* py, float* ang,
    float* vx, float* vy, float* av
) {
    int n = s->bodyCount;
    memcpy(px, s->posX.data(), n*sizeof(float));
    memcpy(py, s->posY.data(), n*sizeof(float));
    memcpy(ang, s->angle.data(), n*sizeof(float));
    memcpy(vx, s->velX.data(), n*sizeof(float));
    memcpy(vy, s->velY.data(), n*sizeof(float));
    memcpy(av, s->angVel.data(), n*sizeof(float));
}

PHYSICS_API int physics_contact_count(PhysicsSolverHandle s) {
    return (int)s->contacts.size();
}

PHYSICS_API int physics_download_contacts(
    PhysicsSolverHandle s,
    ContactNative* out, int maxContacts
) {
    int count = std::min((int)s->contacts.size(), maxContacts);
    for (int i = 0; i < count; i++) {
        auto& cc = s->contacts[i];
        out[i].body_a = cc.bodyA;
        out[i].body_b = cc.bodyB;
        out[i].normal_x = cc.normal.x;
        out[i].normal_y = cc.normal.y;
        out[i].point_count = cc.pointCount;
        for (int j = 0; j < cc.pointCount; j++) {
            out[i].point_x[j] = cc.points[j].position.x;
            out[i].point_y[j] = cc.points[j].position.y;
            out[i].penetration[j] = cc.points[j].penetration;
            out[i].normal_impulse[j] = cc.points[j].normalImpulse;
            out[i].tangent_impulse[j] = cc.points[j].tangentImpulse;
        }
    }
    return count;
}
