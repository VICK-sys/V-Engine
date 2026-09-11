using System;
using SDL2;
using VEngine.Engine.Core;
using VEngine.Engine.Input;
using Color = VEngine.Engine.Math.Color;

namespace VEngine.Engine.UI;

/// <summary>
/// Button that captures the next key/button press and rebinds an InputMap action.
/// Shows the current binding, enters "listening" mode on click, captures the next input.
///
/// Usage:
///   var rebind = new UIRebindButton("Jump", "jump", 100, 50, 200, 30);
///   scene.Add(rebind);
/// </summary>
public class UIRebindButton : UIElement
{
    private readonly string _actionName;
    private bool _listening;
    private bool _hovered;

    /// <summary>Display label (e.g., "Jump").</summary>
    public string Label { get; set; }

    /// <summary>Fires after a successful rebind with the action name.</summary>
    public Action<string>? OnRebound;

    public Color NormalColor = new(60, 60, 60, 220);
    public Color ListeningColor = new(120, 40, 40, 240);
    public Color HoverColor = new(80, 80, 80, 240);
    public Color TextColor = Color.White;
    public Color BorderColor = new(120, 120, 120, 200);

    public UIRebindButton(string label, string actionName, float x, float y, float w, float h)
        : base(x, y, w, h)
    {
        Label = label;
        _actionName = actionName;
    }

    public override void Update(float dt)
    {
        var mouse = Eng.Mouse;
        _hovered = mouse.X >= Position.X && mouse.X < Position.X + BaseWidth &&
                   mouse.Y >= Position.Y && mouse.Y < Position.Y + BaseHeight;

        if (_listening)
        {
            // Check for any key press
            for (int i = 0; i < (int)SDL.SDL_Scancode.SDL_NUM_SCANCODES; i++)
            {
                var sc = (SDL.SDL_Scancode)i;
                if (sc == SDL.SDL_Scancode.SDL_SCANCODE_ESCAPE)
                {
                    if (Eng.Input.IsPressed(sc))
                    {
                        _listening = false; // cancel
                        return;
                    }
                    continue;
                }

                if (Eng.Input.IsPressed(sc))
                {
                    Eng.Actions.Unbind(_actionName);
                    Eng.Actions.Bind(_actionName, sc);
                    _listening = false;
                    OnRebound?.Invoke(_actionName);
                    return;
                }
            }

            // Check for gamepad button press
            if (Eng.Gamepad.Connected)
            {
                for (int i = 0; i < (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_MAX; i++)
                {
                    var btn = (SDL.SDL_GameControllerButton)i;
                    if (Eng.Gamepad.IsPressed(btn))
                    {
                        Eng.Actions.Unbind(_actionName);
                        Eng.Actions.Bind(_actionName, btn);
                        _listening = false;
                        OnRebound?.Invoke(_actionName);
                        return;
                    }
                }
            }
        }
        else
        {
            if (_hovered && mouse.IsPressed(MouseButton.Left))
                _listening = true;
        }
    }

    public override void Draw()
    {
        var bg = _listening ? ListeningColor : _hovered ? HoverColor : NormalColor;
        Eng.GL.FillRect(Position.X, Position.Y, BaseWidth, BaseHeight,
            bg.R / 255f, bg.G / 255f, bg.B / 255f, bg.A / 255f);
        Eng.GL.DrawRect(Position.X, Position.Y, BaseWidth, BaseHeight,
            BorderColor.R / 255f, BorderColor.G / 255f, BorderColor.B / 255f, BorderColor.A / 255f);

        // Text
        string text;
        if (_listening)
        {
            text = $"{Label}: [press a key]";
        }
        else
        {
            var keys = Eng.Actions.GetKeys(_actionName);
            var buttons = Eng.Actions.GetButtons(_actionName);
            string binding = keys.Count > 0 ? keys[0].ToString().Replace("SDL_SCANCODE_", "")
                           : buttons.Count > 0 ? buttons[0].ToString().Replace("SDL_CONTROLLER_BUTTON_", "")
                           : "Unbound";
            text = $"{Label}: {binding}";
        }

        try
        {
            var atlas = Graphics.GlyphAtlas.Get("ui/default-font.ttf", FontSize);
            int tw = atlas.MeasureWidth(text);
            float tx = Position.X + (BaseWidth - tw) / 2;
            float ty = Position.Y + (BaseHeight - FontSize) / 2;
            atlas.DrawText(text, tx, ty,
                TextColor.R / 255f, TextColor.G / 255f, TextColor.B / 255f, TextColor.A / 255f);
        }
        catch { }
    }
}
