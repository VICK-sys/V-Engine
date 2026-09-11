using System;
using System.IO;
using SDL2;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Core;

/// <summary>
/// Debug overlay: FPS counter, entity/tween/timer stats, collision bounds visualization.
/// Toggle with F3 or set Eng.Debug.Enabled programmatically.
/// </summary>
public class DebugOverlay
{
    private bool _enabled;

    // FPS tracking
    private int _frameCount;
    private float _fpsAccum;
    private float _currentFps;

    // Text rendering
    private IntPtr _font;
    private GLTexture? _fpsTexture;
    private GLTexture? _statsTexture;
    private GLTexture? _memTexture;
    private string _lastFpsText = "";
    private string _lastStatsText = "";
    private string _lastMemText = "";
    private int _fpsW, _fpsH;
    private int _statsW, _statsH;
    private int _memW, _memH;

    /// <summary>Memory usage threshold in MB. Above this, memory text turns red.</summary>
    public float MemoryWarningMB = 256f;

    public bool Enabled { get => _enabled; set => _enabled = value; }

    public void Toggle() => _enabled = !_enabled;

    internal void Draw(Scene? scene, float realDt)
    {
        if (!_enabled || scene == null) return;

        // FPS tracking
        _frameCount++;
        _fpsAccum += realDt;
        if (_fpsAccum >= 0.5f)
        {
            _currentFps = _frameCount / _fpsAccum;
            _frameCount = 0;
            _fpsAccum = 0;
        }

        // Collision bounds (drawn first so text is on top)
        DrawCollisionBounds(scene);

        // Text overlay
        EnsureFont();
        if (_font == IntPtr.Zero) return;

        string fpsText = $"FPS: {_currentFps:F0}";
        int entityCount = CountEntities(scene);
        int drawCalls = Eng.GL.Sprites.DrawCalls + Eng.GL.Primitives.DrawCalls;
        string statsText = $"Entities: {entityCount}  Draws: {drawCalls}  Tweens: {Eng.Tweens.Count}  Timers: {Eng.Timers.Count}";

        float memMB = GC.GetTotalMemory(false) / (1024f * 1024f);
        bool memHigh = memMB >= MemoryWarningMB;
        string memText = $"Memory: {memMB:F1} MB";

        var green = new Math.Color(0, 255, 0, 220);
        var red = new Math.Color(255, 60, 60, 220);

        UpdateTexture(fpsText, ref _fpsTexture, ref _lastFpsText, ref _fpsW, ref _fpsH, green);
        UpdateTexture(statsText, ref _statsTexture, ref _lastStatsText, ref _statsW, ref _statsH, green);
        UpdateTexture(memText, ref _memTexture, ref _lastMemText, ref _memW, ref _memH, memHigh ? red : green);

        // Background
        int bgW = System.Math.Max(System.Math.Max(_fpsW, _statsW), _memW) + 14;
        int bgH = _fpsH + _statsH + _memH + 16;
        Eng.GL.FillRect(0, 0, bgW, bgH, 0, 0, 0, 160 / 255f);

        // Draw text at fixed screen coordinates
        int y = 4;
        if (_fpsTexture != null)
        {
            Eng.GL.DrawTexture(_fpsTexture,
                0, 0, _fpsW, _fpsH,
                5, y, _fpsW, _fpsH,
                1, 1, 1, 1);
            y += _fpsH + 2;
        }
        if (_memTexture != null)
        {
            Eng.GL.DrawTexture(_memTexture,
                0, 0, _memW, _memH,
                5, y, _memW, _memH,
                1, 1, 1, 1);
            y += _memH + 2;
        }
        if (_statsTexture != null)
        {
            Eng.GL.DrawTexture(_statsTexture,
                0, 0, _statsW, _statsH,
                5, y, _statsW, _statsH,
                1, 1, 1, 1);
        }
    }

    private void EnsureFont()
    {
        if (_font != IntPtr.Zero) return;
        try { _font = Graphics.FontCache.GetDefault(14); }
        catch (Exception ex) { Console.WriteLine($"[Debug] Font load failed: {ex.Message}"); }
    }

    private void UpdateTexture(string text, ref GLTexture? texture, ref string cached, ref int w, ref int h, Math.Color color)
    {
        if (text == cached && texture != null) return;

        texture?.Dispose();

        var surface = SDL_ttf.TTF_RenderUTF8_Blended(_font, text, color);
        if (surface == IntPtr.Zero) { texture = null; return; }

        // FromSurface reads pixels and frees the surface
        texture = GLTexture.FromSurface(Eng.GL.Api, surface);
        w = texture.Width;
        h = texture.Height;
        cached = text;
    }

    private void DrawCollisionBounds(Scene scene)
    {
        foreach (var e in scene.Entities)
            DrawBoundsRecursive(e);
    }

    private void DrawBoundsRecursive(Entity entity)
    {
        if (!entity.Active) return;

        if (entity is Group group)
        {
            foreach (var m in group.Members)
                DrawBoundsRecursive(m);
            return;
        }

        var b = entity.GetCollisionBounds();
        if (b.W <= 0 || b.H <= 0) return;

        var screen = Eng.Camera.WorldToScreen(b.X, b.Y);
        float rw = b.W * Eng.Camera.Zoom;
        float rh = b.H * Eng.Camera.Zoom;

        // Off-screen cull
        if (screen.X + rw < 0 || screen.X > Eng.Width ||
            screen.Y + rh < 0 || screen.Y > Eng.Height)
            return;

        if (entity.Immovable)
            Eng.GL.DrawRect(screen.X, screen.Y, rw, rh, 0, 180 / 255f, 180 / 255f, 150 / 255f);
        else
            Eng.GL.DrawRect(screen.X, screen.Y, rw, rh, 0, 1f, 0, 150 / 255f);
    }

    private int CountEntities(Scene scene)
    {
        int count = 0;
        foreach (var e in scene.Entities)
            count += CountEntity(e);
        return count;
    }

    private int CountEntity(Entity e)
    {
        if (e is Group g)
        {
            int count = 0;
            foreach (var m in g.Members)
                count += CountEntity(m);
            return count;
        }
        return 1;
    }

    // ── Performance Trace ────────────────────────────────────

    private StreamWriter? _traceWriter;
    private bool _tracing;
    private float _traceTime;

    /// <summary>True when a performance trace is being recorded.</summary>
    public bool Tracing => _tracing;

    /// <summary>Start recording a performance trace to a CSV file.</summary>
    public void StartTrace(string? path = null)
    {
        if (_tracing) StopTrace();

        path ??= Path.Combine(AppContext.BaseDirectory, $"trace_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        _traceWriter = new StreamWriter(path, false) { AutoFlush = true };
        _traceWriter.WriteLine("time_s,fps,frame_ms,memory_mb,gc_gen0,gc_gen1,gc_gen2");
        _tracing = true;
        _traceTime = 0;
        Console.WriteLine($"[Trace] Recording to {path}");
    }

    /// <summary>Stop recording and close the trace file.</summary>
    public void StopTrace()
    {
        if (!_tracing) return;
        _tracing = false;
        _traceWriter?.Flush();
        _traceWriter?.Dispose();
        _traceWriter = null;
        Console.WriteLine("[Trace] Stopped.");
    }

    internal void TraceFrame(float realDt)
    {
        if (!_tracing || _traceWriter == null) return;

        _traceTime += realDt;
        float fps = realDt > 0.0001f ? 1f / realDt : 0;
        float frameMs = realDt * 1000f;
        float memMB = GC.GetTotalMemory(false) / (1024f * 1024f);
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);

        _traceWriter.WriteLine($"{_traceTime:F3},{fps:F1},{frameMs:F2},{memMB:F1},{gen0},{gen1},{gen2}");
    }

    internal void Shutdown()
    {
        StopTrace();
        _fpsTexture?.Dispose();
        _statsTexture?.Dispose();
        _memTexture?.Dispose();
        // Font owned by FontCache — freed in FontCache.Shutdown()
    }
}
