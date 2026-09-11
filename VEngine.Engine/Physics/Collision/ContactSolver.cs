using System;
using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Collision;

/// <summary>
/// Sequential impulse constraint solver (Erin Catto's method).
/// Resolves contacts with friction, restitution, and warm starting.
/// </summary>
internal static class ContactSolver
{
    private const float BaumgarteScale = 0.2f;
    private const float LinearSlop = 0.005f;
    private const float MaxLinearCorrection = 0.2f;

    /// <summary>
    /// Pre-solve: compute effective masses, velocity biases, and apply warm starting impulses.
    /// </summary>
    public static void PreSolve(ContactConstraint cc, float dt)
    {
        var bodyA = cc.BodyA;
        var bodyB = cc.BodyB;
        float invMassA = bodyA.InvMass, invMassB = bodyB.InvMass;
        float invInertiaA = bodyA.InvInertia, invInertiaB = bodyB.InvInertia;

        cc.Tangent = Vec2.Cross(cc.Normal, 1f);

        for (int i = 0; i < cc.PointCount; i++)
        {
            ref var cp = ref (i == 0 ? ref cc.Point0 : ref cc.Point1);

            cp.RelativeA = cp.Position - bodyA.Position;
            cp.RelativeB = cp.Position - bodyB.Position;

            // Effective mass along normal: 1 / (1/mA + 1/mB + (rA×n)²/IA + (rB×n)²/IB)
            float rnA = Vec2.Cross(cp.RelativeA, cc.Normal);
            float rnB = Vec2.Cross(cp.RelativeB, cc.Normal);
            float kNormal = invMassA + invMassB + rnA * rnA * invInertiaA + rnB * rnB * invInertiaB;
            cp.NormalMass = kNormal > 0 ? 1f / kNormal : 0f;

            // Effective mass along tangent
            float rtA = Vec2.Cross(cp.RelativeA, cc.Tangent);
            float rtB = Vec2.Cross(cp.RelativeB, cc.Tangent);
            float kTangent = invMassA + invMassB + rtA * rtA * invInertiaA + rtB * rtB * invInertiaB;
            cp.TangentMass = kTangent > 0 ? 1f / kTangent : 0f;

            // Velocity bias for restitution
            var relVel = ComputeRelativeVelocity(bodyA, bodyB, cp.RelativeA, cp.RelativeB);
            float vn = Vec2.Dot(relVel, cc.Normal);
            cp.VelocityBias = 0;
            if (vn < -1f) // Only apply restitution for approaching contacts above threshold
                cp.VelocityBias = -cc.Restitution * vn;

            // Warm start: apply accumulated impulses from previous frame
            var warmImpulse = cc.Normal * cp.NormalImpulse + cc.Tangent * cp.TangentImpulse;
            bodyA.LinearVelocity -= warmImpulse * invMassA;
            bodyA.AngularVelocity -= Vec2.Cross(cp.RelativeA, warmImpulse) * invInertiaA;
            bodyB.LinearVelocity += warmImpulse * invMassB;
            bodyB.AngularVelocity += Vec2.Cross(cp.RelativeB, warmImpulse) * invInertiaB;
        }
    }

    /// <summary>
    /// Solve velocity constraints: apply normal and friction impulses.
    /// </summary>
    public static void SolveVelocity(ContactConstraint cc)
    {
        var bodyA = cc.BodyA;
        var bodyB = cc.BodyB;
        float invMassA = bodyA.InvMass, invMassB = bodyB.InvMass;
        float invInertiaA = bodyA.InvInertia, invInertiaB = bodyB.InvInertia;

        for (int i = 0; i < cc.PointCount; i++)
        {
            ref var cp = ref (i == 0 ? ref cc.Point0 : ref cc.Point1);

            // Relative velocity at contact point
            var relVel = ComputeRelativeVelocity(bodyA, bodyB, cp.RelativeA, cp.RelativeB);

            // ── Friction (tangent) impulse ──
            float vt = Vec2.Dot(relVel, cc.Tangent);
            float tangentLambda = -vt * cp.TangentMass;

            // Coulomb friction clamp: |friction impulse| <= µ * normal impulse
            float maxFriction = cc.Friction * cp.NormalImpulse;
            float newTangent = System.Math.Clamp(cp.TangentImpulse + tangentLambda, -maxFriction, maxFriction);
            tangentLambda = newTangent - cp.TangentImpulse;
            cp.TangentImpulse = newTangent;

            var frictionImpulse = cc.Tangent * tangentLambda;
            bodyA.LinearVelocity -= frictionImpulse * invMassA;
            bodyA.AngularVelocity -= Vec2.Cross(cp.RelativeA, frictionImpulse) * invInertiaA;
            bodyB.LinearVelocity += frictionImpulse * invMassB;
            bodyB.AngularVelocity += Vec2.Cross(cp.RelativeB, frictionImpulse) * invInertiaB;

            // ── Normal impulse ──
            relVel = ComputeRelativeVelocity(bodyA, bodyB, cp.RelativeA, cp.RelativeB);
            float vn = Vec2.Dot(relVel, cc.Normal);
            float normalLambda = -(vn - cp.VelocityBias) * cp.NormalMass;

            // Accumulate and clamp (normal impulse must be non-negative — can only push, not pull)
            float newNormal = MathF.Max(cp.NormalImpulse + normalLambda, 0);
            normalLambda = newNormal - cp.NormalImpulse;
            cp.NormalImpulse = newNormal;

            var normalImpulse = cc.Normal * normalLambda;
            bodyA.LinearVelocity -= normalImpulse * invMassA;
            bodyA.AngularVelocity -= Vec2.Cross(cp.RelativeA, normalImpulse) * invInertiaA;
            bodyB.LinearVelocity += normalImpulse * invMassB;
            bodyB.AngularVelocity += Vec2.Cross(cp.RelativeB, normalImpulse) * invInertiaB;
        }
    }

    /// <summary>
    /// Solve position constraints: push overlapping bodies apart (Baumgarte stabilization).
    /// Returns true if all contacts are within tolerance.
    /// </summary>
    public static bool SolvePosition(ContactConstraint cc)
    {
        var bodyA = cc.BodyA;
        var bodyB = cc.BodyB;
        float invMassA = bodyA.InvMass, invMassB = bodyB.InvMass;
        float invInertiaA = bodyA.InvInertia, invInertiaB = bodyB.InvInertia;

        float minSeparation = 0;

        for (int i = 0; i < cc.PointCount; i++)
        {
            ref var cp = ref (i == 0 ? ref cc.Point0 : ref cc.Point1);

            // Recompute relative vectors with current positions
            var rA = cp.Position - bodyA.Position;
            var rB = cp.Position - bodyB.Position;

            // Compute current separation along normal
            var diff = (bodyB.Position + rB) - (bodyA.Position + rA);
            float separation = Vec2.Dot(diff, cc.Normal) - cp.Penetration;

            minSeparation = MathF.Min(minSeparation, separation);

            // Only correct if overlapping beyond slop
            float correction = System.Math.Clamp(BaumgarteScale * (separation + LinearSlop), -MaxLinearCorrection, 0);

            // Effective mass for position correction
            float rnA = Vec2.Cross(rA, cc.Normal);
            float rnB = Vec2.Cross(rB, cc.Normal);
            float kNormal = invMassA + invMassB + rnA * rnA * invInertiaA + rnB * rnB * invInertiaB;

            float impulse = kNormal > 0 ? -correction / kNormal : 0;

            bodyA.Position -= cc.Normal * (impulse * invMassA);
            bodyA.Angle -= Vec2.Cross(rA, cc.Normal * impulse) * invInertiaA;
            bodyB.Position += cc.Normal * (impulse * invMassB);
            bodyB.Angle += Vec2.Cross(rB, cc.Normal * impulse) * invInertiaB;
        }

        return minSeparation >= -3f * LinearSlop;
    }

    private static Vec2 ComputeRelativeVelocity(RigidBody a, RigidBody b, Vec2 rA, Vec2 rB)
    {
        // v_relative = (vB + ωB×rB) - (vA + ωA×rA)
        return (b.LinearVelocity + Vec2.Cross(b.AngularVelocity, rB))
             - (a.LinearVelocity + Vec2.Cross(a.AngularVelocity, rA));
    }
}
