using System;
using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Joints;

/// <summary>
/// Slider joint: constrains movement to a single axis. Bodies can slide along the axis
/// but not perpendicular to it.
/// </summary>
public class PrismaticJoint : Joint
{
    /// <summary>Local-space axis on body A that defines the slide direction.</summary>
    public Vec2 LocalAxis;

    // Solver
    private Vec2 _perp; // world perpendicular to axis
    private float _perpMass;
    private float _perpImpulse;
    private float _referenceAngle;

    public PrismaticJoint(RigidBody bodyA, RigidBody bodyB, Vec2 worldAnchor, Vec2 worldAxis)
    {
        BodyA = bodyA;
        BodyB = bodyB;
        LocalAnchorA = bodyA.GetTransform().ToLocal(worldAnchor);
        LocalAnchorB = bodyB.GetTransform().ToLocal(worldAnchor);
        // Store axis in A's local space
        var rotT = new Mat2x2(bodyA.Angle).Transpose();
        LocalAxis = rotT.Multiply(worldAxis.Normalized());
        _referenceAngle = bodyB.Angle - bodyA.Angle;
    }

    internal override void InitVelocityConstraints(float dt)
    {
        // World axis and perpendicular
        var worldAxis = new Mat2x2(BodyA.Angle).Multiply(LocalAxis);
        _perp = worldAxis.Perpendicular();

        var rA = WorldAnchorA - BodyA.Position;
        var rB = WorldAnchorB - BodyB!.Position;

        float mA = BodyA.InvMass, mB = BodyB.InvMass;
        float iA = BodyA.InvInertia, iB = BodyB.InvInertia;

        float s1 = Vec2.Cross(rA, _perp);
        float s2 = Vec2.Cross(rB, _perp);
        float k = mA + mB + s1 * s1 * iA + s2 * s2 * iB;
        _perpMass = k > 0 ? 1f / k : 0;

        // Warm start
        var impulse = _perp * _perpImpulse;
        BodyA.LinearVelocity -= impulse * mA;
        BodyA.AngularVelocity -= s1 * _perpImpulse * iA;
        BodyB.LinearVelocity += impulse * mB;
        BodyB.AngularVelocity += s2 * _perpImpulse * iB;
    }

    internal override void SolveVelocityConstraints(float dt)
    {
        var rA = WorldAnchorA - BodyA.Position;
        var rB = WorldAnchorB - BodyB!.Position;

        var vA = BodyA.LinearVelocity + Vec2.Cross(BodyA.AngularVelocity, rA);
        var vB = BodyB.LinearVelocity + Vec2.Cross(BodyB.AngularVelocity, rB);

        float cdot = Vec2.Dot(_perp, vB - vA);
        float lambda = -_perpMass * cdot;
        _perpImpulse += lambda;

        float s1 = Vec2.Cross(rA, _perp);
        float s2 = Vec2.Cross(rB, _perp);

        var impulse = _perp * lambda;
        BodyA.LinearVelocity -= impulse * BodyA.InvMass;
        BodyA.AngularVelocity -= s1 * lambda * BodyA.InvInertia;
        BodyB.LinearVelocity += impulse * BodyB.InvMass;
        BodyB.AngularVelocity += s2 * lambda * BodyB.InvInertia;
    }

    internal override bool SolvePositionConstraints()
    {
        var worldAxis = new Mat2x2(BodyA.Angle).Multiply(LocalAxis);
        var perp = worldAxis.Perpendicular();

        var rA = WorldAnchorA - BodyA.Position;
        var rB = WorldAnchorB - BodyB!.Position;
        var d = (BodyB.Position + rB) - (BodyA.Position + rA);

        float error = Vec2.Dot(perp, d);

        float s1 = Vec2.Cross(rA, perp);
        float s2 = Vec2.Cross(rB, perp);
        float k = BodyA.InvMass + BodyB.InvMass
                + s1 * s1 * BodyA.InvInertia + s2 * s2 * BodyB.InvInertia;
        float impulse = k > 0 ? -error / k : 0;

        var correction = perp * impulse;
        BodyA.Position -= correction * BodyA.InvMass;
        BodyA.Angle -= s1 * impulse * BodyA.InvInertia;
        BodyB.Position += correction * BodyB.InvMass;
        BodyB.Angle += s2 * impulse * BodyB.InvInertia;

        return MathF.Abs(error) < 0.005f;
    }
}
