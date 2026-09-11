using System;
using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Joints;

/// <summary>
/// Hinge joint: constrains two bodies to share a common anchor point.
/// Allows free rotation. Optional motor and angle limits.
/// </summary>
public class RevoluteJoint : Joint
{
    /// <summary>Enable angle limits.</summary>
    public bool EnableLimit;

    /// <summary>Lower angle limit in radians.</summary>
    public float LowerLimit;

    /// <summary>Upper angle limit in radians.</summary>
    public float UpperLimit;

    /// <summary>Enable motor.</summary>
    public bool EnableMotor;

    /// <summary>Target motor speed in radians/second.</summary>
    public float MotorSpeed;

    /// <summary>Maximum motor torque.</summary>
    public float MaxMotorTorque = 1000f;

    // Solver
    private Vec2 _impulse;
    private float _motorImpulse;
    private float _referenceAngle;
    private float _k00, _k01, _k10, _k11; // 2x2 mass matrix

    public RevoluteJoint(RigidBody bodyA, RigidBody bodyB, Vec2 worldAnchor)
    {
        BodyA = bodyA;
        BodyB = bodyB;
        LocalAnchorA = bodyA.GetTransform().ToLocal(worldAnchor);
        LocalAnchorB = bodyB.GetTransform().ToLocal(worldAnchor);
        _referenceAngle = bodyB.Angle - bodyA.Angle;
    }

    internal override void InitVelocityConstraints(float dt)
    {
        var rA = WorldAnchorA - BodyA.Position;
        var rB = WorldAnchorB - BodyB!.Position;

        float mA = BodyA.InvMass, mB = BodyB.InvMass;
        float iA = BodyA.InvInertia, iB = BodyB.InvInertia;

        // 2x2 effective mass for point constraint
        _k00 = mA + mB + rA.Y * rA.Y * iA + rB.Y * rB.Y * iB;
        _k01 = -rA.X * rA.Y * iA - rB.X * rB.Y * iB;
        _k10 = _k01;
        _k11 = mA + mB + rA.X * rA.X * iA + rB.X * rB.X * iB;

        // Warm start
        BodyA.LinearVelocity -= _impulse * mA;
        BodyA.AngularVelocity -= Vec2.Cross(rA, _impulse) * iA;
        BodyB.LinearVelocity += _impulse * mB;
        BodyB.AngularVelocity += Vec2.Cross(rB, _impulse) * iB;
    }

    internal override void SolveVelocityConstraints(float dt)
    {
        var rA = WorldAnchorA - BodyA.Position;
        var rB = WorldAnchorB - BodyB!.Position;

        // Motor
        if (EnableMotor)
        {
            float cdot = BodyB.AngularVelocity - BodyA.AngularVelocity - MotorSpeed;
            float motorMass = BodyA.InvInertia + BodyB.InvInertia;
            float impulse = motorMass > 0 ? -cdot / motorMass : 0;
            float oldImpulse = _motorImpulse;
            _motorImpulse = System.Math.Clamp(oldImpulse + impulse, -MaxMotorTorque * dt, MaxMotorTorque * dt);
            impulse = _motorImpulse - oldImpulse;
            BodyA.AngularVelocity -= impulse * BodyA.InvInertia;
            BodyB.AngularVelocity += impulse * BodyB.InvInertia;
        }

        // Point constraint
        var vA = BodyA.LinearVelocity + Vec2.Cross(BodyA.AngularVelocity, rA);
        var vB = BodyB.LinearVelocity + Vec2.Cross(BodyB.AngularVelocity, rB);
        var cdotVec = vB - vA;

        // Solve 2x2: K * impulse = -cdot
        var lambda = Solve2x2(-cdotVec.X, -cdotVec.Y);
        _impulse += lambda;

        BodyA.LinearVelocity -= lambda * BodyA.InvMass;
        BodyA.AngularVelocity -= Vec2.Cross(rA, lambda) * BodyA.InvInertia;
        BodyB.LinearVelocity += lambda * BodyB.InvMass;
        BodyB.AngularVelocity += Vec2.Cross(rB, lambda) * BodyB.InvInertia;
    }

    internal override bool SolvePositionConstraints()
    {
        var pA = WorldAnchorA;
        var pB = WorldAnchorB;
        var error = pB - pA;

        var rA = pA - BodyA.Position;
        var rB = pB - BodyB!.Position;

        float mA = BodyA.InvMass, mB = BodyB.InvMass;
        float iA = BodyA.InvInertia, iB = BodyB.InvInertia;

        float k00 = mA + mB + rA.Y * rA.Y * iA + rB.Y * rB.Y * iB;
        float k01 = -rA.X * rA.Y * iA - rB.X * rB.Y * iB;
        float k11 = mA + mB + rA.X * rA.X * iA + rB.X * rB.X * iB;

        // Solve 2x2 for position correction
        float det = k00 * k11 - k01 * k01;
        if (MathF.Abs(det) < 1e-10f) return true;
        float invDet = 1f / det;
        var impulse = new Vec2(
            -(k11 * error.X - k01 * error.Y) * invDet,
            -(k00 * error.Y - k01 * error.X) * invDet
        );

        BodyA.Position -= impulse * mA;
        BodyA.Angle -= Vec2.Cross(rA, impulse) * iA;
        BodyB.Position += impulse * mB;
        BodyB.Angle += Vec2.Cross(rB, impulse) * iB;

        return error.LengthSquared() < 0.005f * 0.005f;
    }

    private Vec2 Solve2x2(float bx, float by)
    {
        float det = _k00 * _k11 - _k01 * _k10;
        if (MathF.Abs(det) < 1e-10f) return Vec2.Zero;
        float invDet = 1f / det;
        return new Vec2(
            (_k11 * bx - _k01 * by) * invDet,
            (_k00 * by - _k10 * bx) * invDet
        );
    }
}
