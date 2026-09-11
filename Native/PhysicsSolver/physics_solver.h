#pragma once

#ifdef _WIN32
#ifdef PHYSICS_EXPORTS
#define PHYSICS_API __declspec(dllexport)
#else
#define PHYSICS_API __declspec(dllimport)
#endif
#else
#define PHYSICS_API __attribute__((visibility("default")))
#endif

struct ContactVector { float x, y; };
struct ContactBody {
    ContactVector position;
    float angle;
    ContactVector velocity;
    float angularVelocity, invMass, invInertia;
};
struct ContactPoint {
    ContactVector relativeA, relativeB;
    float normalMass, tangentMass, velocityBias, normalImpulse, tangentImpulse;
    ContactVector position;
    float penetration;
};
struct ContactState {
    ContactBody a, b;
    ContactVector normal, tangent;
    float friction, restitution;
    ContactPoint points[2];
    int count;
};

extern "C" {
PHYSICS_API void physics_contact_presolve(ContactState* state);
PHYSICS_API void physics_contact_velocity(ContactState* state);
PHYSICS_API int physics_contact_position(ContactState* state);
}
