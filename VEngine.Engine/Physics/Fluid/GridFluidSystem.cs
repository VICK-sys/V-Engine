using System;
using VEngine.Engine.Core;
using VEngine.Engine.Math;

namespace VEngine.Engine.Physics.Fluid;

/// <summary>
/// Cellular automaton water grid — fills containers like real water.
/// Each cell has a water level 0-1. Water flows down, overflows sideways.
/// Simple, fast, and actually fills boxes.
///
/// Inspired by Terraria/Noita-style water simulation.
///
/// Usage:
///   var water = new GridFluidSystem(120, 68, 600, 340);
///   water.AddWater(60, 3, 0.5f);
///   scene.Add(water);
/// </summary>
public class GridFluidSystem : Entity
{
    private readonly int N; // columns
    private readonly int M; // rows

    private readonly float _pixelW, _pixelH;
    private readonly float _cellW, _cellH;

    // Water level per cell: 0 = empty, 1 = full
    private float[] _water;
    private float[] _waterNext; // double buffer

    // Velocity field per cell (for sloshing/splashing)
    private float[] _velX;  // horizontal velocity
    private float[] _velY;  // vertical velocity (negative = upward)

    // ── Parameters ──

    /// <summary>How fast water flows down per second. 1.0 = instant.</summary>
    public float FlowRate = 0.9f;

    /// <summary>How fast water spreads sideways per second.</summary>
    public float SpreadRate = 0.4f;

    /// <summary>Maximum water level per cell.</summary>
    public float MaxLevel = 1.0f;

    /// <summary>Minimum water level to render.</summary>
    public float RenderThreshold = 0.01f;

    /// <summary>If true, cells above threshold render at full opacity.</summary>
    public bool SolidRender = true;

    /// <summary>Render color.</summary>
    public Color FluidColor = new(30, 100, 220, 230);

    /// <summary>Whether simulation is active.</summary>
    public bool Simulating = true;

    /// <summary>Number of flow passes per frame. More = faster settling.</summary>
    public int Passes = 3;

    // Impact detection
    private int[] _prevSurface;   // surface row per column (previous frame)
    private float[] _disturbance; // surface disturbance intensity per column (decays over time)

    /// <summary>Optional particle emitter for splash effects. Set from Lua.</summary>
    public Graphics.ParticleEmitter? SplashEmitter;

    /// <summary>Number of splash particles per impact unit.</summary>
    public int SplashAmount = 3;

    /// <summary>How fast surface disturbance decays per second.</summary>
    public float DisturbanceDecay = 4f;

    /// <summary>Disturbance amplitude multiplier for rendering.</summary>
    public float DisturbanceStrength = 4f;

    public int GridWidth => N;
    public int GridHeight => M;

    private int IX(int i, int j) => i + (N + 2) * j;

    public GridFluidSystem(int gridW, int gridH, float pixelW, float pixelH,
                           float x = 0, float y = 0) : base(x, y)
    {
        N = gridW;
        M = gridH;
        _pixelW = pixelW;
        _pixelH = pixelH;
        _cellW = pixelW / gridW;
        _cellH = pixelH / gridH;

        int size = (N + 2) * (M + 2);
        _water = new float[size];
        _waterNext = new float[size];
        _velX = new float[size];
        _velY = new float[size];
        _prevSurface = new int[gridW + 2];
        _disturbance = new float[gridW + 2];

        BaseWidth = pixelW;
        BaseHeight = pixelH;
        Layer = 3;
    }

    // ── Public API ──────────────────────────────────────────────

    /// <summary>Add water at a grid cell. Triggers splash if cell already has water.</summary>
    public void AddWater(int i, int j, float amount)
    {
        if (i < 1 || i > N || j < 1 || j > M) return;

        float existing = _water[IX(i, j)];
        _water[IX(i, j)] = MathF.Min(existing + amount, MaxLevel * 2f);

        // Impact: adding water to a cell that's already mostly full
        if (existing > 0.3f && amount > 0.05f)
        {
            float intensity = amount * existing;
            _disturbance[i] = MathF.Max(_disturbance[i], MathF.Min(intensity * 3f, 5f));

            // Give upward + sideways VELOCITY to nearby cells (creates real splashing)
            float splashVel = intensity * -15f; // negative = upward
            float sideVel = intensity * 8f;

            // Upward splash velocity at impact point and neighbors
            _velY[IX(i, j)] += splashVel;
            if (i > 1) { _velY[IX(i - 1, j)] += splashVel * 0.5f; _velX[IX(i - 1, j)] -= sideVel; }
            if (i < N) { _velY[IX(i + 1, j)] += splashVel * 0.5f; _velX[IX(i + 1, j)] += sideVel; }
            if (j > 1) _velY[IX(i, j - 1)] += splashVel * 0.7f;

            // Emit splash particles
            if (SplashEmitter != null && intensity > 0.1f)
            {
                float px = Position.X + (i - 1) * _cellW + _cellW * 0.5f;
                // Find the actual surface Y for this column
                float py = Position.Y + (j - 1) * _cellH;
                for (int sj = 1; sj < j; sj++)
                {
                    if (_water[IX(i, sj)] > RenderThreshold)
                    {
                        py = Position.Y + (sj - 1) * _cellH;
                        break;
                    }
                }
                SplashEmitter.Position = new Vec2(px, py);
                SplashEmitter.Emit(System.Math.Max(1, (int)(SplashAmount * intensity)));
            }
        }
    }

    /// <summary>Apply velocity to water in a radius. Use for mouse interaction.</summary>
    public void AddVelocity(int ci, int cj, int radius, float vx, float vy)
    {
        for (int di = -radius; di <= radius; di++)
        {
            for (int dj = -radius; dj <= radius; dj++)
            {
                if (di * di + dj * dj > radius * radius) continue;
                int i = ci + di, j = cj + dj;
                if (i < 1 || i > N || j < 1 || j > M) continue;
                if (_water[IX(i, j)] < 0.05f) continue;

                float falloff = 1f - MathF.Sqrt(di * di + dj * dj) / (radius + 1);
                _velX[IX(i, j)] += vx * falloff;
                _velY[IX(i, j)] += vy * falloff;
            }
        }
    }

    /// <summary>Remove water in a radius (carve through it).</summary>
    public void RemoveWater(int ci, int cj, int radius)
    {
        for (int di = -radius; di <= radius; di++)
        {
            for (int dj = -radius; dj <= radius; dj++)
            {
                if (di * di + dj * dj > radius * radius) continue;
                int i = ci + di, j = cj + dj;
                if (i < 1 || i > N || j < 1 || j > M) continue;
                _water[IX(i, j)] = 0;
                _velX[IX(i, j)] = 0;
                _velY[IX(i, j)] = 0;
            }
        }
    }

    /// <summary>Add water in a circle of grid cells.</summary>
    public void AddWaterCircle(int ci, int cj, int radius, float amount)
    {
        for (int di = -radius; di <= radius; di++)
            for (int dj = -radius; dj <= radius; dj++)
                if (di * di + dj * dj <= radius * radius)
                    AddWater(ci + di, cj + dj, amount);
    }

    /// <summary>Get water level at a grid cell.</summary>
    public float GetWater(int i, int j) => _water[IX(i, j)];

    /// <summary>Total water in the system.</summary>
    public float TotalWater()
    {
        float total = 0;
        for (int i = 1; i <= N; i++)
            for (int j = 1; j <= M; j++)
                total += _water[IX(i, j)];
        return total;
    }

    /// <summary>Convert pixel coordinates to grid cell.</summary>
    public (int i, int j) PixelToGrid(float px, float py)
    {
        int i = (int)((px - Position.X) / _cellW) + 1;
        int j = (int)((py - Position.Y) / _cellH) + 1;
        return (System.Math.Clamp(i, 1, N), System.Math.Clamp(j, 1, M));
    }

    /// <summary>Clear all water.</summary>
    public void Clear()
    {
        Array.Clear(_water);
        Array.Clear(_waterNext);
        Array.Clear(_velX);
        Array.Clear(_velY);
    }

    // ── Update ──────────────────────────────────────────────────

    public override void Update(float dt)
    {
        base.Update(dt);
        if (!Simulating || dt <= 0) return;

        // Record surface levels before simulation
        for (int i = 1; i <= N; i++)
        {
            _prevSurface[i] = M + 1; // default: no water
            for (int j = 1; j <= M; j++)
            {
                if (_water[IX(i, j)] > RenderThreshold)
                {
                    _prevSurface[i] = j;
                    break;
                }
            }
        }

        for (int pass = 0; pass < Passes; pass++)
        {
            float subDt = dt / Passes;
            SimulateFlow(subDt);
        }

        // Detect impacts: where the surface moved down (water arrived from above)
        for (int i = 1; i <= N; i++)
        {
            int newSurface = M + 1;
            for (int j = 1; j <= M; j++)
            {
                if (_water[IX(i, j)] > RenderThreshold)
                {
                    newSurface = j;
                    break;
                }
            }

            int drop = _prevSurface[i] - newSurface; // positive = surface rose (water arrived)
            if (drop > 1)
            {
                // Impact! Surface rose by multiple rows — create splash
                float intensity = MathF.Min(drop / 3f, 3f);
                _disturbance[i] = MathF.Max(_disturbance[i], intensity);

                // Emit splash particles
                if (SplashEmitter != null)
                {
                    float px = Position.X + (i - 1) * _cellW + _cellW * 0.5f;
                    float py = Position.Y + (newSurface - 1) * _cellH;
                    SplashEmitter.Position = new Vec2(px, py);
                    int count = (int)(SplashAmount * intensity);
                    if (count > 0)
                    {
                        SplashEmitter.Emit(count);
                    }
                }
            }
        }

        // Decay disturbance
        for (int i = 1; i <= N; i++)
        {
            _disturbance[i] -= DisturbanceDecay * dt;
            if (_disturbance[i] < 0) _disturbance[i] = 0;
        }
    }

    private void SimulateFlow(float dt)
    {
        // ── Velocity-driven water transfer (momentum/sloshing) ──
        float gravityAccel = 8f; // grid gravity (cells/sec²)
        float velDamping = 0.92f;

        for (int i = 1; i <= N; i++)
        {
            for (int j = 1; j <= M; j++)
            {
                float w = _water[IX(i, j)];
                if (w < 0.01f) { _velX[IX(i, j)] = 0; _velY[IX(i, j)] = 0; continue; }

                // Apply gravity to vertical velocity
                _velY[IX(i, j)] += gravityAccel * dt;

                // Transfer water based on velocity
                float vx = _velX[IX(i, j)];
                float vy = _velY[IX(i, j)];

                // Vertical transfer
                if (vy > 0 && j < M) // downward
                {
                    float transfer = MathF.Min(w * 0.5f, vy * dt * 2f);
                    float space = MaxLevel - _water[IX(i, j + 1)];
                    transfer = MathF.Min(transfer, MathF.Max(space, 0));
                    if (transfer > 0.001f)
                    {
                        _water[IX(i, j)] -= transfer;
                        _water[IX(i, j + 1)] += transfer;
                        _velY[IX(i, j + 1)] = MathF.Max(_velY[IX(i, j + 1)], vy * 0.6f);
                    }
                }
                else if (vy < 0 && j > 1) // upward (splash!)
                {
                    float transfer = MathF.Min(w * 0.4f, -vy * dt * 2f);
                    if (transfer > 0.001f)
                    {
                        _water[IX(i, j)] -= transfer;
                        _water[IX(i, j - 1)] += transfer;
                        _velY[IX(i, j - 1)] = MathF.Min(_velY[IX(i, j - 1)], vy * 0.5f);
                    }
                }

                // Horizontal transfer
                if (vx > 0 && i < N)
                {
                    float transfer = MathF.Min(w * 0.3f, vx * dt);
                    float space = MaxLevel - _water[IX(i + 1, j)];
                    transfer = MathF.Min(transfer, MathF.Max(space, 0));
                    if (transfer > 0.001f)
                    {
                        _water[IX(i, j)] -= transfer;
                        _water[IX(i + 1, j)] += transfer;
                        _velX[IX(i + 1, j)] += vx * 0.3f;
                    }
                }
                else if (vx < 0 && i > 1)
                {
                    float transfer = MathF.Min(w * 0.3f, -vx * dt);
                    float space = MaxLevel - _water[IX(i - 1, j)];
                    transfer = MathF.Min(transfer, MathF.Max(space, 0));
                    if (transfer > 0.001f)
                    {
                        _water[IX(i, j)] -= transfer;
                        _water[IX(i - 1, j)] += transfer;
                        _velX[IX(i - 1, j)] += vx * 0.3f;
                    }
                }

                // Damping
                _velX[IX(i, j)] *= velDamping;
                _velY[IX(i, j)] *= velDamping;

                // Wall bounce
                if (i <= 1 && vx < 0) _velX[IX(i, j)] = -vx * 0.3f;
                if (i >= N && vx > 0) _velX[IX(i, j)] = -vx * 0.3f;
                if (j <= 1 && vy < 0) _velY[IX(i, j)] = -vy * 0.3f;
                if (j >= M && vy > 0) _velY[IX(i, j)] = -vy * 0.3f;
            }
        }

        // ── Static flow rules (gravity settling + lateral spread) ──
        Array.Copy(_water, _waterNext, _water.Length);

        float downRate = FlowRate * dt * 60f;
        float sideRate = SpreadRate * dt * 60f;

        // Process from bottom to top so water settles in one pass
        for (int j = M; j >= 1; j--)
        {
            for (int i = 1; i <= N; i++)
            {
                float w = _waterNext[IX(i, j)];
                if (w <= 0) continue;

                // ── Flow DOWN ──
                if (j < M)
                {
                    float below = _waterNext[IX(i, j + 1)];
                    float space = MaxLevel - below;
                    if (space > 0)
                    {
                        float flow = MathF.Min(w, MathF.Min(space, downRate));
                        _waterNext[IX(i, j)] -= flow;
                        _waterNext[IX(i, j + 1)] += flow;
                        w = _waterNext[IX(i, j)];
                        if (w <= 0) continue;
                    }
                }

                // ── Flow SIDEWAYS (equalize with neighbors) ──
                float left = (i > 1) ? _waterNext[IX(i - 1, j)] : MaxLevel; // wall = full (blocks flow)
                float right = (i < N) ? _waterNext[IX(i + 1, j)] : MaxLevel;

                // Only flow to cells with lower water level
                if (w > left + 0.001f && i > 1)
                {
                    float diff = (w - left) * 0.5f;
                    float flow = MathF.Min(diff, sideRate);
                    _waterNext[IX(i, j)] -= flow;
                    _waterNext[IX(i - 1, j)] += flow;
                }

                w = _waterNext[IX(i, j)];
                if (w > right + 0.001f && i < N)
                {
                    float diff = (w - right) * 0.5f;
                    float flow = MathF.Min(diff, sideRate);
                    _waterNext[IX(i, j)] -= flow;
                    _waterNext[IX(i + 1, j)] += flow;
                }

                // ── Pressure: if cell below is full, push sideways harder ──
                if (j < M && _waterNext[IX(i, j + 1)] >= MaxLevel * 0.99f && w > 0.01f)
                {
                    // Water is supported from below — spread laterally under pressure
                    float pressure = w * sideRate * 0.5f;

                    if (i > 1 && _waterNext[IX(i - 1, j)] < w)
                    {
                        float flow = MathF.Min(pressure, w * 0.25f);
                        _waterNext[IX(i, j)] -= flow;
                        _waterNext[IX(i - 1, j)] += flow;
                    }
                    w = _waterNext[IX(i, j)];
                    if (i < N && _waterNext[IX(i + 1, j)] < w)
                    {
                        float flow = MathF.Min(pressure, w * 0.25f);
                        _waterNext[IX(i, j)] -= flow;
                        _waterNext[IX(i + 1, j)] += flow;
                    }
                }
            }
        }

        // ── Deep equalization ──
        // Only equalize cells that are SUBMERGED (cell above has water too).
        // Surface cells are left free to have different levels — this allows waves and sloshing.
        // Sweep left-to-right:
        for (int j = 2; j <= M; j++) // start at row 2 so we can check row above
        {
            for (int i = 2; i <= N; i++)
            {
                float a = _waterNext[IX(i - 1, j)];
                float b = _waterNext[IX(i, j)];
                if (a < 0.5f && b < 0.5f) continue;

                // Only equalize if BOTH cells are submerged (have water above)
                bool aSubmerged = _waterNext[IX(i - 1, j - 1)] > 0.5f;
                bool bSubmerged = _waterNext[IX(i, j - 1)] > 0.5f;
                if (!aSubmerged || !bSubmerged) continue;

                float avg = (a + b) * 0.5f;
                _waterNext[IX(i - 1, j)] = avg;
                _waterNext[IX(i, j)] = avg;
            }
        }
        // Sweep right-to-left for symmetry
        for (int j = 2; j <= M; j++)
        {
            for (int i = N - 1; i >= 1; i--)
            {
                float a = _waterNext[IX(i, j)];
                float b = _waterNext[IX(i + 1, j)];
                if (a < 0.5f && b < 0.5f) continue;

                bool aSubmerged = _waterNext[IX(i, j - 1)] > 0.5f;
                bool bSubmerged = _waterNext[IX(i + 1, j - 1)] > 0.5f;
                if (!aSubmerged || !bSubmerged) continue;

                float avg = (a + b) * 0.5f;
                _waterNext[IX(i, j)] = avg;
                _waterNext[IX(i + 1, j)] = avg;
            }
        }

        // Swap buffers
        Array.Copy(_waterNext, _water, _water.Length);
    }

    // ── Render Colors ──────────────────────────────────────────

    /// <summary>Deep water color (bottom of body).</summary>
    public Color DeepColor = new(10, 40, 140, 240);

    /// <summary>Surface highlight color (bright line at water surface).</summary>
    public Color SurfaceColor = new(140, 200, 255, 255);

    /// <summary>Surface line thickness in pixels.</summary>
    public float SurfaceThickness = 2f;

    /// <summary>Animate a gentle wave at the surface.</summary>
    public bool WaveEnabled = true;

    /// <summary>Wave amplitude in pixels.</summary>
    public float WaveAmplitude = 1.5f;

    /// <summary>Wave frequency (cycles across the grid).</summary>
    public float WaveFrequency = 0.15f;

    private float _time;

    // ── Render ──────────────────────────────────────────────────

    public override void Draw()
    {
        _time += Eng.Time.Elapsed;

        float ox = Position.X;
        float oy = Position.Y;
        float gridBottom = M * _cellH;

        // Colors
        float sr = FluidColor.R / 255f, sg = FluidColor.G / 255f, sb = FluidColor.B / 255f;
        float dr = DeepColor.R / 255f, dg = DeepColor.G / 255f, db = DeepColor.B / 255f;
        float baseA = FluidColor.A / 255f;
        float deepA = DeepColor.A / 255f;
        float hlR = SurfaceColor.R / 255f, hlG = SurfaceColor.G / 255f, hlB = SurfaceColor.B / 255f;
        float hlA = SurfaceColor.A / 255f;

        for (int i = 1; i <= N; i++)
        {
            float px = ox + (i - 1) * _cellW;
            bool prevHadWater = false;

            for (int j = 1; j <= M; j++)
            {
                float w = _water[IX(i, j)];
                bool hasWater = w > RenderThreshold;

                if (!hasWater)
                {
                    prevHadWater = false;
                    continue;
                }

                float level = MathF.Min(w / MaxLevel, 1f);
                float cellTop = oy + (j - 1) * _cellH;
                float cellBot = cellTop + _cellH;

                // Depth fraction: 0 at top of grid, 1 at bottom
                float depthFrac = (float)j / M;

                // Lerp color by depth
                float cr = sr + (dr - sr) * depthFrac;
                float cg = sg + (dg - sg) * depthFrac;
                float cb = sb + (db - sb) * depthFrac;
                float ca = baseA + (deepA - baseA) * depthFrac;

                bool isSurface = !prevHadWater; // top of a water segment

                if (isSurface && level < 0.99f)
                {
                    // Partial surface cell — draw from bottom of cell
                    float h = _cellH * level;
                    float drawY = cellBot - h;

                    // Wave displacement on surface
                    if (WaveEnabled)
                    {
                        float wave = MathF.Sin((i * WaveFrequency + _time * 2f) * MathF.PI * 2f) * WaveAmplitude;
                        // Add disturbance ripple at impact points
                        float dist = _disturbance[i] * DisturbanceStrength;
                        if (dist > 0)
                            wave += MathF.Sin((i * 0.5f + _time * 8f) * MathF.PI * 2f) * dist;
                        drawY += wave;
                    }

                    Eng.GL.FillRectWorld(px, drawY, _cellW + 1f, h + 1f,
                        ScrollFactor, cr, cg, cb, ca);

                    // Surface highlight
                    if (SurfaceThickness > 0)
                        Eng.GL.FillRectWorld(px, drawY - 1f, _cellW + 1f, SurfaceThickness,
                            ScrollFactor, hlR, hlG, hlB, hlA);
                }
                else
                {
                    // Full cell or submerged cell
                    float drawY = cellTop;

                    if (isSurface)
                    {
                        // Full surface cell — add wave
                        if (WaveEnabled)
                            drawY += MathF.Sin((i * WaveFrequency + _time * 2f) * MathF.PI * 2f) * WaveAmplitude;

                        // Surface highlight
                        if (SurfaceThickness > 0)
                            Eng.GL.FillRectWorld(px, drawY - 1f, _cellW + 1f, SurfaceThickness,
                                ScrollFactor, hlR, hlG, hlB, hlA);
                    }

                    float h = cellBot - drawY;
                    Eng.GL.FillRectWorld(px, drawY, _cellW + 1f, h + 1f,
                        ScrollFactor, cr, cg, cb, ca);
                }

                prevHadWater = true;
            }
        }
    }
}
