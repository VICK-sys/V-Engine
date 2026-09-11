using System;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Shapes;

namespace VEngine.Engine.Physics.Collision;

/// <summary>
/// Narrow-phase collision detection between shape pairs.
/// Produces contact manifolds with normals pointing from A to B.
/// </summary>
public static class CollisionDetection
{
    /// <summary>
    /// Detect collision between two shapes at the given transforms.
    /// Returns a manifold with 0 contacts if no collision.
    /// </summary>
    public static Manifold Detect(
        Shape shapeA, Vec2 posA, float angleA,
        Shape shapeB, Vec2 posB, float angleB)
    {
        // Dispatch by shape type pair
        if (shapeA.Type == ShapeType.Circle && shapeB.Type == ShapeType.Circle)
            return CircleVsCircle((CircleShape)shapeA, posA, angleA, (CircleShape)shapeB, posB, angleB);

        if (shapeA.Type == ShapeType.Circle && shapeB.Type == ShapeType.Polygon)
        {
            // CircleVsPolygon returns normal pointing poly→circle (B→A). Negate for A→B.
            var m = CircleVsPolygon((CircleShape)shapeA, posA, angleA, (PolygonShape)shapeB, posB, angleB);
            m.Normal = -m.Normal;
            return m;
        }

        if (shapeA.Type == ShapeType.Polygon && shapeB.Type == ShapeType.Circle)
        {
            // CircleVsPolygon returns normal pointing poly→circle (A→B). Already correct.
            return CircleVsPolygon((CircleShape)shapeB, posB, angleB, (PolygonShape)shapeA, posA, angleA);
        }

        if (shapeA.Type == ShapeType.Polygon && shapeB.Type == ShapeType.Polygon)
            return PolygonVsPolygon((PolygonShape)shapeA, posA, angleA, (PolygonShape)shapeB, posB, angleB);

        return default;
    }

    // ── Circle vs Circle ──

    private static Manifold CircleVsCircle(
        CircleShape a, Vec2 posA, float angleA,
        CircleShape b, Vec2 posB, float angleB)
    {
        var centerA = posA + a.Center.Rotate(angleA);
        var centerB = posB + b.Center.Rotate(angleB);

        var d = centerB - centerA;
        float distSq = d.LengthSquared();
        float radiusSum = a.Radius + b.Radius;

        if (distSq > radiusSum * radiusSum)
            return default; // No collision

        float dist = MathF.Sqrt(distSq);
        var manifold = new Manifold { ContactCount = 1 };

        if (dist < 1e-6f)
        {
            // Circles are at same position — arbitrary normal
            manifold.Normal = new Vec2(0, 1);
            manifold.Contact0 = new ContactPoint
            {
                Position = centerA,
                Penetration = radiusSum
            };
        }
        else
        {
            manifold.Normal = d / dist;
            manifold.Contact0 = new ContactPoint
            {
                Position = centerA + manifold.Normal * a.Radius,
                Penetration = radiusSum - dist
            };
        }

        return manifold;
    }

    // ── Circle vs Polygon ──

    private static Manifold CircleVsPolygon(
        CircleShape circle, Vec2 posC, float angleC,
        PolygonShape poly, Vec2 posP, float angleP)
    {
        // Transform circle center into polygon's local space
        var worldCenter = posC + circle.Center.Rotate(angleC);
        var rot = new Mat2x2(angleP);
        var rotT = rot.Transpose();
        var localCenter = rotT.Multiply(worldCenter - posP);

        // Find the edge with minimum separation
        int normalIndex = 0;
        float separation = float.MinValue;

        for (int i = 0; i < poly.Count; i++)
        {
            float s = Vec2.Dot(poly.Normals[i], localCenter - poly.Vertices[i]);
            if (s > circle.Radius)
                return default;

            if (s > separation)
            {
                separation = s;
                normalIndex = i;
            }
        }

        // Determine closest feature: vertex v1, vertex v2, or face
        int v1 = normalIndex;
        int v2 = (normalIndex + 1) % poly.Count;
        var edge = poly.Vertices[v2] - poly.Vertices[v1];
        float u1 = Vec2.Dot(localCenter - poly.Vertices[v1], edge);
        float u2 = Vec2.Dot(localCenter - poly.Vertices[v2], -edge);

        Vec2 normal;
        float penetration;

        if (u1 <= 0)
        {
            var d = localCenter - poly.Vertices[v1];
            float distSq = d.LengthSquared();
            if (distSq > circle.Radius * circle.Radius) return default;
            float dist = MathF.Sqrt(distSq);
            normal = dist > 1e-6f ? rot.Multiply(d / dist) : rot.Multiply(poly.Normals[normalIndex]);
            penetration = circle.Radius - dist;
        }
        else if (u2 <= 0)
        {
            var d = localCenter - poly.Vertices[v2];
            float distSq = d.LengthSquared();
            if (distSq > circle.Radius * circle.Radius) return default;
            float dist = MathF.Sqrt(distSq);
            normal = dist > 1e-6f ? rot.Multiply(d / dist) : rot.Multiply(poly.Normals[normalIndex]);
            penetration = circle.Radius - dist;
        }
        else
        {
            // Face region — normal is the face normal, distance is separation
            normal = rot.Multiply(poly.Normals[normalIndex]);
            penetration = circle.Radius - separation;
        }

        return new Manifold
        {
            Normal = normal,
            ContactCount = 1,
            Contact0 = new ContactPoint
            {
                Position = worldCenter - normal * circle.Radius,
                Penetration = penetration
            }
        };
    }

    // ── Polygon vs Polygon (SAT) ──

    private static Manifold PolygonVsPolygon(
        PolygonShape polyA, Vec2 posA, float angleA,
        PolygonShape polyB, Vec2 posB, float angleB)
    {
        // Find the axis of minimum penetration on each polygon
        float penA = FindMinSeparation(polyA, posA, angleA, polyB, posB, angleB, out int faceA);
        if (penA > 0) return default;

        float penB = FindMinSeparation(polyB, posB, angleB, polyA, posA, angleA, out int faceB);
        if (penB > 0) return default;

        // Choose the reference polygon (the one with smaller penetration = less overlap)
        PolygonShape refPoly, incPoly;
        Vec2 refPos, incPos;
        float refAngle, incAngle;
        int refFace;
        bool flip;

        // Bias toward polyA to avoid face oscillation
        const float relativeTol = 0.95f;
        const float absoluteTol = 0.01f;

        if (penB > penA * relativeTol + absoluteTol)
        {
            refPoly = polyB; refPos = posB; refAngle = angleB; refFace = faceB;
            incPoly = polyA; incPos = posA; incAngle = angleA;
            flip = true;
        }
        else
        {
            refPoly = polyA; refPos = posA; refAngle = angleA; refFace = faceA;
            incPoly = polyB; incPos = posB; incAngle = angleB;
            flip = false;
        }

        // Find the incident edge on the incident polygon
        var refNormalWorld = refPoly.GetWorldNormal(refFace, refAngle);
        int incFace = FindIncidentFace(incPoly, incPos, incAngle, refNormalWorld);

        // Reference face vertices in world space
        var rv1 = refPoly.GetWorldVertex(refFace, refPos, refAngle);
        var rv2 = refPoly.GetWorldVertex((refFace + 1) % refPoly.Count, refPos, refAngle);

        // Incident edge vertices in world space
        var iv1 = incPoly.GetWorldVertex(incFace, incPos, incAngle);
        var iv2 = incPoly.GetWorldVertex((incFace + 1) % incPoly.Count, incPos, incAngle);

        // Clip incident edge against reference face side planes
        var tangent = (rv2 - rv1).Normalized();
        float negSide = -Vec2.Dot(tangent, rv1);
        float posSide = Vec2.Dot(tangent, rv2);

        // Clip against first side plane
        int np = ClipSegment(iv1, iv2, -tangent, negSide, out var c1a, out var c1b);
        if (np < 2) return default;

        // Clip against second side plane
        np = ClipSegment(c1a, c1b, tangent, posSide, out var c2a, out var c2b);
        if (np < 2) return default;

        // Filter by reference face (only keep points behind it)
        float refFaceDist = Vec2.Dot(refNormalWorld, rv1);
        var manifold = new Manifold
        {
            Normal = flip ? -refNormalWorld : refNormalWorld,
            ContactCount = 0
        };

        float sep0 = Vec2.Dot(refNormalWorld, c2a) - refFaceDist;
        if (sep0 <= 0)
        {
            var cp = new ContactPoint { Position = c2a, Penetration = -sep0 };
            if (manifold.ContactCount == 0) manifold.Contact0 = cp;
            else manifold.Contact1 = cp;
            manifold.ContactCount++;
        }

        float sep1 = Vec2.Dot(refNormalWorld, c2b) - refFaceDist;
        if (sep1 <= 0)
        {
            var cp = new ContactPoint { Position = c2b, Penetration = -sep1 };
            if (manifold.ContactCount == 0) manifold.Contact0 = cp;
            else manifold.Contact1 = cp;
            manifold.ContactCount++;
        }

        return manifold;
    }

    // ── SAT Helpers ──

    /// <summary>
    /// Find the axis of minimum penetration between polyA's faces and polyB.
    /// Returns the maximum separation (negative = overlapping). Sets faceIndex.
    /// </summary>
    private static float FindMinSeparation(
        PolygonShape polyA, Vec2 posA, float angleA,
        PolygonShape polyB, Vec2 posB, float angleB,
        out int faceIndex)
    {
        faceIndex = 0;
        float maxSep = float.MinValue;

        for (int i = 0; i < polyA.Count; i++)
        {
            // Get face normal and vertex in world space
            var normal = polyA.GetWorldNormal(i, angleA);
            var vertex = polyA.GetWorldVertex(i, posA, angleA);

            // Find the most negative support point on polyB along this normal
            float minDot = float.MaxValue;
            for (int j = 0; j < polyB.Count; j++)
            {
                var bv = polyB.GetWorldVertex(j, posB, angleB);
                float dot = Vec2.Dot(normal, bv - vertex);
                if (dot < minDot) minDot = dot;
            }

            if (minDot > maxSep)
            {
                maxSep = minDot;
                faceIndex = i;
            }
        }

        return maxSep;
    }

    /// <summary>Find the edge on incPoly most anti-aligned with the reference normal.</summary>
    private static int FindIncidentFace(PolygonShape incPoly, Vec2 pos, float angle, Vec2 refNormal)
    {
        int face = 0;
        float minDot = float.MaxValue;

        for (int i = 0; i < incPoly.Count; i++)
        {
            var n = incPoly.GetWorldNormal(i, angle);
            float dot = Vec2.Dot(n, refNormal);
            if (dot < minDot)
            {
                minDot = dot;
                face = i;
            }
        }

        return face;
    }

    /// <summary>Clip a line segment against a plane (keep points where dot(n,p) - offset &lt;= 0).</summary>
    private static int ClipSegment(Vec2 v1, Vec2 v2, Vec2 normal, float offset,
                                   out Vec2 out1, out Vec2 out2)
    {
        out1 = out2 = Vec2.Zero;
        int count = 0;

        float d1 = Vec2.Dot(normal, v1) - offset;
        float d2 = Vec2.Dot(normal, v2) - offset;

        // Keep points behind or on the plane
        if (d1 <= 0) { if (count == 0) out1 = v1; else out2 = v1; count++; }
        if (d2 <= 0) { if (count == 0) out1 = v2; else out2 = v2; count++; }

        // If they're on different sides, compute intersection
        if (d1 * d2 < 0)
        {
            float t = d1 / (d1 - d2);
            var intersection = v1 + (v2 - v1) * t;
            if (count == 0) out1 = intersection; else out2 = intersection;
            count++;
        }

        return count;
    }
}
