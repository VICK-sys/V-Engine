using System;
using System.Collections.Generic;

namespace VEngine.Engine.Core;

/// <summary>
/// Direction flags returned by collision checks.
/// </summary>
[Flags]
public enum CollisionDir
{
    None = 0,
    Top = 1,     // entity hit the top of the solid (landed on it)
    Bottom = 2,  // entity hit the bottom of the solid (bonked head)
    Left = 4,    // entity hit the left side of the solid
    Right = 8    // entity hit the right side of the solid
}

/// <summary>
/// Static collision helpers for AABB separation between entities.
/// </summary>
public static class Collision
{
    /// <summary>
    /// Tiny overlap kept after separation so surfaces never land at exactly 0 penetration.
    /// Prevents jitter from floating-point imprecision.
    /// </summary>
    public static float Skin { get; set; } = 0.1f;

    /// <summary>
    /// Minimum penetration threshold for one-way platforms.
    /// Entities moving faster than this (in pixels/frame) can still land on platforms.
    /// </summary>
    public static float OnewayMinPen { get; set; } = 8f;

    /// <summary>
    /// Extra penetration margin added to velocity-based one-way threshold.
    /// Accounts for acceleration within a single frame.
    /// </summary>
    public static float OnewayMargin { get; set; } = 4f;

    /// <summary>
    /// Check for collision without modifying either entity.
    /// Returns the direction and separation vector needed to resolve the overlap.
    /// Use this when you need to inspect the collision before deciding what to do.
    /// </summary>
    public static (CollisionDir Direction, float PushX, float PushY) Check(Entity a, Entity b)
    {
        var ab = a.GetCollisionBounds();
        var bb = b.GetCollisionBounds();

        float leftPen = (ab.X + ab.W) - bb.X;
        float rightPen = (bb.X + bb.W) - ab.X;
        float topPen = (ab.Y + ab.H) - bb.Y;
        float bottomPen = (bb.Y + bb.H) - ab.Y;

        if (leftPen <= 0 || rightPen <= 0 || topPen <= 0 || bottomPen <= 0)
            return (CollisionDir.None, 0, 0);

        float overlapX = leftPen < rightPen ? -leftPen : rightPen;
        float overlapY = topPen < bottomPen ? -topPen : bottomPen;

        if (MathF.Abs(overlapX) < MathF.Abs(overlapY))
        {
            float pushX = overlapX < 0 ? overlapX + Skin : overlapX - Skin;
            return (overlapX < 0 ? CollisionDir.Left : CollisionDir.Right, pushX, 0);
        }
        else
        {
            float pushY = overlapY < 0 ? overlapY + Skin : overlapY - Skin;
            return (overlapY < 0 ? CollisionDir.Top : CollisionDir.Bottom, 0, pushY);
        }
    }

    /// <summary>
    /// Separate a moving entity from a solid entity using AABB collision.
    /// Pushes the moving entity out along the axis of least penetration.
    /// Sets velocity to 0 on the collision axis.
    /// Returns which side of the solid was hit.
    /// </summary>
    public static CollisionDir Separate(Entity moving, Entity solid)
    {
        var a = moving.GetCollisionBounds();
        var b = solid.GetCollisionBounds();

        float leftPen = (a.X + a.W) - b.X;
        float rightPen = (b.X + b.W) - a.X;
        float topPen = (a.Y + a.H) - b.Y;
        float bottomPen = (b.Y + b.H) - a.Y;

        // No overlap
        if (leftPen <= 0 || rightPen <= 0 || topPen <= 0 || bottomPen <= 0)
            return CollisionDir.None;

        // Find smallest penetration axis
        float overlapX = leftPen < rightPen ? -leftPen : rightPen;
        float overlapY = topPen < bottomPen ? -topPen : bottomPen;

        float pushX = 0, pushY = 0;
        CollisionDir dir;

        if (MathF.Abs(overlapX) < MathF.Abs(overlapY))
        {
            pushX = overlapX < 0 ? overlapX + Skin : overlapX - Skin;
            dir = overlapX < 0 ? CollisionDir.Left : CollisionDir.Right;
        }
        else
        {
            pushY = overlapY < 0 ? overlapY + Skin : overlapY - Skin;
            dir = overlapY < 0 ? CollisionDir.Top : CollisionDir.Bottom;
        }

        moving.Position.X += pushX;
        moving.Position.Y += pushY;

        // Zero velocity on collision axis if runtime type is kinematic
        // (handles the case where a KinematicEntity is passed through an Entity-typed reference)
        if (moving is KinematicEntity k)
        {
            if (pushX != 0) k.Velocity.X = 0;
            if (pushY != 0) k.Velocity.Y = 0;
        }

        return dir;
    }

    /// <summary>
    /// Separate a kinematic entity from a solid. Pushes out and zeros velocity on the collision axis.
    /// </summary>
    public static CollisionDir Separate(KinematicEntity moving, Entity solid)
    {
        var result = Check(moving, solid);
        if (result.Direction == CollisionDir.None) return CollisionDir.None;

        moving.Position.X += result.PushX;
        moving.Position.Y += result.PushY;
        if (result.PushX != 0) moving.Velocity.X = 0;
        if (result.PushY != 0) moving.Velocity.Y = 0;

        return result.Direction;
    }

    /// <summary>
    /// One-way platform collision. Only blocks the entity from falling through the top.
    /// The entity can jump up through the platform and walk through from the sides.
    /// </summary>
    public static bool SeparateOneway(Entity moving, Entity platform)
    {
        return SeparateOnewayCore(moving, platform, 0);
    }

    /// <summary>
    /// One-way platform collision for kinematic entities. Checks downward velocity
    /// and zeros it on landing.
    /// </summary>
    public static bool SeparateOneway(KinematicEntity moving, Entity platform)
    {
        if (moving.Velocity.Y < 0) return false;
        if (!SeparateOnewayCore(moving, platform, moving.Velocity.Y)) return false;
        moving.Velocity.Y = 0;
        return true;
    }

    private static bool SeparateOnewayCore(Entity moving, Entity platform, float velY)
    {
        var a = moving.GetCollisionBounds();
        var b = platform.GetCollisionBounds();

        if (a.X + a.W <= b.X || a.X >= b.X + b.W) return false;

        float feetY = a.Y + a.H;
        float platTop = b.Y;
        float maxPen = MathF.Max(OnewayMinPen, MathF.Abs(velY) * (float)Eng.Game.FixedDeltaTime + OnewayMargin);
        float pen = feetY - platTop;

        if (pen > 0 && pen < maxPen)
        {
            moving.Position.Y -= (pen - Skin);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Collide a moving entity against all entities in a Group.
    /// Returns combined collision directions.
    /// </summary>
    public static CollisionDir SeparateGroup(Entity moving, Group solids, bool oneWay = false)
    {
        var result = CollisionDir.None;
        // Use runtime dispatch so KinematicEntity overload is called when appropriate
        bool isKinematic = moving is KinematicEntity;
        foreach (var solid in solids.Members)
        {
            if (!solid.Active) continue;
            if (oneWay)
            {
                if (isKinematic
                    ? SeparateOneway((KinematicEntity)moving, solid)
                    : SeparateOneway(moving, solid))
                    result |= CollisionDir.Top;
            }
            else
            {
                result |= isKinematic
                    ? Separate((KinematicEntity)moving, solid)
                    : Separate(moving, solid);
            }
        }
        return result;
    }

    /// <summary>
    /// Collide a moving entity against a list of entities.
    /// Returns combined collision directions.
    /// </summary>
    public static CollisionDir SeparateList(Entity moving, IReadOnlyList<Entity> solids, bool oneWay = false)
    {
        var result = CollisionDir.None;
        bool isKinematic = moving is KinematicEntity;
        for (int i = 0; i < solids.Count; i++)
        {
            if (!solids[i].Active) continue;
            if (oneWay)
            {
                if (isKinematic
                    ? SeparateOneway((KinematicEntity)moving, solids[i])
                    : SeparateOneway(moving, solids[i]))
                    result |= CollisionDir.Top;
            }
            else
            {
                result |= isKinematic
                    ? Separate((KinematicEntity)moving, solids[i])
                    : Separate(moving, solids[i]);
            }
        }
        return result;
    }

    /// <summary>
    /// Check overlap between two groups, calling a callback for each pair that overlaps.
    /// Useful for bullets-vs-enemies, coins-vs-player, etc.
    /// </summary>
    public static void OverlapGroup(Group groupA, Group groupB, Action<Entity, Entity> onOverlap)
    {
        foreach (var a in groupA.Members)
        {
            if (!a.Active) continue;
            foreach (var b in groupB.Members)
            {
                if (!b.Active) continue;
                if (a.Overlaps(b))
                    onOverlap(a, b);
            }
        }
    }

    /// <summary>
    /// Check overlap between an entity and a group, calling a callback for each overlap.
    /// </summary>
    public static void OverlapGroup(Entity entity, Group group, Action<Entity, Entity> onOverlap)
    {
        if (!entity.Active) return;
        foreach (var b in group.Members)
        {
            if (!b.Active) continue;
            if (entity.Overlaps(b))
                onOverlap(entity, b);
        }
    }

    // ── Continuous Collision Detection ─────────────────────────

    /// <summary>
    /// Sweep an AABB along a velocity vector and test against a static AABB.
    /// Returns the time of impact (0-1) or 1 if no collision within the sweep.
    /// Use for fast-moving projectiles to prevent tunneling through thin walls.
    /// </summary>
    public static float Sweep(Entity moving, Entity solid, float velX, float velY, out CollisionDir hitDir)
    {
        hitDir = CollisionDir.None;
        var a = moving.GetCollisionBounds();
        var b = solid.GetCollisionBounds();

        // Compute entry and exit distances on each axis
        float xEntry, yEntry, xExit, yExit;

        if (velX > 0)
        {
            xEntry = b.X - (a.X + a.W);
            xExit = (b.X + b.W) - a.X;
        }
        else if (velX < 0)
        {
            xEntry = (b.X + b.W) - a.X;
            xExit = b.X - (a.X + a.W);
        }
        else
        {
            xEntry = float.NegativeInfinity;
            xExit = float.PositiveInfinity;
        }

        if (velY > 0)
        {
            yEntry = b.Y - (a.Y + a.H);
            yExit = (b.Y + b.H) - a.Y;
        }
        else if (velY < 0)
        {
            yEntry = (b.Y + b.H) - a.Y;
            yExit = b.Y - (a.Y + a.H);
        }
        else
        {
            yEntry = float.NegativeInfinity;
            yExit = float.PositiveInfinity;
        }

        // Time of entry and exit
        float txEntry = velX != 0 ? xEntry / velX : float.NegativeInfinity;
        float tyEntry = velY != 0 ? yEntry / velY : float.NegativeInfinity;
        float txExit = velX != 0 ? xExit / velX : float.PositiveInfinity;
        float tyExit = velY != 0 ? yExit / velY : float.PositiveInfinity;

        float entryTime = MathF.Max(txEntry, tyEntry);
        float exitTime = MathF.Min(txExit, tyExit);

        // No collision
        if (entryTime > exitTime || (txEntry < 0 && tyEntry < 0) || txEntry > 1 || tyEntry > 1)
            return 1f;

        // Determine hit direction
        if (txEntry > tyEntry)
            hitDir = velX > 0 ? CollisionDir.Left : CollisionDir.Right;
        else
            hitDir = velY > 0 ? CollisionDir.Top : CollisionDir.Bottom;

        return MathF.Max(entryTime, 0);
    }

    // ── Spatial Hash Accelerated ───────────────────────────────

    // Reusable collections to avoid per-call allocation
    [ThreadStatic] private static List<Entity>? _queryResults;
    [ThreadStatic] private static HashSet<Entity>? _dedup;

    /// <summary>
    /// Check overlap between an entity and a spatial hash. Near O(1) per entity.
    /// </summary>
    public static void OverlapHash(Entity entity, SpatialHash hash, Action<Entity, Entity> onOverlap)
    {
        if (!entity.Active) return;
        _queryResults ??= new List<Entity>();
        _dedup ??= new HashSet<Entity>();
        _queryResults.Clear();
        _dedup.Clear();
        hash.QueryEntity(entity, _queryResults);
        foreach (var other in _queryResults)
        {
            if (other == entity || !other.Active) continue;
            if (!_dedup.Add(other)) continue; // skip duplicates from multi-cell entities
            if (entity.Overlaps(other))
                onOverlap(entity, other);
        }
    }

    /// <summary>
    /// Check overlap between all members of a group and a spatial hash.
    /// Much faster than OverlapGroup for large entity counts.
    /// </summary>
    public static void OverlapHash(Group group, SpatialHash hash, Action<Entity, Entity> onOverlap)
    {
        _queryResults ??= new List<Entity>();
        _dedup ??= new HashSet<Entity>();
        foreach (var entity in group.Members)
        {
            if (!entity.Active) continue;
            _queryResults.Clear();
            _dedup.Clear();
            hash.QueryEntity(entity, _queryResults);
            foreach (var other in _queryResults)
            {
                if (other == entity || !other.Active) continue;
                if (!_dedup.Add(other)) continue;
                if (entity.Overlaps(other))
                    onOverlap(entity, other);
            }
        }
    }

    // ── Circle Collision ────────────────────────────────────────

    /// <summary>
    /// Check if two circles overlap. Returns true if they intersect.
    /// </summary>
    public static bool OverlapCircles(float x1, float y1, float r1, float x2, float y2, float r2)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        float radii = r1 + r2;
        return dx * dx + dy * dy <= radii * radii;
    }

    /// <summary>
    /// Check if a circle and an AABB overlap.
    /// </summary>
    public static bool OverlapCircleRect(float cx, float cy, float radius,
        float rx, float ry, float rw, float rh)
    {
        // Find closest point on rect to circle center
        float closestX = System.Math.Clamp(cx, rx, rx + rw);
        float closestY = System.Math.Clamp(cy, ry, ry + rh);
        float dx = cx - closestX;
        float dy = cy - closestY;
        return dx * dx + dy * dy <= radius * radius;
    }

    /// <summary>
    /// Separate two circles. Pushes entity A out of entity B along the line between centers.
    /// Returns separation distance, or 0 if no overlap.
    /// </summary>
    public static float SeparateCircles(Entity a, float radiusA, Entity b, float radiusB)
    {
        float dx = b.Position.X - a.Position.X;
        float dy = b.Position.Y - a.Position.Y;
        float distSq = dx * dx + dy * dy;
        float radii = radiusA + radiusB;

        if (distSq >= radii * radii) return 0;

        float dist = MathF.Sqrt(distSq);
        if (dist < 0.001f)
        {
            // Overlapping exactly — push right
            a.Position.X -= radii;
            return radii;
        }

        float overlap = radii - dist;
        float nx = dx / dist;
        float ny = dy / dist;
        a.Position.X -= nx * overlap;
        a.Position.Y -= ny * overlap;

        if (a is KinematicEntity k)
        {
            float dot = k.Velocity.X * nx + k.Velocity.Y * ny;
            if (dot > 0)
            {
                k.Velocity.X -= dot * nx;
                k.Velocity.Y -= dot * ny;
            }
        }

        return overlap;
    }
}
