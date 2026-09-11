using System;
using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Joints;

/// <summary>
/// Drags a body toward a target point with spring-damper behavior.
/// Useful for mouse/touch interaction. Only uses BodyA (no BodyB).
/// </summary>
public class MouseJoint : Joint
{
    /// <summary>World-space target point the body is pulled toward.</summary>
    public Vec2 Target;

    /// <summary>Maximum force applied to reach target. Default 10000.</summary>
    public float MaxForce = 10000f;

    /// <summary>Spring frequency in Hz. Default 5.</summary>
    public float Frequency = 5f;

    /// <summary>Damping ratio. Default 0.7.</summary>
    public float DampingRatio = 0.7f;

    // Solver
    private Vec2 _impulse;
    private float _gamma;
    private Vec2 _bias;
    private float _mass00, _mass01, _mass10, _mass11;

    public MouseJoint(RigidBody body, Vec2 target)
    {
        BodyA = body;
        Target = target;
        // Anchor at body center — the body is pulled from its center toward the target
        LocalAnchorA = Vec2.Zero;
    }

    internal override void InitVelocityConstraints(float dt)
    {
        var rA = WorldAnchorA - BodyA.Position;
        float mA = BodyA.InvMass;
        float iA = BodyA.InvInertia;
        float mass = mA > 0 ? 1f / mA : 0;

        // Soft constraint (use mass, not invMass, for stiffness)
        float omega = 2f * MathF.PI * Frequency;
        float d = 2f * mass * DampingRatio * omega;
        float k = mass * omega * omega;

        _gamma = dt * (d + dt * k);
        _gamma = _gamma > 0 ? 1f / _gamma : 0;

        var posError = WorldAnchorA - Target;
        _bias = posError * (dt * k * _gamma);

        // Effective mass
        float k00 = mA + rA.Y * rA.Y * iA + _gamma;
        float k01 = -rA.X * rA.Y * iA;
        float k11 = mA + rA.X * rA.X * iA + _gamma;

        float det = k00 * k11 - k01 * k01;
        if (MathF.Abs(det) > 1e-10f)
        {
            float invDet = 1f / det;
            _mass00 = k11 * invDet;
            _mass01 = -k01 * invDet;
            _mass10 = -k01 * invDet;
            _mass11 = k00 * invDet;
        }

        _impulse *= 0.95f;
        BodyA.LinearVelocity += _impulse * mA;
        BodyA.AngularVelocity += Vec2.Cross(rA, _impulse) * iA;
    }

    internal override void SolveVelocityConstraints(float dt)
    {
        var rA = WorldAnchorA - BodyA.Position;
        var vA = BodyA.LinearVelocity + Vec2.Cross(BodyA.AngularVelocity, rA);

        var cdot = vA + _bias + _impulse * _gamma;
        var lambda = new Vec2(
            -(_mass00 * cdot.X + _mass01 * cdot.Y),
            -(_mass10 * cdot.X + _mass11 * cdot.Y)
        );

        var oldImpulse = _impulse;
        _impulse += lambda;

        // Clamp to max force
        float maxImpulse = MaxForce * dt;
        if (_impulse.LengthSquared() > maxImpulse * maxImpulse)
            _impulse = _impulse.Normalized() * maxImpulse;

        lambda = _impulse - oldImpulse;

        BodyA.LinearVelocity += lambda * BodyA.InvMass;
        BodyA.AngularVelocity += Vec2.Cross(rA, lambda) * BodyA.InvInertia;
    }

    internal override bool SolvePositionConstraints()
    {
        return true; // Soft constraint — handled in velocity
    }
}
