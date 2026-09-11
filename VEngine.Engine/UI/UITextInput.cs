using System;
using SDL2;
using VEngine.Engine.Core;
using VEngine.Engine.Input;
using Color = VEngine.Engine.Math.Color;

namespace VEngine.Engine.UI;

/// <summary>
/// Single-line text input field. Click to focus, type to enter text, Enter to submit.
///
/// Usage:
///   var input = new UITextInput(100, 50, 200, 24, placeholder: "Enter name...");
///   input.OnSubmit = text => Console.WriteLine($"Submitted: {text}");
///   scene.Add(input);
/// </summary>
public class UITextInput : UIElement
{
    private string _text = "";
    private bool _focused;
    private float _cursorBlink;
    private bool _cursorVisible;

    // Hold-to-delete: after the initial press, suppress for InitialDelay then fire every
    // RepeatInterval seconds. Matches OS key-repeat feel without needing SDL text events.
    private float _backspaceHeld;
    private float _backspaceNextRepeat;
    private const float InitialDelay = 0.40f;
    private const float RepeatInterval = 0.035f;

    /// <summary>Current text content.</summary>
    public string Text
    {
        get => _text;
        set => _text = value ?? "";
    }

    /// <summary>Placeholder text shown when empty and not focused.</summary>
    public string Placeholder { get; set; } = "";

    /// <summary>Maximum character length. 0 = unlimited.</summary>
    public int MaxLength { get; set; }

    /// <summary>Whether this input is currently focused.</summary>
    public bool Focused => _focused;

    /// <summary>Fires when Enter is pressed.</summary>
    public Action<string>? OnSubmit;

    /// <summary>Fires when text changes.</summary>
    public Action<string>? OnChanged;

    public Color BackgroundColor = new(20, 20, 30, 220);
    public Color FocusedColor = new(30, 30, 50, 240);
    public Color TextColor = Color.White;
    public Color PlaceholderColor = new(120, 120, 120, 180);
    public Color BorderColor = new(80, 80, 100, 200);
    public Color CursorColor = Color.White;

    public UITextInput(float x, float y, float w, float h, string placeholder = "")
        : base(x, y, w, h)
    {
        Placeholder = placeholder;
    }

    public override void Update(float dt)
    {
        var mouse = Eng.Mouse;
        bool inside = mouse.X >= Position.X && mouse.X < Position.X + BaseWidth &&
                      mouse.Y >= Position.Y && mouse.Y < Position.Y + BaseHeight;

        if (mouse.IsPressed(MouseButton.Left))
        {
            bool wasFocused = _focused;
            _focused = inside;
            if (_focused && !wasFocused)
            {
                SDL.SDL_StartTextInput();
                Eng.Context.TextInputTarget = this;
                _cursorBlink = 0;
                _cursorVisible = true;
            }
            else if (!_focused && wasFocused)
            {
                SDL.SDL_StopTextInput();
                if (Eng.Context.TextInputTarget == this) Eng.Context.TextInputTarget = null;
            }
        }

        if (!_focused) return;

        // Cursor blink
        _cursorBlink += dt;
        if (_cursorBlink >= 0.5f) { _cursorVisible = !_cursorVisible; _cursorBlink = 0; }

        bool ctrl = Eng.Input.IsDown(SDL.SDL_Scancode.SDL_SCANCODE_LCTRL) ||
                    Eng.Input.IsDown(SDL.SDL_Scancode.SDL_SCANCODE_RCTRL);

        // Clipboard shortcuts
        if (ctrl)
        {
            if (Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_V))
            {
                if (SDL.SDL_HasClipboardText() == SDL.SDL_bool.SDL_TRUE)
                    PasteText(SDL.SDL_GetClipboardText());
                return;
            }
            if (Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_C))
            {
                SDL.SDL_SetClipboardText(_text);
                return;
            }
            if (Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_X))
            {
                SDL.SDL_SetClipboardText(_text);
                if (_text.Length > 0) { _text = ""; OnChanged?.Invoke(_text); }
                return;
            }
        }

        // Handle key input
        if (Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_RETURN))
        {
            OnSubmit?.Invoke(_text);
        }
        else if (Eng.Input.IsDown(SDL.SDL_Scancode.SDL_SCANCODE_BACKSPACE))
        {
            bool justPressed = Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_BACKSPACE);
            bool shouldDelete = false;
            if (justPressed)
            {
                shouldDelete = true;
                _backspaceHeld = 0;
                _backspaceNextRepeat = InitialDelay;
            }
            else
            {
                _backspaceHeld += dt;
                if (_backspaceHeld >= _backspaceNextRepeat)
                {
                    shouldDelete = true;
                    _backspaceNextRepeat += RepeatInterval;
                }
            }
            if (shouldDelete && _text.Length > 0)
            {
                // Ctrl+Backspace deletes the last word (bonus while we're here).
                if (ctrl && justPressed)
                {
                    int i = _text.Length - 1;
                    while (i >= 0 && char.IsWhiteSpace(_text[i])) i--;
                    while (i >= 0 && !char.IsWhiteSpace(_text[i])) i--;
                    _text = i < 0 ? "" : _text.Substring(0, i + 1);
                }
                else
                {
                    _text = _text[..^1];
                }
                OnChanged?.Invoke(_text);
            }
        }
        else
        {
            // Released: reset the hold state so the next press gets the full initial delay.
            _backspaceHeld = 0;
            _backspaceNextRepeat = InitialDelay;
        }

        if (Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_ESCAPE))
        {
            _focused = false;
            SDL.SDL_StopTextInput();
        }
    }

    private void PasteText(string? s)
    {
        if (string.IsNullOrEmpty(s)) return;
        // Single-line input: flatten newlines and strip other control chars.
        var sb = new System.Text.StringBuilder(_text, _text.Length + s.Length);
        foreach (var c in s)
        {
            if (c < 32 || c == 127) continue;
            if (MaxLength > 0 && sb.Length >= MaxLength) break;
            sb.Append(c);
        }
        if (sb.Length == _text.Length) return;
        _text = sb.ToString();
        OnChanged?.Invoke(_text);
    }

    /// <summary>Append a character (called from SDL_TEXTINPUT event handling).</summary>
    public void AppendChar(char c)
    {
        if (!_focused) return;
        if (c < 32) return;
        // Ignore text events while Ctrl is held (shortcut keys like Ctrl+V should not type 'v').
        if (Eng.Input.IsDown(SDL.SDL_Scancode.SDL_SCANCODE_LCTRL) ||
            Eng.Input.IsDown(SDL.SDL_Scancode.SDL_SCANCODE_RCTRL))
            return;
        if (MaxLength > 0 && _text.Length >= MaxLength) return;
        _text += c;
        OnChanged?.Invoke(_text);
    }

    public override void Draw()
    {
        var bg = _focused ? FocusedColor : BackgroundColor;
        Eng.GL.FillRect(Position.X, Position.Y, BaseWidth, BaseHeight,
            bg.R / 255f, bg.G / 255f, bg.B / 255f, bg.A / 255f);
        Eng.GL.DrawRect(Position.X, Position.Y, BaseWidth, BaseHeight,
            BorderColor.R / 255f, BorderColor.G / 255f, BorderColor.B / 255f, BorderColor.A / 255f);

        // Text or placeholder
        string display = _text.Length > 0 ? _text : (_focused ? "" : Placeholder);
        var color = _text.Length > 0 ? TextColor : PlaceholderColor;

        try
        {
            var atlas = Graphics.GlyphAtlas.Get("ui/default-font.ttf", FontSize);
            float tx = Position.X + 4;
            float ty = Position.Y + (BaseHeight - FontSize) / 2;
            int textWidth = atlas.DrawText(display, tx, ty,
                color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

            // Cursor
            if (_focused && _cursorVisible)
            {
                float cx = tx + textWidth;
                Eng.GL.FillRect(cx, ty, 1, FontSize,
                    CursorColor.R / 255f, CursorColor.G / 255f, CursorColor.B / 255f, CursorColor.A / 255f);
            }
        }
        catch { }
    }

    /// <summary>Focus this input programmatically.</summary>
    public void Focus()
    {
        _focused = true;
        SDL.SDL_StartTextInput();
        Eng.Context.TextInputTarget = this;
        _cursorBlink = 0;
        _cursorVisible = true;
    }

    /// <summary>Unfocus this input.</summary>
    public void Blur()
    {
        _focused = false;
        SDL.SDL_StopTextInput();
        if (Eng.Context.TextInputTarget == this) Eng.Context.TextInputTarget = null;
    }
}
