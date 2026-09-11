using System;
using SDL2;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Core;

/// <summary>
/// Volume HUD overlay. Shows a bar and percentage when volume changes.
/// Access via Eng.Volume. Triggered by Game.cs volume key handling.
/// </summary>
public class VolumeOverlay
{
    private float _timer;
    private float _alpha;
    private float _displayFill;

    private const float HoldTime = 1.5f;

    private IntPtr _font;
    private GLTexture? _texture;
    private string _cachedText = "";
    private int _texW, _texH;

    /// <summary>Show the volume HUD for the hold duration.</summary>
    public void Show() => _timer = HoldTime;

    internal void Update(float dt)
    {
        if (_timer > 0)
        {
            _timer -= dt;
            _alpha = System.Math.Min(1f, _alpha + dt * 8f);
        }
        else if (_alpha > 0)
        {
            _alpha = System.Math.Max(0f, _alpha - dt * 3f);
        }

        float target = Eng.Audio.MasterVolume;
        _displayFill += (target - _displayFill) * System.Math.Min(1f, dt * 12f);
    }

    internal void Draw()
    {
        if (_alpha <= 0.01f) return;

        float alpha = _alpha;
        float vol = _displayFill;
        int pct = (int)(Eng.Audio.MasterVolume * 100 + 0.5f);
        var gl = Eng.GL;

        // Layout
        int panelW = 180, panelH = 44;
        int px = (Eng.Width - panelW) / 2;
        int py = 16;
        int barX = px + 12, barY = py + 24;
        int barW = panelW - 24, barH = 10;

        // Panel
        gl.FillRect(px, py, panelW, panelH, 15 / 255f, 15 / 255f, 30 / 255f, alpha * (180f / 255f));
        gl.DrawRect(px, py, panelW, panelH, 124 / 255f, 58 / 255f, 237 / 255f, alpha * (150f / 255f));

        // Bar track
        gl.FillRect(barX, barY, barW, barH, 40 / 255f, 40 / 255f, 55 / 255f, alpha * (100f / 255f));

        // Bar fill
        int fillW = (int)(barW * vol);
        if (fillW > 0)
            gl.FillRect(barX, barY, fillW, barH, 110 / 255f, 50 / 255f, 220 / 255f, alpha * (230f / 255f));

        // Bar border
        gl.DrawRect(barX, barY, barW, barH, 80 / 255f, 60 / 255f, 120 / 255f, alpha * (120f / 255f));

        // Speaker icon
        float iconA = alpha * (200f / 255f);
        int ix = px + 12, iy = py + 6;
        gl.FillRect(ix, iy + 2, 4, 6, 200 / 255f, 200 / 255f, 220 / 255f, iconA);
        gl.Flush();
        gl.DrawLine(ix + 4, iy + 2, ix + 8, iy, 200 / 255f, 200 / 255f, 220 / 255f, iconA);
        gl.DrawLine(ix + 4, iy + 7, ix + 8, iy + 9, 200 / 255f, 200 / 255f, 220 / 255f, iconA);
        gl.DrawLine(ix + 8, iy, ix + 8, iy + 9, 200 / 255f, 200 / 255f, 220 / 255f, iconA);

        if (vol > 0.01f)
        {
            gl.DrawLine(ix + 10, iy + 2, ix + 10, iy + 7, 124 / 255f, 58 / 255f, 237 / 255f, iconA);
            if (vol > 0.4f)
                gl.DrawLine(ix + 12, iy + 1, ix + 12, iy + 8, 124 / 255f, 58 / 255f, 237 / 255f, iconA);
            if (vol > 0.7f)
                gl.DrawLine(ix + 14, iy, ix + 14, iy + 9, 124 / 255f, 58 / 255f, 237 / 255f, iconA);
        }
        gl.Flush();

        // Percentage text
        if (_font == IntPtr.Zero)
        {
            try { _font = Graphics.FontCache.GetDefault(12); }
            catch (Exception ex) { Console.WriteLine($"[Volume] Font load failed: {ex.Message}"); }
        }
        if (_font != IntPtr.Zero)
        {
            string text = $"{pct}%";
            if (text != _cachedText || _texture == null)
            {
                _texture?.Dispose();
                var color = new Math.Color(220, 220, 230, 255);
                var surface = SDL_ttf.TTF_RenderUTF8_Blended(_font, text, color);
                if (surface != IntPtr.Zero)
                {
                    _texture = GLTexture.FromSurface(gl.Api, surface);
                    _texW = _texture.Width;
                    _texH = _texture.Height;
                }
                _cachedText = text;
            }
            if (_texture != null)
            {
                int tx = px + panelW - _texW - 12;
                int ty = py + 4;
                gl.DrawTexture(_texture, 0, 0, _texW, _texH, tx, ty, _texW, _texH, 1, 1, 1, alpha * (230f / 255f));
            }
        }
    }

    internal void Shutdown()
    {
        _texture?.Dispose();
        // Font owned by FontCache — freed in FontCache.Shutdown()
    }
}
