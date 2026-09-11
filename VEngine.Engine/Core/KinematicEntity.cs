using VEngine.Engine.Math;

namespace VEngine.Engine.Core;

/// <summary>
/// Entity with velocity, acceleration, and angular velocity.
/// Physics are applied automatically in Update(). Extend this for game objects
/// that move (players, enemies, projectiles). For static objects (tiles, UI),
/// extend Entity directly.
/// </summary>
public class KinematicEntity : Entity
{
    public Vec2 Velocity;
    public Vec2 Acceleration;

    /// <summary>Rotation speed in degrees per second.</summary>
    public float AngularVelocity;

    public KinematicEntity(float x = 0, float y = 0) : base(x, y)
    {
        Velocity = Vec2.Zero;
        Acceleration = Vec2.Zero;
    }

    public override void Update(float dt)
    {
        Velocity.X += Acceleration.X * dt;
        Velocity.Y += Acceleration.Y * dt;
        Position.X += Velocity.X * dt;
        Position.Y += Velocity.Y * dt;
        Angle += AngularVelocity * dt;
        base.Update(dt); // run behaviors
    }
}
