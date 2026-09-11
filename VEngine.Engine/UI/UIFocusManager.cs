using System.Collections.Generic;
using SDL2;
using VEngine.Engine.Core;

namespace VEngine.Engine.UI;

/// <summary>
/// Manages keyboard/gamepad focus navigation between UI elements.
/// Tab moves forward, Shift+Tab moves backward, Enter/Space activates.
/// Add focusable elements in order, then call Update() each frame.
///
/// Usage:
///   var focus = new UIFocusManager();
///   focus.Add(playButton);
///   focus.Add(optionsButton);
///   focus.Add(quitButton);
///   // In scene Update:
///   focus.Update(dt);
/// </summary>
public class UIFocusManager
{
    private readonly List<UIElement> _elements = new();
    private int _focusIndex = -1;

    /// <summary>Currently focused element, or null.</summary>
    public UIElement? Focused => _focusIndex >= 0 && _focusIndex < _elements.Count ? _elements[_focusIndex] : null;

    /// <summary>Add a focusable element.</summary>
    public UIFocusManager Add(UIElement element)
    {
        _elements.Add(element);
        return this;
    }

    /// <summary>Remove an element from focus navigation.</summary>
    public void Remove(UIElement element)
    {
        int idx = _elements.IndexOf(element);
        if (idx >= 0)
        {
            _elements.RemoveAt(idx);
            if (_focusIndex >= _elements.Count) _focusIndex = _elements.Count - 1;
        }
    }

    /// <summary>Set focus to a specific element.</summary>
    public void SetFocus(UIElement element)
    {
        int idx = _elements.IndexOf(element);
        if (idx >= 0) _focusIndex = idx;
    }

    /// <summary>Clear focus.</summary>
    public void ClearFocus() => _focusIndex = -1;

    /// <summary>Process navigation input. Call each frame.</summary>
    public void Update(float dt)
    {
        if (_elements.Count == 0) return;

        bool tab = Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_TAB);
        bool shift = Eng.Input.IsDown(SDL.SDL_Scancode.SDL_SCANCODE_LSHIFT) ||
                     Eng.Input.IsDown(SDL.SDL_Scancode.SDL_SCANCODE_RSHIFT);
        bool down = Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_DOWN) ||
                    (Eng.Gamepad.Connected && Eng.Gamepad.IsPressed(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN));
        bool up = Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_UP) ||
                  (Eng.Gamepad.Connected && Eng.Gamepad.IsPressed(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP));

        // Navigate
        if ((tab && !shift) || down)
        {
            _focusIndex = (_focusIndex + 1) % _elements.Count;
            SkipInactive(1);
        }
        else if ((tab && shift) || up)
        {
            _focusIndex = (_focusIndex - 1 + _elements.Count) % _elements.Count;
            SkipInactive(-1);
        }

        // Activate
        bool activate = Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_RETURN) ||
                        Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_SPACE) ||
                        (Eng.Gamepad.Connected && Eng.Gamepad.IsPressed(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A));

        if (activate && Focused is UIButton btn)
            btn.OnClick?.Invoke();
    }

    /// <summary>Draw a focus indicator around the focused element.</summary>
    public void DrawFocusIndicator(byte r = 255, byte g = 255, byte b = 100, byte a = 200)
    {
        if (Focused == null) return;
        var e = Focused;
        Eng.GL.DrawRect(e.Position.X - 2, e.Position.Y - 2, e.BaseWidth + 4, e.BaseHeight + 4,
            r / 255f, g / 255f, b / 255f, a / 255f);
    }

    private void SkipInactive(int direction)
    {
        int start = _focusIndex;
        while (!_elements[_focusIndex].Active)
        {
            _focusIndex = (_focusIndex + direction + _elements.Count) % _elements.Count;
            if (_focusIndex == start) break;
        }
    }
}
