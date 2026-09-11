using VEngine.Engine.Math;

namespace VEngine.Engine.Core;

/// <summary>
/// Base game object: position, scale, dimensions, visibility, draw layer.
/// For objects that move, extend KinematicEntity instead (adds Velocity, Acceleration).
/// For static objects (tiles, UI, decorations), extend Entity directly.
/// </summary>
public class Entity
{
    public Vec2 Position;

    public float ScaleX = 1f;
    public float ScaleY = 1f;

    /// <summary>Rotation angle in degrees (clockwise).</summary>
    public float Angle;

    /// <summary>Base (unscaled) width.</summary>
    public float BaseWidth;
    /// <summary>Base (unscaled) height.</summary>
    public float BaseHeight;

    /// <summary>Scaled width used for rendering and collision.</summary>
    public float Width => BaseWidth * ScaleX;
    /// <summary>Scaled height used for rendering and collision.</summary>
    public float Height => BaseHeight * ScaleY;

    private bool _active = true;

    /// <summary>Whether this entity is updated and participates in collision. Setting triggers OnActivated/OnDeactivated.</summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value) OnActivated(); else OnDeactivated();
        }
    }

    public bool Visible = true;

    /// <summary>Called when Active changes from false to true. Override for setup logic.</summary>
    protected virtual void OnActivated() { }

    /// <summary>Called when Active changes from true to false. Override for cleanup logic.</summary>
    protected virtual void OnDeactivated() { }

    /// <summary>
    /// If true, this entity is not moved by collision separation (walls, platforms, ground).
    /// </summary>
    public bool Immovable;

    /// <summary>
    /// Draw layer. Lower values draw first (behind). Default 0.
    /// Use for broad categories: 0 = background, 1 = world, 2 = player, 3 = foreground, 4 = UI.
    /// </summary>
    public int Layer;

    /// <summary>
    /// Fine-grained draw order within a layer. Lower values draw first.
    /// Useful for Y-sorting: set to Position.Y so entities lower on screen draw in front.
    /// </summary>
    public float ZOrder;

    /// <summary>
    /// How much this entity scrolls with the camera.
    /// (1,1) = normal world object. (0,0) = fixed to screen (UI/HUD).
    /// </summary>
    public Vec2 ScrollFactor = new(1f, 1f);

    // ── Behaviors (composition) ────────────────────────────────
    private List<Behavior>? _behaviors;

    /// <summary>Attach a behavior to this entity. Supports composition alongside inheritance.</summary>
    public T AddBehavior<T>(T behavior) where T : Behavior
    {
        _behaviors ??= new List<Behavior>();
        behavior.Owner = this;
        _behaviors.Add(behavior);
        behavior.OnAttach();
        return behavior;
    }

    /// <summary>Get the first attached behavior of a given type, or null.</summary>
    public T? GetBehavior<T>() where T : Behavior
    {
        if (_behaviors == null) return null;
        foreach (var b in _behaviors)
            if (b is T t) return t;
        return null;
    }

    /// <summary>Remove a behavior from this entity.</summary>
    public void RemoveBehavior(Behavior behavior) => _behaviors?.Remove(behavior);

    public Entity(float x = 0, float y = 0)
    {
        Position = new Vec2(x, y);
    }

    public virtual void Update(float dt)
    {
        if (_behaviors != null)
            foreach (var b in _behaviors) b.Update(dt);
    }

    public virtual void Draw() { }

    /// <summary>Whether this entity has been destroyed. Prevents double-destroy.</summary>
    public bool Destroyed { get; private set; }

    public void Destroy()
    {
        if (Destroyed) return;
        Destroyed = true;
        if (_behaviors != null)
            foreach (var b in _behaviors) b.OnDestroy();
        OnDestroy();
    }

    /// <summary>Override to clean up resources (textures, audio, etc.). Called once by Destroy().</summary>
    protected virtual void OnDestroy() { }

    /// <summary>
    /// Returns the collision bounds for this entity in world space (x, y, w, h).
    /// Override in subclasses to provide custom collision rects (e.g. hitboxes).
    /// </summary>
    public virtual (float X, float Y, float W, float H) GetCollisionBounds()
    {
        return (Position.X, Position.Y, Width, Height);
    }

    /// <summary>
    /// AABB overlap check using collision bounds.
    /// </summary>
    public bool Overlaps(Entity other)
    {
        var a = GetCollisionBounds();
        var b = other.GetCollisionBounds();
        return a.X < b.X + b.W &&
               a.X + a.W > b.X &&
               a.Y < b.Y + b.H &&
               a.Y + a.H > b.Y;
    }
}
