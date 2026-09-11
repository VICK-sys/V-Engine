using System;
using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Joints;

/// <summary>
/// Rigid connection: constrains two bodies to maintain fixed relative position and angle.
/// </summary>
public class WeldJoint : Joint
{
    private float _referenceAngle;

    // Solver (2D point constraint + 1D angle constraint = 3 DOF)
    private Vec2 _linearImpulse;
    private float _angularImpulse;
    private float _k00, _k01, _k10, _k11;
    private float _angularMass;

    public WeldJoint(RigidBody bodyA, RigidBody bodyB, Vec2 worldAnchor)
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

        _k00 = mA + mB + rA.Y * rA.Y * iA + rB.Y * rB.Y * iB;
        _k01 = -rA.X * rA.Y * iA - rB.X * rB.Y * iB;
        _k10 = _k01;
        _k11 = mA + mB + rA.X * rA.X * iA + rB.X * rB.X * iB;
        _angularMass = iA + iB > 0 ? 1f / (iA + iB) : 0;

        // Warm start linear
        BodyA.LinearVelocity -= _linearImpulse * mA;
        BodyA.AngularVelocity -= (Vec2.Cross(rA, _linearImpulse) + _angularImpulse) * iA;
        BodyB.LinearVelocity += _linearImpulse * mB;
        BodyB.AngularVelocity += (Vec2.Cross(rB, _linearImpulse) + _angularImpulse) * iB;
    }

    internal override void SolveVelocityConstraints(float dt)
    {
        var rA = WorldAnchorA - BodyA.Position;
        var rB = WorldAnchorB - BodyB!.Position;

        // Angular constraint
        float cdotAngular = BodyB.AngularVelocity - BodyA.AngularVelocity;
        float angLambda = -_angularMass * cdotAngular;
        _angularImpulse += angLambda;
        BodyA.AngularVelocity -= angLambda * BodyA.InvInertia;
        BodyB.AngularVelocity += angLambda * BodyB.InvInertia;

        // Linear constraint
        var vA = BodyA.LinearVelocity + Vec2.Cross(BodyA.AngularVelocity, rA);
        var vB = BodyB.LinearVelocity + Vec2.Cross(BodyB.AngularVelocity, rB);
        var cdot = vB - vA;

        float det = _k00 * _k11 - _k01 * _k10;
        if (MathF.Abs(det) < 1e-10f) return;
        float invDet = 1f / det;
        var lambda = new Vec2(
            -(_k11 * cdot.X - _k01 * cdot.Y) * invDet,
            -(_k00 * cdot.Y - _k10 * cdot.X) * invDet
        );
        _linearImpulse += lambda;

        BodyA.LinearVelocity -= lambda * BodyA.InvMass;
        BodyA.AngularVelocity -= Vec2.Cross(rA, lambda) * BodyA.InvInertia;
        BodyB.LinearVelocity += lambda * BodyB.InvMass;
        BodyB.AngularVelocity += Vec2.Cross(rB, lambda) * BodyB.InvInertia;
    }

    internal override bool SolvePositionConstraints()
    {
        var pA = WorldAnchorA;
        var pB = WorldAnchorB;
        var posError = pB - pA;
        float angError = BodyB!.Angle - BodyA.Angle - _referenceAngle;

        var rA = pA - BodyA.Position;
        var rB = pB - BodyB.Position;
        float mA = BodyA.InvMass, mB = BodyB.InvMass;
        float iA = BodyA.InvInertia, iB = BodyB.InvInertia;

        // Angular correction
        float angMass = iA + iB > 0 ? 1f / (iA + iB) : 0;
        float angImp = -angMass * angError;
        BodyA.Angle -= angImp * iA;
        BodyB.Angle += angImp * iB;

        // Linear correction
        float k00 = mA + mB + rA.Y * rA.Y * iA + rB.Y * rB.Y * iB;
        float k01 = -rA.X * rA.Y * iA - rB.X * rB.Y * iB;
        float k11 = mA + mB + rA.X * rA.X * iA + rB.X * rB.X * iB;
        float det = k00 * k11 - k01 * k01;
        if (MathF.Abs(det) < 1e-10f) return true;
        float invDet = 1f / det;
        var imp = new Vec2(
            -(k11 * posError.X - k01 * posError.Y) * invDet,
            -(k00 * posError.Y - k01 * posError.X) * invDet
        );

        BodyA.Position -= imp * mA;
        BodyA.Angle -= Vec2.Cross(rA, imp) * iA;
        BodyB.Position += imp * mB;
        BodyB.Angle += Vec2.Cross(rB, imp) * iB;

        return posError.LengthSquared() < 0.005f * 0.005f && MathF.Abs(angError) < 0.01f;
    }
}
