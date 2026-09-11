using System;
using VEngine.Engine.Graphics;
using VEngine.Engine.Math;

namespace VEngine.Engine.Core;

/// <summary>
/// Lightweight AI utility functions. Not a framework — just helpers
/// for common patterns like movement, range checks, and line of sight.
/// </summary>
public static class AI
{
    /// <summary>
    /// Move an entity toward a world position at a given speed.
    /// Sets velocity directly. Returns true if within arriveDistance.
    /// </summary>
    public static bool MoveToward(KinematicEntity entity, float targetX, float targetY, float speed, float arriveDistance = 2f)
    {
        float dx = targetX - entity.Position.X;
        float dy = targetY - entity.Position.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist <= arriveDistance || dist < 0.001f)
        {
            entity.Velocity = Vec2.Zero;
            return true;
        }

        float scale = speed / dist;
        entity.Velocity.X = dx * scale;
        entity.Velocity.Y = dy * scale;
        return false;
    }

    /// <summary>
    /// Move an entity toward another entity at a given speed.
    /// Returns true if within arriveDistance.
    /// </summary>
    public static bool MoveToward(KinematicEntity entity, Entity target, float speed, float arriveDistance = 2f)
    {
        return MoveToward(entity, target.Position.X, target.Position.Y, speed, arriveDistance);
    }

    /// <summary>
    /// Move an entity away from a world position at a given speed.
    /// </summary>
    public static void MoveAway(KinematicEntity entity, float fromX, float fromY, float speed)
    {
        float dx = entity.Position.X - fromX;
        float dy = entity.Position.Y - fromY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist < 0.01f)
        {
            entity.Velocity.X = speed;
            entity.Velocity.Y = 0;
            return;
        }

        float scale = speed / dist;
        entity.Velocity.X = dx * scale;
        entity.Velocity.Y = dy * scale;
    }

    /// <summary>
    /// Move an entity away from another entity at a given speed.
    /// </summary>
    public static void MoveAway(KinematicEntity entity, Entity from, float speed)
    {
        MoveAway(entity, from.Position.X, from.Position.Y, speed);
    }

    /// <summary>
    /// Distance between two entities (center to center using position).
    /// </summary>
    public static float DistanceTo(Entity a, Entity b)
    {
        return Vec2.Distance(a.Position, b.Position);
    }

    /// <summary>
    /// Distance between an entity and a world point.
    /// </summary>
    public static float DistanceTo(Entity entity, float x, float y)
    {
        float dx = entity.Position.X - x;
        float dy = entity.Position.Y - y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Check if an entity is within range of another.
    /// </summary>
    public static bool InRange(Entity a, Entity b, float range)
    {
        return Vec2.DistanceSquared(a.Position, b.Position) <= range * range;
    }

    /// <summary>
    /// Check line of sight between two world points against a tilemap.
    /// Uses Bresenham's line algorithm on the tile grid for precise, gap-free raycasting.
    /// Returns true if no solid tiles block the path.
    /// </summary>
    public static bool HasLineOfSight(float x1, float y1, float x2, float y2, Tilemap tilemap)
    {
        var (c0, r0) = tilemap.WorldToTile(x1, y1);
        var (c1, r1) = tilemap.WorldToTile(x2, y2);

        // Bresenham's line algorithm on the tile grid
        int dc = System.Math.Abs(c1 - c0);
        int dr = System.Math.Abs(r1 - r0);
        int sc = c0 < c1 ? 1 : -1;
        int sr = r0 < r1 ? 1 : -1;
        int err = dc - dr;

        while (true)
        {
            if (tilemap.IsSolid(c0, r0))
                return false;

            if (c0 == c1 && r0 == r1)
                break;

            int e2 = err * 2;
            if (e2 > -dr)
            {
                err -= dr;
                c0 += sc;
            }
            if (e2 < dc)
            {
                err += dc;
                r0 += sr;
            }
        }
        return true;
    }

    /// <summary>
    /// Check line of sight between two entities against a tilemap.
    /// </summary>
    public static bool HasLineOfSight(Entity a, Entity b, Tilemap tilemap)
    {
        return HasLineOfSight(a.Position.X, a.Position.Y, b.Position.X, b.Position.Y, tilemap);
    }

    /// <summary>
    /// Face an entity toward a target by setting FlipX (for Sprite entities).
    /// </summary>
    public static void FaceToward(Sprite sprite, float targetX)
    {
        sprite.FlipX = targetX < sprite.Position.X;
    }

    /// <summary>
    /// Face an entity toward another entity.
    /// </summary>
    public static void FaceToward(Sprite sprite, Entity target)
    {
        sprite.FlipX = target.Position.X < sprite.Position.X;
    }
}
