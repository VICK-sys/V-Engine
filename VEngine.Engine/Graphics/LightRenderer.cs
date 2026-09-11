using System;
using System.Collections.Generic;
using Silk.NET.OpenGL;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;
using VEngine.Engine.Math;

namespace VEngine.Engine.Graphics;

/// <summary>
/// A light source in the scene. Create via LightRenderer.AddLight().
/// </summary>
public class Light
{
    /// <summary>World-space position of the light.</summary>
    public Vec2 Position;

    /// <summary>Maximum radius in world pixels.</summary>
    public float Radius = 100;

    /// <summary>Light color.</summary>
    public Color Color = Color.White;

    /// <summary>Brightness multiplier (0-1+).</summary>
    public float Intensity = 1f;

    /// <summary>Whether this light is active.</summary>
    public bool Active = true;

    /// <summary>Whether this light casts shadows against tilemaps.</summary>
    public bool CastShadows;

    /// <summary>If true, this is a directional spot light instead of an omni point light.</summary>
    public bool IsSpot;

    /// <summary>Spot light direction in degrees (0 = right, 90 = down).</summary>
    public float Direction;

    /// <summary>Spot light cone half-angle in degrees.</summary>
    public float ConeAngle = 45;

    public Light(float x, float y, float radius, Color color)
    {
        Position = new Vec2(x, y);
        Radius = radius;
        Color = color;
    }
}

/// <summary>
/// 2D lighting system. Renders point and spot lights to an FBO, then multiplies
/// over the scene. Supports shadow casting against tilemap solid tiles.
///
/// Usage:
///   var lights = new LightRenderer();
///   lights.Ambient = new Color(20, 20, 30);
///   var torch = lights.AddLight(100, 200, 150, Color.Orange);
///   torch.CastShadows = true;
///   // In Scene.Draw(), after drawing game objects:
///   lights.Render(tilemap);
///   // In Scene.OnDestroy():
///   lights.Dispose();
/// </summary>
public class LightRenderer : IDisposable
{
    private GL.Framebuffer? _fbo;
    private readonly List<Light> _lights = new();
    private readonly List<(Vec2 A, Vec2 B)> _edgeCache = new();
    private readonly List<float> _angleCache = new();
    private readonly List<Vec2> _polyCache = new();

    /// <summary>Ambient light color. Black = full darkness, White = full daylight (no effect).</summary>
    public Color Ambient = new(20, 20, 30);

    /// <summary>Master enable. When false, Render() is a no-op.</summary>
    public bool Enabled = true;

    /// <summary>Number of circle segments for point lights (higher = smoother).</summary>
    public int LightSegments = 32;

    /// <summary>All registered lights.</summary>
    public IReadOnlyList<Light> Lights => _lights;

    // ── Public API ────────────────────────────────────────────

    /// <summary>Add a point light and return it for configuration.</summary>
    public Light AddLight(float x, float y, float radius, Color color)
    {
        var light = new Light(x, y, radius, color);
        _lights.Add(light);
        return light;
    }

    /// <summary>Add a pre-configured light.</summary>
    public void AddLight(Light light) => _lights.Add(light);

    /// <summary>Remove a light.</summary>
    public void RemoveLight(Light light) => _lights.Remove(light);

    /// <summary>Remove all lights.</summary>
    public void ClearLights() => _lights.Clear();

    /// <summary>
    /// Render the light map and composite it over the scene.
    /// Call from Scene.Draw() after all game objects are drawn.
    /// Pass a tilemap for shadow casting (optional).
    /// </summary>
    public void Render(Tilemap? tilemap = null)
    {
        if (!Enabled) return;

        EnsureFBO();
        var gl = Eng.GL;

        // Flush scene draws before switching to light FBO
        gl.FlushAll();

        // 1. Bind light map, clear to ambient
        _fbo!.Bind();
        gl.Api.ClearColor(Ambient.R / 255f, Ambient.G / 255f, Ambient.B / 255f, 1f);
        gl.Api.Clear(ClearBufferMask.ColorBufferBit);

        // Set projection for the FBO (same as main screen)
        gl.PrimitiveShader.Use();
        gl.PrimitiveShader.SetMatrix4("uProjection", gl.Projection);

        // 2. Render each light with additive blending
        gl.Primitives.Blend = BlendMode.Additive;

        foreach (var light in _lights)
        {
            if (!light.Active || light.Radius <= 0 || light.Intensity <= 0) continue;

            if (light.CastShadows && tilemap != null)
                RenderLightWithShadows(light, tilemap);
            else if (light.IsSpot)
                RenderSpotLight(light);
            else
                RenderPointLight(light);
        }

        gl.Primitives.Flush();
        gl.Primitives.Blend = BlendMode.Alpha;

        // 3. Unbind light map, restore viewport
        _fbo.Unbind();
        gl.RestoreViewport();

        // 4. Composite: multiply light map over the scene
        gl.SpriteShader.Use();
        gl.SpriteShader.SetMatrix4("uProjection", gl.ActiveProjection);
        gl.DrawTexture(_fbo.ColorAttachment,
            0, 0, Eng.Width, Eng.Height,
            0, 0, Eng.Width, Eng.Height,
            1, 1, 1, 1, flipY: true, blend: BlendMode.Multiply);
        gl.FlushAll();
    }

    public void Dispose()
    {
        _fbo?.Dispose();
        _fbo = null;
    }

    // ── Point Light ───────────────────────────────────────────

    private void RenderPointLight(Light light)
    {
        var cam = Eng.Camera;
        var center = cam.WorldToScreen(light.Position.X, light.Position.Y);
        float r = light.Radius * cam.Zoom;

        float cr = light.Color.R / 255f * light.Intensity;
        float cg = light.Color.G / 255f * light.Intensity;
        float cb = light.Color.B / 255f * light.Intensity;

        float step = MathF.PI * 2f / LightSegments;
        var prims = Eng.GL.Primitives;

        for (int i = 0; i < LightSegments; i++)
        {
            float a0 = i * step, a1 = (i + 1) * step;
            prims.DrawTriangle(
                center.X, center.Y, cr, cg, cb, 1f,
                center.X + MathF.Cos(a0) * r, center.Y + MathF.Sin(a0) * r, 0, 0, 0, 0,
                center.X + MathF.Cos(a1) * r, center.Y + MathF.Sin(a1) * r, 0, 0, 0, 0);
        }
    }

    // ── Spot Light ────────────────────────────────────────────

    private void RenderSpotLight(Light light)
    {
        var cam = Eng.Camera;
        var center = cam.WorldToScreen(light.Position.X, light.Position.Y);
        float r = light.Radius * cam.Zoom;

        float cr = light.Color.R / 255f * light.Intensity;
        float cg = light.Color.G / 255f * light.Intensity;
        float cb = light.Color.B / 255f * light.Intensity;

        float halfAngle = light.ConeAngle * MathF.PI / 180f;
        float dir = light.Direction * MathF.PI / 180f;
        float startAngle = dir - halfAngle;
        float endAngle = dir + halfAngle;

        int segments = System.Math.Max(4, (int)(halfAngle * 2f / (MathF.PI * 2f) * LightSegments) + 4);
        float step = (endAngle - startAngle) / segments;
        var prims = Eng.GL.Primitives;

        for (int i = 0; i < segments; i++)
        {
            float a0 = startAngle + i * step;
            float a1 = startAngle + (i + 1) * step;
            prims.DrawTriangle(
                center.X, center.Y, cr, cg, cb, 1f,
                center.X + MathF.Cos(a0) * r, center.Y + MathF.Sin(a0) * r, 0, 0, 0, 0,
                center.X + MathF.Cos(a1) * r, center.Y + MathF.Sin(a1) * r, 0, 0, 0, 0);
        }
    }

    // ── Shadow Casting (Visibility Polygon) ───────────────────

    private void RenderLightWithShadows(Light light, Tilemap tilemap)
    {
        _polyCache.Clear();
        ComputeVisibilityPolygon(light, tilemap, _polyCache);

        if (_polyCache.Count < 2) return;

        var cam = Eng.Camera;
        var center = cam.WorldToScreen(light.Position.X, light.Position.Y);

        float cr = light.Color.R / 255f * light.Intensity;
        float cg = light.Color.G / 255f * light.Intensity;
        float cb = light.Color.B / 255f * light.Intensity;

        float maxR = light.Radius * cam.Zoom;
        var prims = Eng.GL.Primitives;

        for (int i = 0; i < _polyCache.Count; i++)
        {
            int j = (i + 1) % _polyCache.Count;
            var s0 = cam.WorldToScreen(_polyCache[i].X, _polyCache[i].Y);
            var s1 = cam.WorldToScreen(_polyCache[j].X, _polyCache[j].Y);

            // Falloff based on distance from light center
            float d0 = Distance(center, s0) / maxR;
            float d1 = Distance(center, s1) / maxR;
            float f0 = System.Math.Max(0, 1f - d0);
            float f1 = System.Math.Max(0, 1f - d1);

            prims.DrawTriangle(
                center.X, center.Y, cr, cg, cb, 1f,
                s0.X, s0.Y, cr * f0, cg * f0, cb * f0, f0,
                s1.X, s1.Y, cr * f1, cg * f1, cb * f1, f1);
        }
    }

    private void ComputeVisibilityPolygon(Light light, Tilemap tilemap, List<Vec2> polygon)
    {
        // 1. Collect occluder edges from solid tiles within light radius
        _edgeCache.Clear();
        CollectEdges(light, tilemap, _edgeCache);

        // 2. Collect ray angles: toward each edge vertex + small offsets + boundary angles
        _angleCache.Clear();
        float lx = light.Position.X, ly = light.Position.Y;

        foreach (var (a, b) in _edgeCache)
        {
            float ang1 = MathF.Atan2(a.Y - ly, a.X - lx);
            float ang2 = MathF.Atan2(b.Y - ly, b.X - lx);
            _angleCache.Add(ang1 - 0.0001f);
            _angleCache.Add(ang1);
            _angleCache.Add(ang1 + 0.0001f);
            _angleCache.Add(ang2 - 0.0001f);
            _angleCache.Add(ang2);
            _angleCache.Add(ang2 + 0.0001f);
        }

        // Boundary rays for full circle coverage
        int boundaryRays = LightSegments;
        for (int i = 0; i < boundaryRays; i++)
            _angleCache.Add(i * MathF.PI * 2f / boundaryRays);

        _angleCache.Sort();

        // 3. Cast rays, find closest intersection
        float maxR = light.Radius;
        bool isSpot = light.IsSpot;
        float spotDir = 0, spotHalf = 0;
        if (isSpot)
        {
            spotDir = light.Direction * MathF.PI / 180f;
            spotHalf = light.ConeAngle * MathF.PI / 180f;
        }

        for (int i = 0; i < _angleCache.Count; i++)
        {
            float angle = _angleCache[i];

            // Skip angles outside spot cone
            if (isSpot)
            {
                float diff = AngleDiff(angle, spotDir);
                if (diff > spotHalf) continue;
            }

            float dx = MathF.Cos(angle);
            float dy = MathF.Sin(angle);
            float closest = maxR;

            foreach (var (a, b) in _edgeCache)
            {
                if (RaySegmentIntersect(lx, ly, dx, dy, a.X, a.Y, b.X, b.Y, out float dist))
                {
                    if (dist < closest) closest = dist;
                }
            }

            polygon.Add(new Vec2(lx + dx * closest, ly + dy * closest));
        }
    }

    private void CollectEdges(Light light, Tilemap tilemap, List<(Vec2 A, Vec2 B)> edges)
    {
        float lx = light.Position.X, ly = light.Position.Y, r = light.Radius;
        var (startCol, startRow) = tilemap.WorldToTile(lx - r, ly - r);
        var (endCol, endRow) = tilemap.WorldToTile(lx + r, ly + r);

        startCol = System.Math.Max(0, startCol);
        startRow = System.Math.Max(0, startRow);
        endCol = System.Math.Min(tilemap.GridWidth - 1, endCol);
        endRow = System.Math.Min(tilemap.GridHeight - 1, endRow);

        int ts = tilemap.TileSize;
        float ox = tilemap.Position.X, oy = tilemap.Position.Y;

        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                if (!tilemap.IsSolid(col, row)) continue;
                float x = ox + col * ts, y = oy + row * ts;

                if (!tilemap.IsSolid(col, row - 1))
                    edges.Add((new Vec2(x, y), new Vec2(x + ts, y)));
                if (!tilemap.IsSolid(col, row + 1))
                    edges.Add((new Vec2(x, y + ts), new Vec2(x + ts, y + ts)));
                if (!tilemap.IsSolid(col - 1, row))
                    edges.Add((new Vec2(x, y), new Vec2(x, y + ts)));
                if (!tilemap.IsSolid(col + 1, row))
                    edges.Add((new Vec2(x + ts, y), new Vec2(x + ts, y + ts)));
            }
        }
    }

    // ── Math Helpers ──────────────────────────────────────────

    private static bool RaySegmentIntersect(
        float rx, float ry, float rdx, float rdy,
        float ax, float ay, float bx, float by,
        out float dist)
    {
        float sdx = bx - ax, sdy = by - ay;
        float denom = rdx * sdy - rdy * sdx;
        if (MathF.Abs(denom) < 1e-8f) { dist = float.MaxValue; return false; }

        float t = ((ax - rx) * sdy - (ay - ry) * sdx) / denom;
        float u = ((ax - rx) * rdy - (ay - ry) * rdx) / denom;

        if (t > 0 && u >= 0 && u <= 1)
        {
            dist = t;
            return true;
        }
        dist = float.MaxValue;
        return false;
    }

    private static float AngleDiff(float a, float b)
    {
        float d = MathF.Abs(a - b) % (MathF.PI * 2f);
        return d > MathF.PI ? MathF.PI * 2f - d : d;
    }

    private static float Distance(Vec2 a, Vec2 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private void EnsureFBO()
    {
        if (_fbo != null && _fbo.Width == Eng.Width && _fbo.Height == Eng.Height) return;
        _fbo?.Dispose();
        _fbo = new GL.Framebuffer(Eng.GL.Api, Eng.Width, Eng.Height);
    }
}
