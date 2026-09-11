using System;
using System.Collections.Generic;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Collision;
using VEngine.Engine.Physics.Shapes;

namespace VEngine.Engine.Physics.Queries;

/// <summary>
/// Ray and region query algorithms for physics shapes.
/// </summary>
internal static class PhysicsQuery
{
    /// <summary>
    /// Cast a ray against a circle shape. Returns fraction (0-1) or -1 if no hit.
    /// </summary>
    public static float RayVsCircle(Vec2 origin, Vec2 dir, float maxDist,
        CircleShape circle, Vec2 bodyPos, float bodyAngle,
        out Vec2 hitPoint, out Vec2 hitNormal)
    {
        hitPoint = hitNormal = Vec2.Zero;

        var center = bodyPos + circle.Center.Rotate(bodyAngle);
        var oc = origin - center;

        float a = Vec2.Dot(dir, dir);
        float b = 2f * Vec2.Dot(oc, dir);
        float c = Vec2.Dot(oc, oc) - circle.Radius * circle.Radius;

        float discriminant = b * b - 4f * a * c;
        if (discriminant < 0) return -1f;

        float sqrtD = MathF.Sqrt(discriminant);
        float t = (-b - sqrtD) / (2f * a);

        // If t < 0, ray starts inside — try the exit point
        if (t < 0) t = (-b + sqrtD) / (2f * a);
        if (t < 0 || t > maxDist) return -1f;

        float fraction = t / maxDist;
        hitPoint = origin + dir * t;
        hitNormal = (hitPoint - center).Normalized();
        return fraction;
    }

    /// <summary>
    /// Cast a ray against a polygon shape. Returns fraction (0-1) or -1 if no hit.
    /// </summary>
    public static float RayVsPolygon(Vec2 origin, Vec2 dir, float maxDist,
        PolygonShape poly, Vec2 bodyPos, float bodyAngle,
        out Vec2 hitPoint, out Vec2 hitNormal)
    {
        hitPoint = hitNormal = Vec2.Zero;

        // Transform ray into polygon local space
        var rot = new Mat2x2(bodyAngle);
        var rotT = rot.Transpose();
        var localOrigin = rotT.Multiply(origin - bodyPos);
        var localDir = rotT.Multiply(dir);

        float tMin = 0f;
        float tMax = maxDist;
        int hitIndex = -1;

        for (int i = 0; i < poly.Count; i++)
        {
            var normal = poly.Normals[i];
            float num = Vec2.Dot(normal, poly.Vertices[i] - localOrigin);
            float den = Vec2.Dot(normal, localDir);

            if (MathF.Abs(den) < 1e-8f)
            {
                // Ray parallel to edge — check if outside
                if (num < 0) return -1f;
                continue;
            }

            float t = num / den;

            if (den < 0)
            {
                // Entry
                if (t > tMin) { tMin = t; hitIndex = i; }
            }
            else
            {
                // Exit
                if (t < tMax) tMax = t;
            }

            if (tMin > tMax) return -1f;
        }

        if (hitIndex < 0 || tMin < 0 || tMin > maxDist) return -1f;

        float fraction = tMin / maxDist;
        hitPoint = origin + dir * tMin;
        hitNormal = rot.Multiply(poly.Normals[hitIndex]);
        return fraction;
    }

    /// <summary>
    /// Test if an AABB overlaps a ray segment.
    /// </summary>
    public static bool RayVsAABB(Vec2 origin, Vec2 dir, float maxDist, PhysicsAABB aabb)
    {
        float tMin = 0f;
        float tMax = maxDist;

        for (int axis = 0; axis < 2; axis++)
        {
            float o = axis == 0 ? origin.X : origin.Y;
            float d = axis == 0 ? dir.X : dir.Y;
            float lo = axis == 0 ? aabb.Min.X : aabb.Min.Y;
            float hi = axis == 0 ? aabb.Max.X : aabb.Max.Y;

            if (MathF.Abs(d) < 1e-8f)
            {
                if (o < lo || o > hi) return false;
                continue;
            }

            float invD = 1f / d;
            float t1 = (lo - o) * invD;
            float t2 = (hi - o) * invD;
            if (t1 > t2) (t1, t2) = (t2, t1);

            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);

            if (tMin > tMax) return false;
        }

        return true;
    }
}
