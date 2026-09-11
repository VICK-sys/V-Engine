using System;
using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Joints;

/// <summary>
/// Constrains two bodies to maintain a fixed distance between their anchor points.
/// </summary>
public class DistanceJoint : Joint
{
    /// <summary>Target distance between anchors in pixels.</summary>
    public float Length;

    // Solver data
    private Vec2 _u;
    private float _mass;
    private float _impulse;

    public DistanceJoint(RigidBody bodyA, RigidBody bodyB, Vec2 localAnchorA, Vec2 localAnchorB, float length)
    {
        BodyA = bodyA;
        BodyB = bodyB;
        LocalAnchorA = localAnchorA;
        LocalAnchorB = localAnchorB;
        Length = length;
    }

    /// <summary>Create from world-space anchors, auto-computing local anchors and length.</summary>
    public static DistanceJoint Create(RigidBody bodyA, RigidBody bodyB, Vec2 worldAnchorA, Vec2 worldAnchorB)
    {
        var localA = bodyA.GetTransform().ToLocal(worldAnchorA);
        var localB = bodyB.GetTransform().ToLocal(worldAnchorB);
        float length = Vec2.Distance(worldAnchorA, worldAnchorB);
        if (length < 1e-4f) length = 1e-4f; // Prevent zero-length NaN
        return new DistanceJoint(bodyA, bodyB, localA, localB, length);
    }

    internal override void InitVelocityConstraints(float dt)
    {
        var pA = WorldAnchorA;
        var pB = WorldAnchorB;
        _u = pB - pA;
        float dist = _u.Length();
        _u = dist > 1e-6f ? _u / dist : new Vec2(0, 1);

        var rA = pA - BodyA.Position;
        var rB = pB - BodyB!.Position;

        float crA = Vec2.Cross(rA, _u);
        float crB = Vec2.Cross(rB, _u);
        float invMass = BodyA.InvMass + BodyB.InvMass
                      + crA * crA * BodyA.InvInertia
                      + crB * crB * BodyB.InvInertia;
        _mass = invMass > 0 ? 1f / invMass : 0;

        // Warm start
        var impulse = _u * _impulse;
        BodyA.LinearVelocity -= impulse * BodyA.InvMass;
        BodyA.AngularVelocity -= Vec2.Cross(rA, impulse) * BodyA.InvInertia;
        BodyB.LinearVelocity += impulse * BodyB.InvMass;
        BodyB.AngularVelocity += Vec2.Cross(rB, impulse) * BodyB.InvInertia;
    }

    internal override void SolveVelocityConstraints(float dt)
    {
        var rA = WorldAnchorA - BodyA.Position;
        var rB = WorldAnchorB - BodyB!.Position;

        var vA = BodyA.LinearVelocity + Vec2.Cross(BodyA.AngularVelocity, rA);
        var vB = BodyB.LinearVelocity + Vec2.Cross(BodyB.AngularVelocity, rB);

        float cdot = Vec2.Dot(_u, vB - vA);
        float lambda = -_mass * cdot;
        _impulse += lambda;

        var impulse = _u * lambda;
        BodyA.LinearVelocity -= impulse * BodyA.InvMass;
        BodyA.AngularVelocity -= Vec2.Cross(rA, impulse) * BodyA.InvInertia;
        BodyB.LinearVelocity += impulse * BodyB.InvMass;
        BodyB.AngularVelocity += Vec2.Cross(rB, impulse) * BodyB.InvInertia;
    }

    internal override bool SolvePositionConstraints()
    {
        var pA = WorldAnchorA;
        var pB = WorldAnchorB;
        var d = pB - pA;
        float dist = d.Length();
        var u = dist > 1e-6f ? d / dist : new Vec2(0, 1);
        float error = dist - Length;

        var rA = pA - BodyA.Position;
        var rB = pB - BodyB!.Position;

        float crA = Vec2.Cross(rA, u);
        float crB = Vec2.Cross(rB, u);
        float invMass = BodyA.InvMass + BodyB.InvMass
                      + crA * crA * BodyA.InvInertia
                      + crB * crB * BodyB.InvInertia;
        float impulse = invMass > 0 ? -error / invMass : 0;

        var correction = u * impulse;
        BodyA.Position -= correction * BodyA.InvMass;
        BodyA.Angle -= Vec2.Cross(rA, correction) * BodyA.InvInertia;
        BodyB.Position += correction * BodyB.InvMass;
        BodyB.Angle += Vec2.Cross(rB, correction) * BodyB.InvInertia;

        return MathF.Abs(error) < 0.005f;
    }
}
