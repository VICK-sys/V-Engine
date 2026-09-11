using System.Collections.Generic;
using SDL2;

namespace VEngine.Engine.Input;

public class KeyboardInput
{
    private readonly HashSet<SDL.SDL_Scancode> _down = new();
    private readonly HashSet<SDL.SDL_Scancode> _pressed = new();
    private readonly HashSet<SDL.SDL_Scancode> _released = new();
    private readonly HashSet<SDL.SDL_Scancode> _pressedBuffer = new();
    private readonly HashSet<SDL.SDL_Scancode> _releasedBuffer = new();
    private bool _inFixedUpdate;

    /// <summary>True while the key is held down.</summary>
    public bool IsDown(SDL.SDL_Scancode key) => _down.Contains(key);

    /// <summary>True only on the first frame the key is pressed.</summary>
    public bool IsPressed(SDL.SDL_Scancode key) => (_inFixedUpdate ? _pressedBuffer : _pressed).Contains(key);

    /// <summary>True only on the frame the key is released.</summary>
    public bool IsReleased(SDL.SDL_Scancode key) => (_inFixedUpdate ? _releasedBuffer : _released).Contains(key);

    // Convenience overloads using SDL_Keycode
    public bool IsDown(SDL.SDL_Keycode key) => IsDown(SDL.SDL_GetScancodeFromKey(key));
    public bool IsPressed(SDL.SDL_Keycode key) => IsPressed(SDL.SDL_GetScancodeFromKey(key));
    public bool IsReleased(SDL.SDL_Keycode key) => IsReleased(SDL.SDL_GetScancodeFromKey(key));

    internal void BeginFrame()
    {
        _pressed.Clear();
        _released.Clear();
    }

    internal void BeginFixedUpdate() => _inFixedUpdate = true;

    internal void EndFixedUpdate()
    {
        _pressedBuffer.Clear();
        _releasedBuffer.Clear();
        _inFixedUpdate = false;
    }

    internal void ProcessEvent(SDL.SDL_Event e)
    {
        if (e.type == SDL.SDL_EventType.SDL_KEYDOWN && e.key.repeat == 0)
        {
            var code = e.key.keysym.scancode;
            _down.Add(code);
            _pressed.Add(code);
            _pressedBuffer.Add(code);
        }
        else if (e.type == SDL.SDL_EventType.SDL_KEYUP)
        {
            var code = e.key.keysym.scancode;
            _down.Remove(code);
            _released.Add(code);
            _releasedBuffer.Add(code);
        }
    }
}
