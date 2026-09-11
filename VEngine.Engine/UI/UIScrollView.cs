using System;
using System.Collections.Generic;
using VEngine.Engine.Core;
using VEngine.Engine.Input;
using Color = VEngine.Engine.Math.Color;

namespace VEngine.Engine.UI;

/// <summary>
/// Scrollable container. Children are clipped to the view bounds and scrolled
/// vertically via mouse wheel or drag. Use for inventories, logs, settings lists.
///
/// Usage:
///   var scroll = new UIScrollView(10, 50, 200, 300);
///   for (int i = 0; i < 50; i++)
///       scroll.AddChild(new UILabel($"Item {i}", 0, 0));
///   scene.Add(scroll);
/// </summary>
public class UIScrollView : UIElement
{
    private readonly List<UIElement> _children = new();
    private float _scrollY;
    private float _contentHeight;
    private bool _dragging;
    private float _dragStartY;
    private float _dragStartScroll;

    /// <summary>Space between children.</summary>
    public float Spacing { get; set; } = 4;

    /// <summary>Padding inside the scroll view.</summary>
    public float Padding { get; set; } = 4;

    /// <summary>Scroll speed multiplier for mouse wheel.</summary>
    public float ScrollSpeed { get; set; } = 20;

    public Color BackgroundColor = new(30, 30, 30, 180);
    public Color ScrollbarColor = new(100, 100, 100, 150);

    public UIScrollView(float x, float y, float w, float h) : base(x, y, w, h) { }

    public UIScrollView AddChild(UIElement child)
    {
        _children.Add(child);
        return this;
    }

    public void RemoveChild(UIElement child) => _children.Remove(child);
    public IReadOnlyList<UIElement> Children => _children;

    public override void Update(float dt)
    {
        // Layout children vertically
        float offset = Padding;
        foreach (var child in _children)
        {
            if (!child.Active) continue;
            child.Position.X = Position.X + Padding;
            child.Position.Y = Position.Y + offset - _scrollY;
            offset += child.BaseHeight + Spacing;
        }
        _contentHeight = offset - Spacing + Padding;

        // Mouse hover check
        var mouse = Eng.Mouse;
        bool inside = mouse.X >= Position.X && mouse.X < Position.X + BaseWidth &&
                      mouse.Y >= Position.Y && mouse.Y < Position.Y + BaseHeight;

        // Scroll via wheel
        if (inside && mouse.ScrollY != 0)
            _scrollY -= mouse.ScrollY * ScrollSpeed;

        // Scroll via drag
        if (inside && mouse.IsPressed(MouseButton.Left))
        {
            _dragging = true;
            _dragStartY = mouse.Y;
            _dragStartScroll = _scrollY;
        }
        if (_dragging)
        {
            if (mouse.IsDown(MouseButton.Left))
                _scrollY = _dragStartScroll - (mouse.Y - _dragStartY);
            else
                _dragging = false;
        }

        // Clamp scroll
        float maxScroll = System.Math.Max(0, _contentHeight - BaseHeight);
        _scrollY = System.Math.Clamp(_scrollY, 0, maxScroll);

        // Update visible children
        foreach (var child in _children)
        {
            if (!child.Active) continue;
            float childBottom = child.Position.Y + child.BaseHeight;
            float childTop = child.Position.Y;
            // Only update if visible within the scroll area
            if (childBottom >= Position.Y && childTop < Position.Y + BaseHeight)
                child.Update(dt);
        }
    }

    public override void Draw()
    {
        // Background
        Eng.GL.FillRect(Position.X, Position.Y, BaseWidth, BaseHeight,
            BackgroundColor.R / 255f, BackgroundColor.G / 255f, BackgroundColor.B / 255f, BackgroundColor.A / 255f);

        // Clip children to scroll view bounds via glScissor
        Eng.GL.Flush();
        var gl = Eng.GL.Api;
        // Save current scissor state
        int sx, sy, sw, sh;
        unsafe
        {
            int* box = stackalloc int[4];
            gl.GetInteger((Silk.NET.OpenGL.GLEnum)0x0C10, box); // GL_SCISSOR_BOX
            sx = box[0]; sy = box[1]; sw = box[2]; sh = box[3];
        }
        // Compute scissor rect in GL coordinates (Y-up from bottom-left)
        int vx = Eng.GL.ViewportX, vy = Eng.GL.ViewportY;
        int vw = Eng.GL.ViewportW, vh = Eng.GL.ViewportH;
        float scaleX = (float)vw / Eng.GL.LogicalWidth;
        float scaleY = (float)vh / Eng.GL.LogicalHeight;
        int clipX = vx + (int)(Position.X * scaleX);
        int clipW = (int)(BaseWidth * scaleX);
        int clipH = (int)(BaseHeight * scaleY);
        int clipY = vy + vh - (int)((Position.Y + BaseHeight) * scaleY);
        gl.Scissor(clipX, clipY, (uint)clipW, (uint)clipH);

        try
        {
            foreach (var child in _children)
            {
                if (!child.Visible || !child.Active) continue;
                child.Draw();
            }
        }
        finally
        {
            // Always restore scissor, even if a child throws
            Eng.GL.Flush();
            gl.Scissor(sx, sy, (uint)sw, (uint)sh);
        }

        // Scrollbar
        if (_contentHeight > BaseHeight)
        {
            float barHeight = System.Math.Max(20, BaseHeight * (BaseHeight / _contentHeight));
            float maxScroll = _contentHeight - BaseHeight;
            float barY = Position.Y + (_scrollY / maxScroll) * (BaseHeight - barHeight);
            Eng.GL.FillRect(Position.X + BaseWidth - 4, barY, 4, barHeight,
                ScrollbarColor.R / 255f, ScrollbarColor.G / 255f, ScrollbarColor.B / 255f, ScrollbarColor.A / 255f);
        }
    }

    protected override void OnDestroy()
    {
        foreach (var child in _children) child.Destroy();
        _children.Clear();
    }
}
