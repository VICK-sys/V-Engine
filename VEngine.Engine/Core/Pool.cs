using System;
using System.Collections.Generic;
using VEngine.Engine.Math;

namespace VEngine.Engine.Core;

/// <summary>
/// Object pool backed by a Group. Pooled entities stay in the scene as inactive members.
/// Use Get() to spawn and Release() to recycle — no allocation during gameplay.
/// Add pool.Group to a scene to include pooled entities in update/draw/collision.
/// </summary>
public class Pool<T> where T : Entity
{
    private readonly Func<T> _factory;
    private readonly Stack<T> _inactive = new();
    private readonly HashSet<T> _members = new(); // O(1) membership check for Release validation

    /// <summary>The Group containing all pooled entities. Add this to a scene.</summary>
    public Group Group { get; } = new();

    /// <summary>Read-only list of all entities (active + inactive).</summary>
    public IReadOnlyList<Entity> Members => Group.Members;

    /// <summary>Number of currently active (in-use) entities.</summary>
    public int ActiveCount => Members.Count - _inactive.Count;

    /// <summary>Total entities in the pool (active + inactive).</summary>
    public int TotalCount => Members.Count;

    /// <summary>Add this pool's Group to a scene. Convenience for scene.Add(pool.Group).</summary>
    public Pool<T> AddTo(Scene scene) { scene.Add(Group); return this; }

    /// <summary>
    /// Create a pool with a factory function. Optionally preload entities.
    /// </summary>
    public Pool(Func<T> factory, int preload = 0)
    {
        _factory = factory;
        for (int i = 0; i < preload; i++)
        {
            var e = _factory();
            e.Active = false;
            e.Visible = false;
            Group.Add(e);
            _members.Add(e);
            _inactive.Push(e);
        }
    }

    /// <summary>
    /// Get an entity from the pool. Reuses an inactive one or creates a new one.
    /// </summary>
    public T Get()
    {
        T entity;
        if (_inactive.Count > 0)
        {
            entity = _inactive.Pop();
        }
        else
        {
            entity = _factory();
            Group.Add(entity);
            _members.Add(entity);
        }
        entity.Active = true;
        entity.Visible = true;
        return entity;
    }

    /// <summary>
    /// Get an entity and reset its basic state (position, velocity, angle).
    /// </summary>
    public T Get(float x, float y)
    {
        var e = Get();
        e.Position = new Vec2(x, y);
        e.Angle = 0;
        if (e is KinematicEntity k)
        {
            k.Velocity = Vec2.Zero;
            k.Acceleration = Vec2.Zero;
            k.AngularVelocity = 0;
        }
        return e;
    }

    /// <summary>
    /// Return an entity to the pool. It becomes inactive and invisible.
    /// </summary>
    public void Release(T entity)
    {
        if (!entity.Active) return;
        if (!_members.Contains(entity)) return; // O(1) membership check
        entity.Active = false;
        entity.Visible = false;
        _inactive.Push(entity);
    }

    /// <summary>
    /// Release all active entities back to the pool.
    /// </summary>
    public void ReleaseAll()
    {
        foreach (var e in Members)
        {
            if (e.Active)
            {
                e.Active = false;
                e.Visible = false;
                if (e is T typed) _inactive.Push(typed);
            }
        }
    }
}
