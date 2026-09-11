#include "physics_solver.h"
#include <algorithm>

static ContactVector operator+(ContactVector a, ContactVector b) { return {a.x + b.x, a.y + b.y}; }
static ContactVector operator-(ContactVector a, ContactVector b) { return {a.x - b.x, a.y - b.y}; }
static ContactVector operator*(ContactVector a, float b) { return {a.x * b, a.y * b}; }
static float dot(ContactVector a, ContactVector b) { return a.x * b.x + a.y * b.y; }
static float cross(ContactVector a, ContactVector b) { return a.x * b.y - a.y * b.x; }
static ContactVector cross(float a, ContactVector b) { return {-a * b.y, a * b.x}; }

static ContactVector relative(const ContactState& s, const ContactPoint& p) {
    return (s.b.velocity + cross(s.b.angularVelocity, p.relativeB)) -
        (s.a.velocity + cross(s.a.angularVelocity, p.relativeA));
}

static void impulse(ContactState& s, const ContactPoint& p, ContactVector value) {
    s.a.velocity = s.a.velocity - value * s.a.invMass;
    s.a.angularVelocity -= cross(p.relativeA, value) * s.a.invInertia;
    s.b.velocity = s.b.velocity + value * s.b.invMass;
    s.b.angularVelocity += cross(p.relativeB, value) * s.b.invInertia;
}

PHYSICS_API void physics_contact_presolve(ContactState* state) {
    auto& s = *state;
    s.tangent = {s.normal.y, -s.normal.x};
    for (int i = 0; i < s.count; i++) {
        auto& p = s.points[i];
        p.relativeA = p.position - s.a.position;
        p.relativeB = p.position - s.b.position;
        float rnA = cross(p.relativeA, s.normal), rnB = cross(p.relativeB, s.normal);
        float kn = s.a.invMass + s.b.invMass + rnA * rnA * s.a.invInertia + rnB * rnB * s.b.invInertia;
        p.normalMass = kn > 0 ? 1.0f / kn : 0;
        float rtA = cross(p.relativeA, s.tangent), rtB = cross(p.relativeB, s.tangent);
        float kt = s.a.invMass + s.b.invMass + rtA * rtA * s.a.invInertia + rtB * rtB * s.b.invInertia;
        p.tangentMass = kt > 0 ? 1.0f / kt : 0;
        float vn = dot(relative(s, p), s.normal);
        p.velocityBias = vn < -1 ? -s.restitution * vn : 0;
        impulse(s, p, s.normal * p.normalImpulse + s.tangent * p.tangentImpulse);
    }
}

PHYSICS_API void physics_contact_velocity(ContactState* state) {
    auto& s = *state;
    for (int i = 0; i < s.count; i++) {
        auto& p = s.points[i];
        float vt = dot(relative(s, p), s.tangent);
        float limit = s.friction * p.normalImpulse;
        float tangent = std::clamp(p.tangentImpulse - vt * p.tangentMass, -limit, limit);
        float tangentDelta = tangent - p.tangentImpulse;
        p.tangentImpulse = tangent;
        impulse(s, p, s.tangent * tangentDelta);
        float vn = dot(relative(s, p), s.normal);
        float normal = std::max(p.normalImpulse - (vn - p.velocityBias) * p.normalMass, 0.0f);
        float normalDelta = normal - p.normalImpulse;
        p.normalImpulse = normal;
        impulse(s, p, s.normal * normalDelta);
    }
}

PHYSICS_API int physics_contact_position(ContactState* state) {
    auto& s = *state;
    float minimum = 0;
    for (int i = 0; i < s.count; i++) {
        auto& p = s.points[i];
        auto ra = p.position - s.a.position;
        auto rb = p.position - s.b.position;
        float separation = dot((s.b.position + rb) - (s.a.position + ra), s.normal) - p.penetration;
        minimum = std::min(minimum, separation);
        float correction = std::clamp(0.2f * (separation + 0.005f), -0.2f, 0.0f);
        float rnA = cross(ra, s.normal), rnB = cross(rb, s.normal);
        float k = s.a.invMass + s.b.invMass + rnA * rnA * s.a.invInertia + rnB * rnB * s.b.invInertia;
        float value = k > 0 ? -correction / k : 0;
        s.a.position = s.a.position - s.normal * (value * s.a.invMass);
        s.a.angle -= cross(ra, s.normal * value) * s.a.invInertia;
        s.b.position = s.b.position + s.normal * (value * s.b.invMass);
        s.b.angle += cross(rb, s.normal * value) * s.b.invInertia;
    }
    return minimum >= -3.0f * 0.005f;
}
