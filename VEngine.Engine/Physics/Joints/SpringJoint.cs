using System;
using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Joints;

/// <summary>
/// Soft distance constraint with spring-damper behavior.
/// Uses frequency (Hz) and damping ratio (0-1) instead of stiffness/damping.
/// </summary>
public class SpringJoint : Joint
{
    /// <summary>Rest length in pixels.</summary>
    public float Length;

    /// <summary>Spring frequency in Hz. Higher = stiffer. Default 4.</summary>
    public float Frequency = 4f;

    /// <summary>Damping ratio. 0 = no damping, 1 = critical. Default 0.5.</summary>
    public float DampingRatio = 0.5f;

    // Solver
    private Vec2 _u;
    private float _mass;
    private float _impulse;
    private float _gamma;
    private float _bias;

    public SpringJoint(RigidBody bodyA, RigidBody bodyB, Vec2 localAnchorA, Vec2 localAnchorB, float length)
    {
        BodyA = bodyA;
        BodyB = bodyB;
        LocalAnchorA = localAnchorA;
        LocalAnchorB = localAnchorB;
        Length = length;
    }

    /// <summary>Create from world anchors.</summary>
    public static SpringJoint Create(RigidBody bodyA, RigidBody bodyB, Vec2 worldAnchorA, Vec2 worldAnchorB)
    {
        var localA = bodyA.GetTransform().ToLocal(worldAnchorA);
        var localB = bodyB.GetTransform().ToLocal(worldAnchorB);
        float len = Vec2.Distance(worldAnchorA, worldAnchorB);
        if (len < 1e-4f) len = 1e-4f; // Prevent zero-length NaN
        return new SpringJoint(bodyA, bodyB, localA, localB, len);
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

        // Soft constraint parameters (use mass for stiffness, not invMass)
        float mass = invMass > 0 ? 1f / invMass : 0;
        float omega = 2f * MathF.PI * Frequency;
        float d = 2f * mass * DampingRatio * omega;
        float k = mass * omega * omega;

        _gamma = dt * (d + dt * k);
        _gamma = _gamma > 0 ? 1f / _gamma : 0;
        _bias = (dist - Length) * dt * k * _gamma;

        float totalMass = invMass + _gamma;
        _mass = totalMass > 0 ? 1f / totalMass : 0;

        // Warm start
        _impulse *= 0.95f; // Scale down for stability with soft constraints
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
        float lambda = -_mass * (cdot + _bias + _gamma * _impulse);
        _impulse += lambda;

        var impulse = _u * lambda;
        BodyA.LinearVelocity -= impulse * BodyA.InvMass;
        BodyA.AngularVelocity -= Vec2.Cross(rA, impulse) * BodyA.InvInertia;
        BodyB.LinearVelocity += impulse * BodyB.InvMass;
        BodyB.AngularVelocity += Vec2.Cross(rB, impulse) * BodyB.InvInertia;
    }

    internal override bool SolvePositionConstraints()
    {
        // Soft constraints handle position through velocity bias, no position correction needed
        return true;
    }
}
