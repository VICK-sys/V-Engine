using System;
using System.Collections.Generic;

namespace VEngine.Engine.Core;

/// <summary>
/// Attachable behavior for entities. Provides composition alongside inheritance.
/// Attach behaviors to any Entity to add modular logic without subclassing.
///
/// Usage:
///   entity.AddBehavior(new HealthBehavior(100));
///   entity.AddBehavior(new FlickerOnHit());
/// </summary>
public abstract class Behavior
{
    /// <summary>The entity this behavior is attached to.</summary>
    public Entity Owner { get; internal set; } = null!;

    /// <summary>Called when the behavior is added to an entity.</summary>
    public virtual void OnAttach() { }

    /// <summary>Called each frame during the entity's Update.</summary>
    public virtual void Update(float dt) { }

    /// <summary>Called when the entity is destroyed.</summary>
    public virtual void OnDestroy() { }
}
