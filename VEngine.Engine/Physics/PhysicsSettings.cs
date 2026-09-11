using System;
using VEngine.Engine.Math;

namespace VEngine.Engine.Physics;

/// <summary>
/// Global physics simulation settings.
/// </summary>
public class PhysicsSettings
{
    /// <summary>World gravity in pixels/s². Default (0, 980) — Y-down, ~1g at 100px/meter.</summary>
    public Vec2 Gravity = new(0, 980f);

    /// <summary>Number of velocity constraint solver iterations per step. Higher = more accurate stacking. Default 8.</summary>
    public int VelocityIterations = 8;

    /// <summary>Number of position constraint solver iterations per step. Higher = less overlap. Default 3.</summary>
    public int PositionIterations = 3;

    /// <summary>Maximum linear speed in pixels/s. Bodies are clamped to this. Default 2000.</summary>
    public float MaxLinearSpeed = 2000f;

    /// <summary>Maximum angular speed in radians/s. Default π*60 (~30 full rotations/s).</summary>
    public float MaxAngularSpeed = MathF.PI * 60f;

    /// <summary>Time a body must be nearly still before sleeping (seconds). Default 0.5.</summary>
    public float SleepTimeThreshold = 0.5f;

    /// <summary>Linear velocity below which a body is considered still (px/s). Default 0.5.</summary>
    public float SleepLinearTolerance = 0.5f;

    /// <summary>Angular velocity below which a body is considered still (rad/s). Default ~2°/s.</summary>
    public float SleepAngularTolerance = 2f * MathF.PI / 180f;
}
