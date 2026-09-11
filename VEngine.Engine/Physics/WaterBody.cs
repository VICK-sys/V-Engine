using System;
using System.Collections.Generic;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics;
using VEngine.Engine.Graphics.GL;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Shapes;

namespace VEngine.Engine.Physics;

/// <summary>
/// Interactive water body with buoyancy, drag, animated surface waves,
/// surface deformation, splash particles, and flow currents.
/// Add to a scene like any entity.
/// </summary>
public class WaterBody : Entity
{
    // ── Dimensions ──
    public float WaterWidth;
    public float WaterDepth;
    public float ColumnSpacing;

    // ── Wave Properties ──
    public float SpringStiffness = 0.025f;
    public float Damping = 0.05f;
    public float Spread = 0.25f;
    public int PropagationPasses = 4;
    public float AmbientAmplitude = 1.0f;
    public float AmbientFrequency = 0.8f;

    // ── Physics Properties ──
    public float Density = 2f;
    public float LinearDragCoeff = 3f;
    public float AngularDragCoeff = 2f;
    /// <summary>Direct velocity multiplier per second when fully submerged. 0.05 = lose 95% speed/sec. Default 0.05.</summary>
    public float VelocityDamping = 0.05f;
    /// <summary>Extra velocity kill at the surface boundary (0-1). Prevents bobbing. Default 0.92.</summary>
    public float SurfaceDamping = 0.92f;
    public Vec2 FlowVelocity;

    // ── Visual Properties ──
    public Color SurfaceColor = new(120, 180, 255, 220);
    public Color ShallowColor = new(40, 100, 200, 100);
    public Color DeepColor = new(10, 30, 80, 200);
    public float SurfaceThickness = 2.5f;
    public bool EnableSplashParticles = true;

    // ── Internal State ──
    private WaterColumn[] _columns;
    private float[] _leftDeltas;
    private float[] _rightDeltas;
    private Vec2[] _surfacePoints; // pre-allocated for polyline
    private float _time;

    // Splash tracking: bodyId → was submerged last frame
    private readonly Dictionary<int, bool> _trackedBodies = new();
    private readonly HashSet<int> _currentSubmerged = new();

    // Splash emitters (owned, not in scene)
    private ParticleEmitter _splashEmitter = null!;
    private ParticleEmitter _dripEmitter = null!;

    // Pre-allocated clip buffer for polygon buoyancy
    private readonly Vec2[] _clipBuffer = new Vec2[PolygonShape.MaxVertices * 2 + 2];

    /// <summary>Number of wave columns.</summary>
    public int ColumnCount => _columns.Length;

    // ── Construction ──

    public WaterBody(float x, float y, float width, float depth, float columnSpacing = 6f)
    {
        Position = new Vec2(x, y);
        WaterWidth = width;
        WaterDepth = depth;
        ColumnSpacing = columnSpacing;
        Layer = 1;

        int count = System.Math.Max(2, (int)(width / columnSpacing) + 1);
        _columns = new WaterColumn[count];
        _leftDeltas = new float[count];
        _rightDeltas = new float[count];
        _surfacePoints = new Vec2[count];

        InitParticles();
    }

    private void InitParticles()
    {
        _splashEmitter = new ParticleEmitter(0, 0);
        _splashEmitter.MinSpeedX = -60; _splashEmitter.MaxSpeedX = 60;
        _splashEmitter.MinSpeedY = -200; _splashEmitter.MaxSpeedY = -50;
        _splashEmitter.GravityY = 400;
        _splashEmitter.MinLife = 0.2f; _splashEmitter.MaxLife = 0.5f;
        _splashEmitter.MinSize = 1; _splashEmitter.MaxSize = 3;
        _splashEmitter.ColorMin = new Color(80, 150, 255, 180);
        _splashEmitter.ColorMax = new Color(180, 220, 255, 255);
        _splashEmitter.FadeOut = true;
        _splashEmitter.ScaleStart = 1f; _splashEmitter.ScaleEnd = 0.2f;

        _dripEmitter = new ParticleEmitter(0, 0);
        _dripEmitter.MinSpeedX = -15; _dripEmitter.MaxSpeedX = 15;
        _dripEmitter.MinSpeedY = -10; _dripEmitter.MaxSpeedY = 10;
        _dripEmitter.GravityY = 300;
        _dripEmitter.MinLife = 0.15f; _dripEmitter.MaxLife = 0.3f;
        _dripEmitter.MinSize = 1; _dripEmitter.MaxSize = 2;
        _dripEmitter.ColorMin = new Color(80, 140, 220, 150);
        _dripEmitter.ColorMax = new Color(140, 200, 255, 200);
        _dripEmitter.FadeOut = true;
    }

    // ── Queries ──

    /// <summary>Get the interpolated water surface Y position at a world X coordinate.</summary>
    public float SurfaceYAt(float worldX)
    {
        float local = (worldX - Position.X) / ColumnSpacing;
        int i = System.Math.Clamp((int)local, 0, _columns.Length - 2);
        float t = System.Math.Clamp(local - i, 0f, 1f);
        return Position.Y + _columns[i].Height * (1 - t) + _columns[i + 1].Height * t;
    }

    /// <summary>Manually disturb the water surface at a world X position.</summary>
    public void Disturb(float worldX, float amount)
    {
        int col = WorldXToColumn(worldX);
        if (col >= 0 && col < _columns.Length)
            _columns[col].Velocity += amount;
    }

    private int WorldXToColumn(float worldX)
    {
        return (int)((worldX - Position.X) / ColumnSpacing);
    }

    // ── Update ──

    public override void Update(float dt)
    {
        base.Update(dt);
        _time += dt;

        SimulateWaves(dt);
        ApplyWaterForces(dt);

        _splashEmitter.Update(dt);
        _dripEmitter.Update(dt);
    }

    // ── Wave Simulation ──

    private void SimulateWaves(float dt)
    {
        int n = _columns.Length;
        float step = dt * 60f;

        // Spring forces + damping
        for (int i = 0; i < n; i++)
        {
            float accel = -SpringStiffness * _columns[i].Height - Damping * _columns[i].Velocity;
            _columns[i].Velocity += accel * step;
            _columns[i].Height += _columns[i].Velocity * step;
        }

        // Wave propagation (multiple passes for stability)
        for (int pass = 0; pass < PropagationPasses; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                _leftDeltas[i] = 0;
                _rightDeltas[i] = 0;
            }

            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    _leftDeltas[i] = Spread * (_columns[i].Height - _columns[i - 1].Height) * step;
                    _columns[i - 1].Velocity += _leftDeltas[i];
                }
                if (i < n - 1)
                {
                    _rightDeltas[i] = Spread * (_columns[i].Height - _columns[i + 1].Height) * step;
                    _columns[i + 1].Velocity += _rightDeltas[i];
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (i > 0) _columns[i - 1].Height += _leftDeltas[i];
                if (i < n - 1) _columns[i + 1].Height += _rightDeltas[i];
            }
        }
    }

    // ── Buoyancy, Drag, Flow ──

    private void ApplyWaterForces(float dt)
    {
        var physics = Eng.Physics;
        var gravity = physics.Settings.Gravity;
        float gravMag = gravity.Length();
        var upDir = gravMag > 0.001f ? -(gravity / gravMag) : new Vec2(0, -1);

        float waterLeft = Position.X;
        float waterRight = Position.X + WaterWidth;
        float waterBottom = Position.Y + WaterDepth;

        // Track which bodies are currently submerged (reuse set)
        _currentSubmerged.Clear();

        for (int i = 0; i < physics.Bodies.Count; i++)
        {
            var body = physics.Bodies[i];
            if (body.Type != BodyType.Dynamic || body.Shape == null) continue;

            // Quick AABB pre-filter: must overlap water X range and be below the surface
            if (body.AABB.Max.X < waterLeft || body.AABB.Min.X > waterRight) continue;
            if (body.AABB.Max.Y < Position.Y) continue; // entirely above water surface

            float submergedFraction;
            Vec2 buoyancyCenter;

            if (body.Shape is CircleShape circle)
                ComputeCircleBuoyancy(body, circle, out submergedFraction, out buoyancyCenter);
            else if (body.Shape is PolygonShape poly)
                ComputePolygonBuoyancy(body, poly, out submergedFraction, out buoyancyCenter);
            else
                continue;

            if (submergedFraction <= 0) continue;

            _currentSubmerged.Add(body.Id);

            // Buoyancy force: F = density * submergedArea * gravity (upward)
            float submergedArea = submergedFraction * ComputeShapeArea(body.Shape);
            var buoyancyForce = upDir * (Density * submergedArea * gravMag);
            body.ApplyForceAtPoint(buoyancyForce, buoyancyCenter);

            // ── Drag: direct velocity damping relative to water flow ──
            // Damp velocity toward flow velocity, not toward zero
            var relativeVel = body.LinearVelocity - FlowVelocity;
            float dampPower = submergedFraction * dt * 60f;
            float linearDamp = MathF.Pow(VelocityDamping, dampPower);
            body.LinearVelocity = FlowVelocity + relativeVel * linearDamp;
            body.AngularVelocity *= MathF.Pow(VelocityDamping * 2f, dampPower);

            // Force-based drag on top for gradual feel
            float speed = relativeVel.Length();
            if (speed > 0.5f)
                body.ApplyForce(relativeVel * (-LinearDragCoeff * submergedFraction * speed));

            body.ApplyTorque(-AngularDragCoeff * submergedFraction * body.AngularVelocity);

            // ── Surface damping: strong energy loss at the water line ──
            // This stops bobbing — objects crossing the surface lose vertical energy fast
            if (submergedFraction > 0.05f && submergedFraction < 0.95f)
            {
                // Damp vertical velocity heavily at the surface
                body.LinearVelocity = new Vec2(
                    body.LinearVelocity.X * SurfaceDamping,
                    body.LinearVelocity.Y * (SurfaceDamping * SurfaceDamping)); // extra Y damping
                body.AngularVelocity *= SurfaceDamping;
            }

            // Flow current
            if (FlowVelocity.LengthSquared() > 0.001f)
                body.ApplyForce(FlowVelocity * (Density * submergedFraction));

            // Surface disturbance from moving bodies near the surface
            if (speed > 5f && submergedFraction < 0.9f && submergedFraction > 0.05f)
            {
                int colMin = System.Math.Max(0, WorldXToColumn(body.AABB.Min.X));
                int colMax = System.Math.Min(_columns.Length - 1, WorldXToColumn(body.AABB.Max.X));
                float pushAmount = body.LinearVelocity.Y * 0.008f * (1f - submergedFraction);
                for (int c = colMin; c <= colMax; c++)
                    _columns[c].Velocity += pushAmount;
            }
        }

        // Splash detection: enter/exit
        HandleSplashes(_currentSubmerged, physics);
    }

    private void ComputeCircleBuoyancy(RigidBody body, CircleShape circle,
        out float submergedFraction, out Vec2 buoyancyCenter)
    {
        submergedFraction = 0;
        buoyancyCenter = body.Position;

        var center = body.Position + circle.Center.Rotate(body.Angle);
        float r = circle.Radius;
        float surfaceY = SurfaceYAt(center.X);

        if (center.Y + r <= surfaceY) { submergedFraction = 0; return; } // entirely above water

        // d = how far center is below surface (positive = below)
        float d = center.Y - surfaceY;

        if (d >= r) { submergedFraction = 1f; buoyancyCenter = center; return; } // fully submerged

        // Partial submersion: circular segment
        // In Y-down, "below surface" = Y > surfaceY
        float clampedD = System.Math.Clamp(-d, -r, r);
        float segmentArea = MathF.Acos(clampedD / r) * r * r - clampedD * MathF.Sqrt(r * r - clampedD * clampedD);
        float totalArea = MathF.PI * r * r;
        submergedFraction = System.Math.Clamp(segmentArea / totalArea, 0f, 1f);

        // Approximate buoyancy center: midpoint of submerged portion
        buoyancyCenter = new Vec2(center.X, (surfaceY + center.Y + r) * 0.5f);
    }

    private void ComputePolygonBuoyancy(RigidBody body, PolygonShape poly,
        out float submergedFraction, out Vec2 buoyancyCenter)
    {
        submergedFraction = 0;
        buoyancyCenter = body.Position;

        // Clip polygon against the wave-displaced surface (sampled per-vertex)
        int clipCount = 0;

        for (int i = 0; i < poly.Count; i++)
        {
            var v1 = poly.GetWorldVertex(i, body.Position, body.Angle);
            var v2 = poly.GetWorldVertex((i + 1) % poly.Count, body.Position, body.Angle);

            float surfY1 = SurfaceYAt(v1.X);
            float surfY2 = SurfaceYAt(v2.X);

            bool v1Below = v1.Y >= surfY1;
            bool v2Below = v2.Y >= surfY2;

            if (v1Below)
            {
                if (clipCount < _clipBuffer.Length)
                    _clipBuffer[clipCount++] = v1;
            }

            if (v1Below != v2Below)
            {
                // Edge crosses the wave surface — find intersection
                // Edge: P(t) = v1 + t*(v2-v1), Surface: S(t) = surfY1 + t*(surfY2-surfY1)
                float denom = (v2.Y - v1.Y) - (surfY2 - surfY1);
                float t = MathF.Abs(denom) > 1e-6f ? (surfY1 - v1.Y) / denom : 0.5f;
                t = System.Math.Clamp(t, 0f, 1f);
                float ix = v1.X + t * (v2.X - v1.X);
                float iy = surfY1 + t * (surfY2 - surfY1);
                if (clipCount < _clipBuffer.Length)
                    _clipBuffer[clipCount++] = new Vec2(ix, iy);
            }
        }

        if (clipCount < 3) return;

        // Compute area and centroid of clipped polygon (shoelace)
        float area = 0;
        var centroid = Vec2.Zero;

        for (int i = 0; i < clipCount; i++)
        {
            int next = (i + 1) % clipCount;
            float cross = Vec2.Cross(_clipBuffer[i], _clipBuffer[next]);
            area += cross;
            centroid += (_clipBuffer[i] + _clipBuffer[next]) * cross;
        }

        area *= 0.5f;
        if (MathF.Abs(area) < 0.01f) return;

        centroid /= (6f * area);
        area = MathF.Abs(area);

        float totalArea = ComputeShapeArea(poly);
        submergedFraction = System.Math.Clamp(area / totalArea, 0f, 1f);
        buoyancyCenter = centroid;
    }

    private static float ComputeShapeArea(Shape shape)
    {
        if (shape is CircleShape c)
            return MathF.PI * c.Radius * c.Radius;
        if (shape is PolygonShape p)
        {
            float area = 0;
            for (int i = 0; i < p.Count; i++)
            {
                int next = (i + 1) % p.Count;
                area += Vec2.Cross(p.Vertices[i], p.Vertices[next]);
            }
            return MathF.Abs(area * 0.5f);
        }
        return 1f;
    }

    // ── Splash Detection ──

    private void HandleSplashes(HashSet<int> _currentSubmerged, PhysicsWorld physics)
    {
        // Detect entries
        foreach (int id in _currentSubmerged)
        {
            if (!_trackedBodies.ContainsKey(id))
            {
                // New entry — splash!
                if (EnableSplashParticles)
                {
                    var body = FindBody(physics, id);
                    if (body != null)
                    {
                        float speed = body.LinearVelocity.Length();
                        int count = System.Math.Clamp((int)(speed * 0.08f), 3, 20);
                        float surfY = SurfaceYAt(body.Position.X);
                        _splashEmitter.Position = new Vec2(body.Position.X, surfY);
                        _splashEmitter.SpawnWidth = body.AABB.Width * 0.6f;
                        _splashEmitter.Emit(count);

                        // Disturb surface proportional to entry speed
                        float disturbance = System.Math.Clamp(speed * 0.02f, 0.5f, 8f);
                        int colMin = System.Math.Max(0, WorldXToColumn(body.AABB.Min.X) - 1);
                        int colMax = System.Math.Min(_columns.Length - 1, WorldXToColumn(body.AABB.Max.X) + 1);
                        for (int c = colMin; c <= colMax; c++)
                            _columns[c].Velocity += disturbance;
                    }
                }
            }
        }

        // Detect exits
        foreach (var kv in _trackedBodies)
        {
            if (!_currentSubmerged.Contains(kv.Key))
            {
                if (EnableSplashParticles)
                {
                    var body = FindBody(Eng.Physics, kv.Key);
                    if (body != null)
                    {
                        float surfY = SurfaceYAt(body.Position.X);
                        _dripEmitter.Position = new Vec2(body.Position.X, surfY);
                        _dripEmitter.Emit(4);
                    }
                }
            }
        }

        // Update tracking
        _trackedBodies.Clear();
        foreach (int id in _currentSubmerged)
            _trackedBodies[id] = true;
    }

    private static RigidBody? FindBody(PhysicsWorld physics, int id) => physics.GetBody(id);

    // ── Rendering ──

    public override void Draw()
    {
        int n = _columns.Length;
        float bottom = Position.Y + WaterDepth;

        // Build surface points with ambient animation
        for (int i = 0; i < n; i++)
        {
            float x = Position.X + i * ColumnSpacing;
            float ambient = AmbientAmplitude * MathF.Sin(AmbientFrequency * _time + i * 0.3f);
            float y = Position.Y + _columns[i].Height + ambient;
            _surfacePoints[i] = new Vec2(x, y);
        }

        // Fill: gradient quads from surface to bottom
        float sr = ShallowColor.R / 255f, sg = ShallowColor.G / 255f, sb = ShallowColor.B / 255f, sa = ShallowColor.A / 255f;
        float dr = DeepColor.R / 255f, dg = DeepColor.G / 255f, db = DeepColor.B / 255f, da = DeepColor.A / 255f;

        var cam = Eng.Camera;
        for (int i = 0; i < n - 1; i++)
        {
            var tl = cam.WorldToScreen(_surfacePoints[i].X, _surfacePoints[i].Y);
            var tr = cam.WorldToScreen(_surfacePoints[i + 1].X, _surfacePoints[i + 1].Y);
            var br = cam.WorldToScreen(_surfacePoints[i + 1].X, bottom);
            var bl = cam.WorldToScreen(_surfacePoints[i].X, bottom);

            Eng.GL.Primitives.DrawFilledQuadGradient(
                tl.X, tl.Y, sr, sg, sb, sa,
                tr.X, tr.Y, sr, sg, sb, sa,
                br.X, br.Y, dr, dg, db, da,
                bl.X, bl.Y, dr, dg, db, da);
        }

        // Surface glow (wider, transparent)
        Graphics.Draw.Polyline(_surfacePoints, SurfaceThickness * 3f,
            (byte)(SurfaceColor.R * 0.6f), (byte)(SurfaceColor.G * 0.6f), SurfaceColor.B, 30);

        // Surface line
        Graphics.Draw.Polyline(_surfacePoints, SurfaceThickness,
            SurfaceColor.R, SurfaceColor.G, SurfaceColor.B, SurfaceColor.A);

        // Particles
        _splashEmitter.Draw();
        _dripEmitter.Draw();
    }
}
