using System;
using SDL2;
using VEngine.Engine.Core;
using VEngine.Engine.Math;

namespace VEngine.Engine.Input;

/// <summary>
/// Mouse button identifiers.
/// </summary>
public enum MouseButton
{
    Left = 1,
    Middle = 2,
    Right = 3,
}

/// <summary>
/// Mouse input: position, buttons, scroll wheel.
/// WorldPosition converts screen coords to world coords via the camera.
/// </summary>
public class MouseInput
{
    private int _x, _y;
    private readonly bool[] _down = new bool[6];    // indices 1-5 (SDL button IDs)
    private readonly bool[] _pressed = new bool[6];
    private readonly bool[] _released = new bool[6];
    private readonly bool[] _pressedBuffer = new bool[6];  // survives until fixed update consumes it
    private readonly bool[] _releasedBuffer = new bool[6];
    private int _scrollX, _scrollY;

    /// <summary>Mouse X in screen coordinates.</summary>
    public int X => _x;

    /// <summary>Mouse Y in screen coordinates.</summary>
    public int Y => _y;

    /// <summary>Mouse position in screen coordinates.</summary>
    public Vec2 Position => new(_x, _y);

    /// <summary>Mouse position in world coordinates (accounts for camera position, zoom, and shake).</summary>
    public Vec2 WorldPosition => Eng.Camera.ScreenToWorld(_x, _y);

    /// <summary>Horizontal scroll delta this frame.</summary>
    public int ScrollX => _scrollX;

    /// <summary>Vertical scroll delta this frame (positive = up).</summary>
    public int ScrollY => _scrollY;

    /// <summary>True while the button is held down.</summary>
    public bool IsDown(MouseButton button) => _down[(int)button];

    /// <summary>True only on the first frame the button is pressed. Buffered for fixed timestep.</summary>
    public bool IsPressed(MouseButton button) => _pressed[(int)button] || _pressedBuffer[(int)button];

    /// <summary>True only on the frame the button is released. Buffered for fixed timestep.</summary>
    public bool IsReleased(MouseButton button) => _released[(int)button] || _releasedBuffer[(int)button];

    internal void BeginFrame()
    {
        // Move current pressed/released into buffer (survives until ConsumeBuffered clears it)
        for (int i = 0; i < _pressed.Length; i++)
        {
            if (_pressed[i]) _pressedBuffer[i] = true;
            if (_released[i]) _releasedBuffer[i] = true;
        }
        Array.Clear(_pressed);
        Array.Clear(_released);
        _scrollX = 0;
        _scrollY = 0;
    }

    /// <summary>Clear buffered press/release state. Called after fixed timestep update runs.</summary>
    internal void ConsumeBuffered()
    {
        Array.Clear(_pressed);
        Array.Clear(_released);
        Array.Clear(_pressedBuffer);
        Array.Clear(_releasedBuffer);
    }

    /// <summary>Simulate a button press (for testing). Sets both down and pressed.</summary>
    internal void SimulatePress(MouseButton button)
    {
        _down[(int)button] = true;
        _pressed[(int)button] = true;
    }

    /// <summary>Simulate a button release (for testing).</summary>
    internal void SimulateRelease(MouseButton button)
    {
        _down[(int)button] = false;
        _released[(int)button] = true;
    }

    internal void ProcessEvent(SDL.SDL_Event e)
    {
        switch (e.type)
        {
            case SDL.SDL_EventType.SDL_MOUSEMOTION:
                _x = e.motion.x;
                _y = e.motion.y;
                break;

            case SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
                if (e.button.button < _down.Length)
                {
                    _down[e.button.button] = true;
                    _pressed[e.button.button] = true;
                }
                break;

            case SDL.SDL_EventType.SDL_MOUSEBUTTONUP:
                if (e.button.button < _down.Length)
                {
                    _down[e.button.button] = false;
                    _released[e.button.button] = true;
                }
                break;

            case SDL.SDL_EventType.SDL_MOUSEWHEEL:
                _scrollX += e.wheel.x;
                _scrollY += e.wheel.y;
                break;
        }
    }
}
