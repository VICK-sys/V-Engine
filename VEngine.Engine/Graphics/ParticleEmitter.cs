using System;
using System.Collections.Generic;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Lightweight particle system. Add to a Scene, configure properties, then call Emit().
/// Particles are self-contained — no extra entities added to the scene.
/// Supports both colored rectangles (default) and textured sprites.
/// </summary>
public class ParticleEmitter : Entity
{
    private readonly List<Particle> _particles = new();
    private readonly Random _rng;

    /// <summary>Random seed for this emitter. Pass a fixed seed for deterministic particles.</summary>
    public int Seed { get; }

    // Texture (optional — null = colored rectangles)
    private GLTexture? _texture;
    private int _frameW, _frameH, _frameCols, _frameCount;

    // Spawn area (offset from Position)
    public float SpawnWidth;
    public float SpawnHeight;

    // Velocity range
    public float MinSpeedX = -20f, MaxSpeedX = 20f;
    public float MinSpeedY = -40f, MaxSpeedY = -10f;

    // Gravity applied to particles
    public float GravityY;

    // Lifetime range (seconds)
    public float MinLife = 0.3f, MaxLife = 0.8f;

    // Size range (pixels)
    public int MinSize = 2, MaxSize = 4;

    // Color range (random between min and max)
    public Math.Color ColorMin = new(180, 160, 140, 200);
    public Math.Color ColorMax = new(220, 200, 180, 255);

    // Scale over lifetime: start -> end
    public float ScaleStart = 1f, ScaleEnd = 0f;

    // Alpha fade: if true, alpha fades to 0 over lifetime
    public bool FadeOut = true;

    /// <summary>Blend mode for all particles. Use Additive for fire, magic, glow effects.</summary>
    public BlendMode BlendMode = BlendMode.Alpha;

    /// <summary>Angular velocity range in degrees/sec. Particles spin between min and max.</summary>
    public float MinAngularVelocity, MaxAngularVelocity;

    public ParticleEmitter(float x = 0, float y = 0, int? seed = null) : base(x, y)
    {
        Seed = seed ?? Environment.TickCount ^ GetHashCode();
        _rng = new Random(Seed);
    }

    /// <summary>Load a single texture for particles. Each particle draws as this texture.</summary>
    public ParticleEmitter LoadTexture(string path)
    {
        if (!System.IO.Path.IsPathRooted(path))
            path = Eng.Asset(path);
        _texture = Eng.GL.GetOrCreateTexture(path);
        _frameW = _texture.Width;
        _frameH = _texture.Height;
        _frameCols = 1;
        _frameCount = 1;
        return this;
    }

    /// <summary>Load a spritesheet for particles. Each particle picks a random frame.</summary>
    public ParticleEmitter LoadTexture(string path, int frameWidth, int frameHeight)
    {
        if (!System.IO.Path.IsPathRooted(path))
            path = Eng.Asset(path);
        _texture = Eng.GL.GetOrCreateTexture(path);
        _frameW = frameWidth;
        _frameH = frameHeight;
        _frameCols = System.Math.Max(1, _texture.Width / frameWidth);
        int rows = System.Math.Max(1, _texture.Height / frameHeight);
        _frameCount = _frameCols * rows;
        return this;
    }

    /// <summary>
    /// Burst a number of particles from the emitter's position.
    /// </summary>
    public void Emit(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var p = new Particle
            {
                X = Position.X + RandF(-SpawnWidth / 2, SpawnWidth / 2),
                Y = Position.Y + RandF(-SpawnHeight / 2, SpawnHeight / 2),
                VX = RandF(MinSpeedX, MaxSpeedX),
                VY = RandF(MinSpeedY, MaxSpeedY),
                Life = RandF(MinLife, MaxLife),
                Age = 0,
                Size = _rng.Next(MinSize, MaxSize + 1),
                R = RandByte(ColorMin.R, ColorMax.R),
                G = RandByte(ColorMin.G, ColorMax.G),
                B = RandByte(ColorMin.B, ColorMax.B),
                A = RandByte(ColorMin.A, ColorMax.A),
                Frame = _frameCount > 1 ? _rng.Next(_frameCount) : 0,
                AngVel = RandF(MinAngularVelocity, MaxAngularVelocity),
            };
            _particles.Add(p);
        }
    }

    /// <summary>Number of active particles.</summary>
    public int Count => _particles.Count;

    public override void Update(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                // Swap-with-last for O(1) removal (order doesn't matter for particles)
                _particles[i] = _particles[^1];
                _particles.RemoveAt(_particles.Count - 1);
                continue;
            }

            p.VY += GravityY * dt;
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            p.Angle += p.AngVel * dt;
            _particles[i] = p;
        }
    }

    public override void Draw()
    {
        var cam = Eng.Camera;

        foreach (var p in _particles)
        {
            float t = p.Age / p.Life;
            float scale = ScaleStart + (ScaleEnd - ScaleStart) * t;
            int size = System.Math.Max(1, (int)(p.Size * scale));
            float alpha = FadeOut ? p.A / 255f * (1f - t) : p.A / 255f;

            var screen = cam.Transform(p.X, p.Y, ScrollFactor);
            float drawSize = size * cam.Zoom;
            float half = drawSize / 2f;

            if (_texture != null)
            {
                int col = p.Frame % _frameCols;
                int row = p.Frame / _frameCols;
                Eng.GL.DrawTexture(_texture,
                    col * _frameW, row * _frameH, _frameW, _frameH,
                    screen.X - half, screen.Y - half, drawSize, drawSize,
                    p.R / 255f, p.G / 255f, p.B / 255f, alpha,
                    angle: p.Angle, blend: BlendMode);
            }
            else
            {
                Eng.GL.FillRect(
                    screen.X - half, screen.Y - half, drawSize, drawSize,
                    p.R / 255f, p.G / 255f, p.B / 255f, alpha);
            }
        }
    }

    private float RandF(float min, float max) =>
        min + (float)_rng.NextDouble() * (max - min);

    private byte RandByte(byte min, byte max) =>
        (byte)(min + _rng.Next(max - min + 1));

    private struct Particle
    {
        public float X, Y, VX, VY;
        public float Life, Age;
        public int Size;
        public byte R, G, B, A;
        public int Frame;
        public float Angle, AngVel;
    }
}
