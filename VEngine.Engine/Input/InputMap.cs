using System;
using System.Collections.Generic;
using SDL2;
using VEngine.Engine.Core;

namespace VEngine.Engine.Input;

/// <summary>
/// Maps named actions to physical inputs (keyboard keys, gamepad buttons, analog axes).
/// Decouples game logic from hardware — allows remapping without changing game code.
/// Access via Eng.Actions.
/// </summary>
public class InputMap
{
    private readonly Dictionary<string, ActionBinding> _actions = new();
    private readonly Dictionary<string, AxisBinding> _axes = new();

    // ── Digital Actions ─────────────────────────────────────────

    /// <summary>Bind keyboard keys to an action. Additive — call multiple times to add more bindings.</summary>
    public void Bind(string action, params SDL.SDL_Scancode[] keys)
    {
        var binding = GetOrCreateAction(action);
        binding.Keys.AddRange(keys);
    }

    /// <summary>Bind gamepad buttons to an action. Additive.</summary>
    public void Bind(string action, params SDL.SDL_GameControllerButton[] buttons)
    {
        var binding = GetOrCreateAction(action);
        binding.Buttons.AddRange(buttons);
    }

    /// <summary>Bind a keyboard key and gamepad button to the same action.</summary>
    public void Bind(string action, SDL.SDL_Scancode key, SDL.SDL_GameControllerButton button)
    {
        var binding = GetOrCreateAction(action);
        binding.Keys.Add(key);
        binding.Buttons.Add(button);
    }

    /// <summary>Remove all bindings for an action.</summary>
    public void Unbind(string action)
    {
        _actions.Remove(action);
    }

    /// <summary>True if any bound input is held this frame.</summary>
    public bool Down(string action)
    {
        if (!_actions.TryGetValue(action, out var b)) return false;
        foreach (var key in b.Keys)
            if (Eng.Input.IsDown(key)) return true;
        foreach (var btn in b.Buttons)
            if (Eng.Gamepad.IsDown(btn)) return true;
        return false;
    }

    /// <summary>True on the first frame any bound input is pressed.</summary>
    public bool Pressed(string action)
    {
        if (!_actions.TryGetValue(action, out var b)) return false;
        foreach (var key in b.Keys)
            if (Eng.Input.IsPressed(key)) return true;
        foreach (var btn in b.Buttons)
            if (Eng.Gamepad.IsPressed(btn)) return true;
        return false;
    }

    /// <summary>
    /// True on the frame ALL bound inputs for an action are released.
    /// Returns true if at least one binding fired a release AND no bindings are still held.
    /// This means: if two keys are bound and only one is released, this returns false
    /// until the second key is also released.
    /// </summary>
    public bool Released(string action)
    {
        if (!_actions.TryGetValue(action, out var b)) return false;
        bool anyReleased = false;
        foreach (var key in b.Keys)
        {
            if (Eng.Input.IsDown(key)) return false;
            if (Eng.Input.IsReleased(key)) anyReleased = true;
        }
        foreach (var btn in b.Buttons)
        {
            if (Eng.Gamepad.IsDown(btn)) return false;
            if (Eng.Gamepad.IsReleased(btn)) anyReleased = true;
        }
        return anyReleased;
    }

    /// <summary>
    /// True if ANY bound input was released this frame, regardless of whether others are still held.
    /// Use for actions where partial release matters (e.g., combo timing).
    /// Compare with Released() which requires ALL bindings to be released.
    /// </summary>
    public bool AnyReleased(string action)
    {
        if (!_actions.TryGetValue(action, out var b)) return false;
        foreach (var key in b.Keys)
            if (Eng.Input.IsReleased(key)) return true;
        foreach (var btn in b.Buttons)
            if (Eng.Gamepad.IsReleased(btn)) return true;
        return false;
    }

    // ── Analog Axes ─────────────────────────────────────────────

    /// <summary>Bind keyboard keys to an axis. Positive key = +1, negative key = -1. Additive.</summary>
    public void BindAxis(string axis, SDL.SDL_Scancode positive, SDL.SDL_Scancode negative)
    {
        var binding = GetOrCreateAxis(axis);
        binding.KeyPairs.Add((positive, negative));
    }

    /// <summary>Bind a gamepad axis. Additive.</summary>
    public void BindAxis(string axis, GamepadAxis gamepadAxis)
    {
        var binding = GetOrCreateAxis(axis);
        binding.GamepadAxes.Add(gamepadAxis);
    }

    /// <summary>Get the current axis value (-1 to 1). Keyboard gives -1/0/1, gamepad gives analog. Largest magnitude wins.</summary>
    public float Axis(string axis)
    {
        if (!_axes.TryGetValue(axis, out var b)) return 0;

        float result = 0;

        // Keyboard: digital -1/0/1
        foreach (var (pos, neg) in b.KeyPairs)
        {
            float v = 0;
            if (Eng.Input.IsDown(pos)) v += 1;
            if (Eng.Input.IsDown(neg)) v -= 1;
            if (MathF.Abs(v) > MathF.Abs(result)) result = v;
        }

        // Gamepad: analog
        foreach (var ga in b.GamepadAxes)
        {
            float v = ReadGamepadAxis(ga);
            if (MathF.Abs(v) > MathF.Abs(result)) result = v;
        }

        return result;
    }

    /// <summary>Remove all bindings for an axis.</summary>
    public void UnbindAxis(string axis)
    {
        _axes.Remove(axis);
    }

    /// <summary>Remove all action and axis bindings.</summary>
    public void ClearAll()
    {
        _actions.Clear();
        _axes.Clear();
    }

    // ── Introspection (for rebinding UIs) ───────────────────────

    /// <summary>Get all keyboard keys bound to an action.</summary>
    public IReadOnlyList<SDL.SDL_Scancode> GetKeys(string action)
    {
        if (_actions.TryGetValue(action, out var b)) return b.Keys;
        return Array.Empty<SDL.SDL_Scancode>();
    }

    /// <summary>Get all gamepad buttons bound to an action.</summary>
    public IReadOnlyList<SDL.SDL_GameControllerButton> GetButtons(string action)
    {
        if (_actions.TryGetValue(action, out var b)) return b.Buttons;
        return Array.Empty<SDL.SDL_GameControllerButton>();
    }

    /// <summary>Check if an action is registered.</summary>
    public bool HasAction(string action) => _actions.ContainsKey(action);

    /// <summary>Check if an axis is registered.</summary>
    public bool HasAxis(string axis) => _axes.ContainsKey(axis);

    /// <summary>Get all registered action names.</summary>
    public IEnumerable<string> ActionNames => _actions.Keys;

    /// <summary>Get all registered axis names.</summary>
    public IEnumerable<string> AxisNames => _axes.Keys;

    /// <summary>Get all gamepad axes bound to an axis name.</summary>
    public IReadOnlyList<GamepadAxis> GetGamepadAxes(string axis)
    {
        if (_axes.TryGetValue(axis, out var b)) return b.GamepadAxes;
        return Array.Empty<GamepadAxis>();
    }

    // ── Internal ────────────────────────────────────────────────

    private ActionBinding GetOrCreateAction(string action)
    {
        if (!_actions.TryGetValue(action, out var binding))
        {
            binding = new ActionBinding();
            _actions[action] = binding;
        }
        return binding;
    }

    private AxisBinding GetOrCreateAxis(string axis)
    {
        if (!_axes.TryGetValue(axis, out var binding))
        {
            binding = new AxisBinding();
            _axes[axis] = binding;
        }
        return binding;
    }

    private static float ReadGamepadAxis(GamepadAxis axis) => axis switch
    {
        GamepadAxis.LeftStickX => Eng.Gamepad.LeftStick.X,
        GamepadAxis.LeftStickY => Eng.Gamepad.LeftStick.Y,
        GamepadAxis.RightStickX => Eng.Gamepad.RightStick.X,
        GamepadAxis.RightStickY => Eng.Gamepad.RightStick.Y,
        GamepadAxis.LeftTrigger => Eng.Gamepad.LeftTrigger,
        GamepadAxis.RightTrigger => Eng.Gamepad.RightTrigger,
        _ => 0
    };

    private class ActionBinding
    {
        public readonly List<SDL.SDL_Scancode> Keys = new();
        public readonly List<SDL.SDL_GameControllerButton> Buttons = new();
    }

    private class AxisBinding
    {
        public readonly List<(SDL.SDL_Scancode Positive, SDL.SDL_Scancode Negative)> KeyPairs = new();
        public readonly List<GamepadAxis> GamepadAxes = new();
    }
}

/// <summary>Gamepad analog axes for use with InputMap.BindAxis().</summary>
public enum GamepadAxis
{
    LeftStickX,
    LeftStickY,
    RightStickX,
    RightStickY,
    LeftTrigger,
    RightTrigger
}
