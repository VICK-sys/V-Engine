using VEngine.Engine.Core;

namespace VEngine.Engine.Graphics;

/// <summary>
/// Static primitive drawing helpers. World-space methods transform through camera.
/// Screen-space methods draw at pixel coordinates (for HUD/UI).
/// Call from Scene.Draw() or Entity.Draw() overrides.
/// </summary>
public static class Draw
{
    // ── Polyline (thick, with miter joins) ────────────────────

    /// <summary>
    /// Draw a connected polyline with configurable thickness and miter joins.
    /// Points are in world space. Use for trails, paths, debug visualization.
    /// </summary>
    public static void Polyline(ReadOnlySpan<Math.Vec2> points, float thickness,
        byte r, byte g, byte b, byte a = 255, bool closed = false)
    {
        if (points.Length < 2) return;
        var cam = Eng.Camera;
        float fr = r / 255f, fg = g / 255f, fb = b / 255f, fa = a / 255f;
        float halfW = thickness * cam.Zoom * 0.5f;

        // Convert to screen space
        Span<Math.Vec2> screen = stackalloc Math.Vec2[points.Length];
        for (int i = 0; i < points.Length; i++)
            screen[i] = cam.WorldToScreen(points[i].X, points[i].Y);

        DrawPolylineScreen(screen, halfW, fr, fg, fb, fa, closed);
    }

    /// <summary>Draw a connected polyline in screen space with miter joins.</summary>
    public static void ScreenPolyline(ReadOnlySpan<Math.Vec2> points, float thickness,
        byte r, byte g, byte b, byte a = 255, bool closed = false)
    {
        if (points.Length < 2) return;
        DrawPolylineScreen(points, thickness * 0.5f, r / 255f, g / 255f, b / 255f, a / 255f, closed);
    }

    private static void DrawPolylineScreen(ReadOnlySpan<Math.Vec2> pts, float halfW,
        float r, float g, float b, float a, bool closed)
    {
        int count = pts.Length;
        int segments = closed ? count : count - 1;

        for (int i = 0; i < segments; i++)
        {
            int i0 = i;
            int i1 = (i + 1) % count;

            var p0 = pts[i0];
            var p1 = pts[i1];

            float dx = p1.X - p0.X;
            float dy = p1.Y - p0.Y;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f) continue;

            // Normal perpendicular to segment
            float nx = -dy / len * halfW;
            float ny = dx / len * halfW;

            // Miter at start: average normals of this segment and previous
            float nx0 = nx, ny0 = ny;
            if (i > 0 || closed)
            {
                int iPrev = (i - 1 + count) % count;
                var pp = pts[iPrev];
                float pdx = p0.X - pp.X;
                float pdy = p0.Y - pp.Y;
                float plen = MathF.Sqrt(pdx * pdx + pdy * pdy);
                if (plen > 0.001f)
                {
                    float pnx = -pdy / plen;
                    float pny = pdx / plen;
                    float mnx = (pnx + (-dy / len)) * 0.5f;
                    float mny = (pny + (dx / len)) * 0.5f;
                    float mlen = MathF.Sqrt(mnx * mnx + mny * mny);
                    if (mlen > 0.001f)
                    {
                        // Limit miter to prevent spikes at sharp angles
                        float miterScale = MathF.Min(halfW / mlen, halfW * 2f);
                        nx0 = mnx * miterScale;
                        ny0 = mny * miterScale;
                    }
                }
            }

            // Miter at end
            float nx1 = nx, ny1 = ny;
            if (i < segments - 1 || closed)
            {
                int iNext = (i + 2) % count;
                var pn = pts[iNext];
                float ndx = pn.X - p1.X;
                float ndy = pn.Y - p1.Y;
                float nlen = MathF.Sqrt(ndx * ndx + ndy * ndy);
                if (nlen > 0.001f)
                {
                    float nnx = -ndy / nlen;
                    float nny = ndx / nlen;
                    float mnx = ((-dy / len) + nnx) * 0.5f;
                    float mny = ((dx / len) + nny) * 0.5f;
                    float mlen = MathF.Sqrt(mnx * mnx + mny * mny);
                    if (mlen > 0.001f)
                    {
                        float miterScale = MathF.Min(halfW / mlen, halfW * 2f);
                        nx1 = mnx * miterScale;
                        ny1 = mny * miterScale;
                    }
                }
            }

            Eng.GL.Primitives.DrawFilledQuad(
                p0.X + nx0, p0.Y + ny0,
                p1.X + nx1, p1.Y + ny1,
                p1.X - nx1, p1.Y - ny1,
                p0.X - nx0, p0.Y - ny0,
                r, g, b, a);
        }
    }

    // ── World-space (camera-transformed) ────────────────────────

    /// <summary>Draw a 1px line between two world points.</summary>
    public static void Line(float x1, float y1, float x2, float y2,
        byte r, byte g, byte b, byte a = 255)
    {
        var cam = Eng.Camera;
        var s1 = cam.WorldToScreen(x1, y1);
        var s2 = cam.WorldToScreen(x2, y2);
        Eng.GL.DrawLine(s1.X, s1.Y, s2.X, s2.Y, r / 255f, g / 255f, b / 255f, a / 255f);
    }

    /// <summary>Draw a thick line between two world points (rendered as a rotated quad).</summary>
    public static void ThickLine(float x1, float y1, float x2, float y2, float thickness,
        byte r, byte g, byte b, byte a = 255)
    {
        var cam = Eng.Camera;
        var s1 = cam.WorldToScreen(x1, y1);
        var s2 = cam.WorldToScreen(x2, y2);
        DrawThickLineScreen(s1.X, s1.Y, s2.X, s2.Y, thickness * cam.Zoom,
            r / 255f, g / 255f, b / 255f, a / 255f);
    }

    /// <summary>Draw a thick line in screen space.</summary>
    public static void ScreenThickLine(float x1, float y1, float x2, float y2, float thickness,
        byte r, byte g, byte b, byte a = 255)
    {
        DrawThickLineScreen(x1, y1, x2, y2, thickness, r / 255f, g / 255f, b / 255f, a / 255f);
    }

    private static void DrawThickLineScreen(float x1, float y1, float x2, float y2, float thickness,
        float r, float g, float b, float a)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f) return;

        // Normal perpendicular to line direction, scaled by half-thickness
        float nx = -dy / len * thickness * 0.5f;
        float ny = dx / len * thickness * 0.5f;

        // Render as two triangles forming a quad
        Eng.GL.Primitives.DrawFilledQuad(
            x1 + nx, y1 + ny,
            x2 + nx, y2 + ny,
            x2 - nx, y2 - ny,
            x1 - nx, y1 - ny,
            r, g, b, a);
    }

    /// <summary>Draw a rectangle outline in world space.</summary>
    public static void Rect(float x, float y, float w, float h,
        byte r, byte g, byte b, byte a = 255)
    {
        var cam = Eng.Camera;
        var s = cam.WorldToScreen(x, y);
        Eng.GL.DrawRect(s.X, s.Y, w * cam.Zoom, h * cam.Zoom,
            r / 255f, g / 255f, b / 255f, a / 255f);
    }

    /// <summary>Draw a filled rectangle in world space.</summary>
    public static void FillRect(float x, float y, float w, float h,
        byte r, byte g, byte b, byte a = 255)
    {
        var cam = Eng.Camera;
        var s = cam.WorldToScreen(x, y);
        Eng.GL.FillRect(s.X, s.Y, w * cam.Zoom, h * cam.Zoom,
            r / 255f, g / 255f, b / 255f, a / 255f);
    }

    /// <summary>Draw a circle outline in world space.</summary>
    public static void Circle(float cx, float cy, float radius,
        byte r, byte g, byte b, byte a = 255)
    {
        var cam = Eng.Camera;
        var center = cam.WorldToScreen(cx, cy);
        Eng.GL.DrawCircle(center.X, center.Y, radius * cam.Zoom,
            r / 255f, g / 255f, b / 255f, a / 255f);
    }

    /// <summary>Draw a filled circle in world space.</summary>
    public static void FillCircle(float cx, float cy, float radius,
        byte r, byte g, byte b, byte a = 255)
    {
        var cam = Eng.Camera;
        var center = cam.WorldToScreen(cx, cy);
        Eng.GL.FillCircle(center.X, center.Y, radius * cam.Zoom,
            r / 255f, g / 255f, b / 255f, a / 255f);
    }

    // ── Screen-space (no camera transform) ─────────────────────

    /// <summary>Draw a 1px line in screen space.</summary>
    public static void ScreenLine(float x1, float y1, float x2, float y2,
        byte r, byte g, byte b, byte a = 255)
    {
        Eng.GL.DrawLine(x1, y1, x2, y2, r / 255f, g / 255f, b / 255f, a / 255f);
    }

    /// <summary>Draw a rectangle outline in screen space.</summary>
    public static void ScreenRect(float x, float y, float w, float h,
        byte r, byte g, byte b, byte a = 255)
    {
        Eng.GL.DrawRect(x, y, w, h, r / 255f, g / 255f, b / 255f, a / 255f);
    }

    /// <summary>Draw a filled rectangle in screen space.</summary>
    public static void ScreenFillRect(float x, float y, float w, float h,
        byte r, byte g, byte b, byte a = 255)
    {
        Eng.GL.FillRect(x, y, w, h, r / 255f, g / 255f, b / 255f, a / 255f);
    }
}
