using System;
using System.Collections.Generic;
using VEngine.Engine.Core;
using VEngine.Engine.Math;
using VEngine.Engine.Physics;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Weather entity that spawns falling rain particles across the camera viewport.
/// Renders drops as thin streaks. Optionally spawns splash particles on ground
/// impact and disturbs WaterBody surfaces.
/// Add to a scene like any entity.
/// </summary>
public class Rain : Entity
{
    // ── Configuration ──

    /// <summary>Rain intensity 0-1. Controls drops per second (0 = off, 1 = downpour).</summary>
    public float Intensity = 0.5f;

    /// <summary>Maximum drops alive at once. Budget cap to prevent runaway allocation.</summary>
    public int MaxDrops = 600;

    /// <summary>Wind angle in degrees from vertical. Positive = blows right, negative = left.</summary>
    public float WindAngle;

    /// <summary>Minimum fall speed in pixels/sec.</summary>
    public float MinSpeed = 300f;

    /// <summary>Maximum fall speed in pixels/sec.</summary>
    public float MaxSpeed = 500f;

    /// <summary>Minimum streak length in pixels (world space).</summary>
    public float MinLength = 6f;

    /// <summary>Maximum streak length in pixels (world space).</summary>
    public float MaxLength = 14f;

    /// <summary>Streak thickness in screen pixels.</summary>
    public float Thickness = 1.2f;

    /// <summary>Rain drop color (tip). Alpha controls overall opacity.</summary>
    public Color ColorFront = new(180, 200, 230, 160);

    /// <summary>Rain drop color (tail). Fades to this.</summary>
    public Color ColorTail = new(140, 170, 210, 40);

    /// <summary>World Y coordinate where drops hit the ground and splash. Set to float.MaxValue to disable.</summary>
    public float GroundY = float.MaxValue;

    /// <summary>Extra margin (world pixels) around the camera viewport where drops spawn/live.</summary>
    public float Margin = 60f;

    /// <summary>Enable splash particles when drops hit ground or water.</summary>
    public bool EnableSplashes = true;

    // ── Internal State ──

    private RainDrop[] _drops;
    private int _count;
    private float _spawnAccum;
    private readonly Random _rng;

    // Splash emitter (owned, not added to scene)
    private ParticleEmitter _splashEmitter = null!;

    // Water bodies to disturb
    private readonly List<WaterBody> _waterBodies = new();

    // Entities that rain collides with (splashes off their bounds)
    private readonly List<Entity> _obstacles = new();

    // Cached direction vector (recomputed when WindAngle changes)
    private float _cachedWindAngle = float.NaN;
    private float _dirX, _dirY;

    public Rain(int maxDrops = 600)
    {
        MaxDrops = maxDrops;
        _drops = new RainDrop[maxDrops];
        _rng = new Random(Environment.TickCount ^ GetHashCode());
        Layer = 3; // foreground by default
        InitSplash();
    }

    private void InitSplash()
    {
        _splashEmitter = new ParticleEmitter(0, 0);
        _splashEmitter.MinSpeedX = -30; _splashEmitter.MaxSpeedX = 30;
        _splashEmitter.MinSpeedY = -50; _splashEmitter.MaxSpeedY = -15;
        _splashEmitter.GravityY = 200;
        _splashEmitter.MinLife = 0.08f; _splashEmitter.MaxLife = 0.2f;
        _splashEmitter.MinSize = 1; _splashEmitter.MaxSize = 2;
        _splashEmitter.ColorMin = new Color(160, 190, 220, 120);
        _splashEmitter.ColorMax = new Color(200, 220, 240, 200);
        _splashEmitter.FadeOut = true;
        _splashEmitter.ScaleStart = 1f; _splashEmitter.ScaleEnd = 0.3f;
    }

    // ── Water Body Registration ──

    /// <summary>Register a WaterBody so rain disturbs its surface on impact.</summary>
    public void AddWaterBody(WaterBody water) => _waterBodies.Add(water);

    /// <summary>Unregister a WaterBody.</summary>
    public void RemoveWaterBody(WaterBody water) => _waterBodies.Remove(water);

    // ── Obstacle Registration ──

    /// <summary>Register an entity as a rain obstacle. Drops splash off its collision bounds.</summary>
    public void AddObstacle(Entity entity) => _obstacles.Add(entity);

    /// <summary>Unregister an obstacle.</summary>
    public void RemoveObstacle(Entity entity) => _obstacles.Remove(entity);

    // ── Queries ──

    /// <summary>Number of active rain drops.</summary>
    public int ActiveDrops => _count;

    /// <summary>Drops spawned per second at current intensity.</summary>
    public float DropsPerSecond => Intensity * MaxDrops * 2.5f;

    // ── Update ──

    public override void Update(float dt)
    {
        base.Update(dt);
        UpdateDirection();
        UpdateDrops(dt);
        SpawnDrops(dt);
        _splashEmitter.Update(dt);
    }

    private void UpdateDirection()
    {
        // Only recompute when wind changes
        if (WindAngle == _cachedWindAngle) return;
        _cachedWindAngle = WindAngle;
        float rad = WindAngle * (MathF.PI / 180f);
        _dirX = MathF.Sin(rad);
        _dirY = MathF.Cos(rad); // Y-down: cos gives downward component
    }

    private void GetViewport(out float w, out float h)
    {
        var game = Eng.Context.Game;
        w = game != null ? game.Width : 320;
        h = game != null ? game.Height : 240;
    }

    private void UpdateDrops(float dt)
    {
        var cam = Eng.Camera;
        GetViewport(out float vw, out float vh);
        float viewLeft = cam.Position.X - Margin;
        float viewRight = cam.Position.X + vw / cam.Zoom + Margin;
        float viewBottom = cam.Position.Y + vh / cam.Zoom + Margin;

        for (int i = _count - 1; i >= 0; i--)
        {
            ref var d = ref _drops[i];
            d.X += d.VX * dt;
            d.Y += d.VY * dt;

            bool remove = false;

            // Off-screen bottom or sides
            if (d.Y > viewBottom || d.X < viewLeft || d.X > viewRight)
                remove = true;

            // Ground collision
            if (!remove && d.Y >= GroundY)
            {
                if (EnableSplashes)
                    SpawnSplash(d.X, GroundY);
                remove = true;
            }

            // Water body collision
            if (!remove)
            {
                for (int w = 0; w < _waterBodies.Count; w++)
                {
                    var wb = _waterBodies[w];
                    float wLeft = wb.Position.X;
                    float wRight = wb.Position.X + wb.WaterWidth;
                    if (d.X >= wLeft && d.X <= wRight)
                    {
                        float surfY = wb.SurfaceYAt(d.X);
                        if (d.Y >= surfY)
                        {
                            wb.Disturb(d.X, 0.3f);
                            if (EnableSplashes)
                                SpawnSplash(d.X, surfY);
                            remove = true;
                            break;
                        }
                    }
                }
            }

            // Obstacle collision (entities rain bounces off)
            if (!remove)
            {
                for (int o = 0; o < _obstacles.Count; o++)
                {
                    var obs = _obstacles[o];
                    if (!obs.Active || obs.Destroyed) continue;
                    var (bx, by, bw, bh) = obs.GetCollisionBounds();
                    if (d.X >= bx && d.X <= bx + bw && d.Y >= by && d.Y <= by + bh)
                    {
                        if (EnableSplashes)
                            SpawnSplash(d.X, d.Y);
                        remove = true;
                        break;
                    }
                }
            }

            if (remove)
            {
                // Swap-with-last removal
                _drops[i] = _drops[_count - 1];
                _count--;
            }
        }
    }

    private void SpawnDrops(float dt)
    {
        if (Intensity <= 0) return;

        float rate = DropsPerSecond;
        _spawnAccum += rate * dt;

        var cam = Eng.Camera;
        GetViewport(out float vw, out float vh);
        float viewLeft = cam.Position.X - Margin;
        float viewRight = cam.Position.X + vw / cam.Zoom + Margin;
        float viewTop = cam.Position.Y - Margin;

        // Wind offset: shift spawn region so drops appear to come from the right angle
        // If wind blows right, spawn further left so drops don't appear from nothing mid-screen
        float viewHeight = vh / cam.Zoom + Margin * 2;
        float windShift = -_dirX / _dirY * viewHeight; // horizontal shift over full fall distance
        float spawnLeft = viewLeft + MathF.Min(0, windShift);
        float spawnRight = viewRight + MathF.Max(0, windShift);
        float spawnWidth = spawnRight - spawnLeft;

        while (_spawnAccum >= 1f && _count < MaxDrops)
        {
            _spawnAccum -= 1f;

            float speed = MinSpeed + (float)_rng.NextDouble() * (MaxSpeed - MinSpeed);
            float length = MinLength + (float)_rng.NextDouble() * (MaxLength - MinLength);

            _drops[_count++] = new RainDrop
            {
                X = spawnLeft + (float)_rng.NextDouble() * spawnWidth,
                Y = viewTop - (float)_rng.NextDouble() * Margin, // spawn above viewport
                VX = _dirX * speed,
                VY = _dirY * speed,
                Length = length,
            };
        }

        // Prevent accumulator from growing unbounded when MaxDrops is hit
        if (_spawnAccum > 3f) _spawnAccum = 3f;
    }

    private void SpawnSplash(float x, float y)
    {
        _splashEmitter.Position = new Vec2(x, y);
        _splashEmitter.Emit(_rng.Next(2, 5));
    }

    // ── Rendering ──

    public override void Draw()
    {
        var cam = Eng.Camera;
        float fr1 = ColorFront.R / 255f, fg1 = ColorFront.G / 255f, fb1 = ColorFront.B / 255f, fa1 = ColorFront.A / 255f;
        float fr2 = ColorTail.R / 255f, fg2 = ColorTail.G / 255f, fb2 = ColorTail.B / 255f, fa2 = ColorTail.A / 255f;
        float halfT = Thickness * 0.5f;

        for (int i = 0; i < _count; i++)
        {
            ref var d = ref _drops[i];

            // Front (leading tip) and tail positions
            float speed = MathF.Sqrt(d.VX * d.VX + d.VY * d.VY);
            float nx, ny;
            if (speed > 0.001f)
            {
                nx = d.VX / speed;
                ny = d.VY / speed;
            }
            else
            {
                nx = 0; ny = 1;
            }

            float frontX = d.X;
            float frontY = d.Y;
            float tailX = d.X - nx * d.Length;
            float tailY = d.Y - ny * d.Length;

            // Transform to screen space
            var sf = cam.WorldToScreen(frontX, frontY);
            var st = cam.WorldToScreen(tailX, tailY);

            // Perpendicular for thickness
            float dx = sf.X - st.X;
            float dy = sf.Y - st.Y;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 0.5f) continue;

            float px = -dy / len * halfT;
            float py = dx / len * halfT;

            // Gradient quad: front color at tip, tail color at tail
            Eng.GL.Primitives.DrawFilledQuadGradient(
                sf.X + px, sf.Y + py, fr1, fg1, fb1, fa1,
                sf.X - px, sf.Y - py, fr1, fg1, fb1, fa1,
                st.X - px, st.Y - py, fr2, fg2, fb2, fa2,
                st.X + px, st.Y + py, fr2, fg2, fb2, fa2);
        }

        _splashEmitter.Draw();
    }

    // ── Internal ──

    /// <summary>Resize the drop pool. Active drops beyond the new limit are discarded.</summary>
    public void SetMaxDrops(int max)
    {
        if (max == MaxDrops) return;
        MaxDrops = System.Math.Max(1, max);
        if (_count > MaxDrops) _count = MaxDrops;
        Array.Resize(ref _drops, MaxDrops);
    }

    private struct RainDrop
    {
        public float X, Y;
        public float VX, VY;
        public float Length;
    }
}
