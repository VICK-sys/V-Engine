using System;
using SDL2;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics.GL;
using VEngine.Engine.Input;
using Color = VEngine.Engine.Math.Color;

namespace VEngine.Engine.UI;

/// <summary>
/// Checkbox with label. Toggles on click.
/// </summary>
public class UICheckbox : UIElement
{
    private bool _checked;
    private bool _hovered;

    /// <summary>Whether the checkbox is checked.</summary>
    public bool Checked
    {
        get => _checked;
        set => _checked = value;
    }

    /// <summary>Label text displayed next to the checkbox.</summary>
    public string Text { get; set; }

    /// <summary>Fires when the value changes.</summary>
    public Action<bool>? OnChanged;

    public Color CheckColor = Color.White;
    public Color BoxColor = new(60, 60, 60, 220);
    public Color BorderColor = new(120, 120, 120, 200);

    private int BoxSize => FontSize + 4;

    public UICheckbox(string text, float x, float y, bool initial = false) : base(x, y)
    {
        Text = text;
        _checked = initial;
        BaseHeight = FontSize + 4;
    }

    public override void Update(float dt)
    {
        var mouse = Eng.Mouse;
        int textWidth = 0;
        try { textWidth = Graphics.GlyphAtlas.Get("ui/default-font.ttf", FontSize).MeasureWidth(Text); } catch { }
        _hovered = mouse.X >= Position.X && mouse.X < Position.X + BoxSize + 6 + textWidth &&
                   mouse.Y >= Position.Y && mouse.Y < Position.Y + BoxSize;

        if (_hovered && mouse.IsPressed(MouseButton.Left))
        {
            _checked = !_checked;
            OnChanged?.Invoke(_checked);
        }
    }

    public override void Draw()
    {
        int bs = BoxSize;
        var bg = _hovered ? new Color(80, 80, 80, 240) : BoxColor;

        Eng.GL.FillRect(Position.X, Position.Y, bs, bs, bg.R / 255f, bg.G / 255f, bg.B / 255f, bg.A / 255f);
        Eng.GL.DrawRect(Position.X, Position.Y, bs, bs, BorderColor.R / 255f, BorderColor.G / 255f, BorderColor.B / 255f, BorderColor.A / 255f);

        if (_checked)
        {
            float p = 3;
            Eng.GL.FillRect(Position.X + p, Position.Y + p, bs - p * 2, bs - p * 2,
                CheckColor.R / 255f, CheckColor.G / 255f, CheckColor.B / 255f, CheckColor.A / 255f);
        }

        // Label — render via glyph atlas if available
        try
        {
            var atlas = Graphics.GlyphAtlas.Get("ui/default-font.ttf", FontSize);
            atlas.DrawText(Text, Position.X + bs + 6, Position.Y + 2, 1, 1, 1, 1);
        }
        catch { }
    }
}

/// <summary>
/// Horizontal slider with min/max range. Drag to change value.
/// </summary>
public class UISlider : UIElement
{
    private float _value;
    private bool _dragging;

    /// <summary>Current value (clamped to Min..Max).</summary>
    public float Value
    {
        get => _value;
        set => _value = System.Math.Clamp(value, Min, Max);
    }

    public float Min { get; set; }
    public float Max { get; set; } = 1f;

    /// <summary>Fires when the value changes via user interaction.</summary>
    public Action<float>? OnChanged;

    public Color TrackColor = new(40, 40, 55, 200);
    public Color FillColor = new(110, 50, 220, 230);
    public Color HandleColor = Color.White;
    public Color BorderColor = new(80, 60, 120, 150);

    public UISlider(float x, float y, float width, float min = 0, float max = 1, float initial = 0)
        : base(x, y, width, 12)
    {
        Min = min;
        Max = max;
        _value = System.Math.Clamp(initial, min, max);
    }

    public override void Update(float dt)
    {
        var mouse = Eng.Mouse;
        bool over = mouse.X >= Position.X && mouse.X < Position.X + BaseWidth &&
                    mouse.Y >= Position.Y - 4 && mouse.Y < Position.Y + BaseHeight + 4;

        if (over && mouse.IsPressed(MouseButton.Left))
            _dragging = true;

        if (_dragging)
        {
            if (mouse.IsDown(MouseButton.Left))
            {
                float t = System.Math.Clamp((mouse.X - Position.X) / BaseWidth, 0, 1);
                float newVal = Min + t * (Max - Min);
                if (MathF.Abs(newVal - _value) > 0.0001f)
                {
                    _value = newVal;
                    OnChanged?.Invoke(_value);
                }
            }
            else
            {
                _dragging = false;
            }
        }
    }

    public override void Draw()
    {
        float w = BaseWidth, h = BaseHeight;

        // Track
        Eng.GL.FillRect(Position.X, Position.Y, w, h,
            TrackColor.R / 255f, TrackColor.G / 255f, TrackColor.B / 255f, TrackColor.A / 255f);

        // Fill
        float fill = (Value - Min) / (Max - Min);
        if (fill > 0)
            Eng.GL.FillRect(Position.X, Position.Y, w * fill, h,
                FillColor.R / 255f, FillColor.G / 255f, FillColor.B / 255f, FillColor.A / 255f);

        // Border
        Eng.GL.DrawRect(Position.X, Position.Y, w, h,
            BorderColor.R / 255f, BorderColor.G / 255f, BorderColor.B / 255f, BorderColor.A / 255f);

        // Handle
        float hx = Position.X + w * fill - 3;
        Eng.GL.FillRect(hx, Position.Y - 2, 6, h + 4,
            HandleColor.R / 255f, HandleColor.G / 255f, HandleColor.B / 255f, HandleColor.A / 255f);
    }
}
