using System;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Shapes;

namespace VEngine.Engine.Physics.Collision;

/// <summary>
/// Time of Impact calculation for continuous collision detection.
/// Used for IsBullet bodies to prevent tunneling through thin objects.
/// </summary>
internal static class TOI
{
    /// <summary>
    /// Compute the time of impact between a moving body and a static/slow body.
    /// Returns a fraction (0-1) along the bullet's movement, or 1 if no hit.
    /// Uses conservative advancement with binary search refinement.
    /// </summary>
    public static float ComputeTOI(
        RigidBody bullet, Vec2 displacement,
        RigidBody other)
    {
        if (bullet.Shape == null || other.Shape == null) return 1f;

        // Compute swept AABB for the bullet
        var startAABB = bullet.Shape.ComputeAABB(bullet.Position, bullet.Angle);
        var endPos = bullet.Position + displacement;
        var endAABB = bullet.Shape.ComputeAABB(endPos, bullet.Angle);
        var sweptAABB = PhysicsAABB.Merge(startAABB, endAABB);

        // Quick check: does swept AABB overlap the other body's AABB?
        if (!sweptAABB.Overlaps(other.AABB)) return 1f;

        // Binary search for TOI
        float tLo = 0f;
        float tHi = 1f;
        const int maxIterations = 20;
        const float tolerance = 0.001f;

        for (int i = 0; i < maxIterations; i++)
        {
            float tMid = (tLo + tHi) * 0.5f;
            var testPos = bullet.Position + displacement * tMid;

            var manifold = CollisionDetection.Detect(
                bullet.Shape, testPos, bullet.Angle,
                other.Shape, other.Position, other.Angle);

            if (manifold.ContactCount > 0)
            {
                tHi = tMid; // Collision at tMid — search earlier
            }
            else
            {
                tLo = tMid; // No collision — search later
            }

            if (tHi - tLo < tolerance) break;
        }

        return tLo;
    }
}
