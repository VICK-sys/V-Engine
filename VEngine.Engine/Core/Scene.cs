using System.Collections.Generic;

namespace VEngine.Engine.Core;

public abstract class Scene
{
    private readonly Group _root = new();

    /// <summary>Read-only list of entities in this scene.</summary>
    public IReadOnlyList<Entity> Entities => _root.Members;

    public virtual void Create() { }

    public virtual void Update(float dt)
    {
        // Delegate to root Group (which iterates backwards for safe removal)
        _root.Update(dt);
    }

    /// <summary>
    /// Mark draw order as needing a re-sort. Call after changing Layer or ZOrder.
    /// Also called automatically when entities are added/removed.
    /// </summary>
    public void SortDrawOrder() => _root.SortDrawOrder();

    public virtual void Draw() => _root.Draw();

    public virtual void Destroy() => _root.Destroy();

    public T Add<T>(T entity) where T : Entity => _root.Add(entity);

    public bool Remove(Entity entity, bool destroy = true) => _root.Remove(entity, destroy);
}
