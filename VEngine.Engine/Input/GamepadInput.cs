using System;
using System.Collections.Generic;
using SDL2;
using VEngine.Engine.Math;

namespace VEngine.Engine.Input;

/// <summary>
/// Gamepad input via SDL2 GameController API.
/// Automatically connects to the first available controller and handles hot-plug.
/// </summary>
public class GamepadInput
{
    private IntPtr _controller;
    private int _instanceId = -1;

    private readonly HashSet<SDL.SDL_GameControllerButton> _down = new();
    private readonly HashSet<SDL.SDL_GameControllerButton> _pressed = new();
    private readonly HashSet<SDL.SDL_GameControllerButton> _released = new();

    private float _leftStickX, _leftStickY;
    private float _rightStickX, _rightStickY;
    private float _leftTrigger, _rightTrigger;

    /// <summary>Whether a gamepad is currently connected.</summary>
    public bool Connected => _controller != IntPtr.Zero;

    /// <summary>Analog stick deadzone threshold (0-1). Default 0.15.</summary>
    public float DeadZone { get; set; } = 0.15f;

    /// <summary>True while the button is held down.</summary>
    public bool IsDown(SDL.SDL_GameControllerButton button) => _down.Contains(button);

    /// <summary>True only on the first frame the button is pressed.</summary>
    public bool IsPressed(SDL.SDL_GameControllerButton button) => _pressed.Contains(button);

    /// <summary>True only on the frame the button is released.</summary>
    public bool IsReleased(SDL.SDL_GameControllerButton button) => _released.Contains(button);

    /// <summary>True if any button was pressed this frame.</summary>
    public bool AnyPressed => _pressed.Count > 0;

    /// <summary>Left analog stick (-1 to 1 per axis, deadzone applied). Y positive = down.</summary>
    public Vec2 LeftStick => new(_leftStickX, _leftStickY);

    /// <summary>Right analog stick (-1 to 1 per axis, deadzone applied). Y positive = down.</summary>
    public Vec2 RightStick => new(_rightStickX, _rightStickY);

    /// <summary>Left trigger (0 to 1).</summary>
    public float LeftTrigger => _leftTrigger;

    /// <summary>Right trigger (0 to 1).</summary>
    public float RightTrigger => _rightTrigger;

    internal void BeginFrame()
    {
        _pressed.Clear();
        _released.Clear();
    }

    internal void ProcessEvent(SDL.SDL_Event e)
    {
        switch (e.type)
        {
            case SDL.SDL_EventType.SDL_CONTROLLERDEVICEADDED:
                if (_controller == IntPtr.Zero)
                    Open(e.cdevice.which);
                break;

            case SDL.SDL_EventType.SDL_CONTROLLERDEVICEREMOVED:
                if (e.cdevice.which == _instanceId)
                    Close();
                break;

            case SDL.SDL_EventType.SDL_CONTROLLERBUTTONDOWN:
                if (e.cbutton.which == _instanceId && Enum.IsDefined(typeof(SDL.SDL_GameControllerButton), (int)e.cbutton.button))
                {
                    var btn = (SDL.SDL_GameControllerButton)e.cbutton.button;
                    _down.Add(btn);
                    _pressed.Add(btn);
                }
                break;

            case SDL.SDL_EventType.SDL_CONTROLLERBUTTONUP:
                if (e.cbutton.which == _instanceId && Enum.IsDefined(typeof(SDL.SDL_GameControllerButton), (int)e.cbutton.button))
                {
                    var btn = (SDL.SDL_GameControllerButton)e.cbutton.button;
                    _down.Remove(btn);
                    _released.Add(btn);
                }
                break;

            case SDL.SDL_EventType.SDL_CONTROLLERAXISMOTION:
                if (e.caxis.which == _instanceId)
                    UpdateAxis((SDL.SDL_GameControllerAxis)e.caxis.axis, e.caxis.axisValue);
                break;
        }
    }

    internal void Init()
    {
        int count = SDL.SDL_NumJoysticks();
        for (int i = 0; i < count; i++)
        {
            if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_TRUE)
            {
                Open(i);
                break;
            }
        }
    }

    internal void Shutdown()
    {
        Close();
    }

    private void Open(int deviceIndex)
    {
        _controller = SDL.SDL_GameControllerOpen(deviceIndex);
        if (_controller != IntPtr.Zero)
        {
            var joystick = SDL.SDL_GameControllerGetJoystick(_controller);
            _instanceId = SDL.SDL_JoystickInstanceID(joystick);
        }
    }

    private void Close()
    {
        if (_controller != IntPtr.Zero)
        {
            SDL.SDL_GameControllerClose(_controller);
            _controller = IntPtr.Zero;
            _instanceId = -1;
            _down.Clear();
            _leftStickX = _leftStickY = 0;
            _rightStickX = _rightStickY = 0;
            _leftTrigger = _rightTrigger = 0;
        }
    }

    private void UpdateAxis(SDL.SDL_GameControllerAxis axis, short value)
    {
        float normalized = System.Math.Clamp(value / 32767f, -1f, 1f);

        switch (axis)
        {
            case SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX:
                _leftStickX = ApplyDeadZone(normalized);
                break;
            case SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY:
                _leftStickY = ApplyDeadZone(normalized);
                break;
            case SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX:
                _rightStickX = ApplyDeadZone(normalized);
                break;
            case SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY:
                _rightStickY = ApplyDeadZone(normalized);
                break;
            case SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT:
                _leftTrigger = System.Math.Clamp(normalized, 0f, 1f);
                break;
            case SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT:
                _rightTrigger = System.Math.Clamp(normalized, 0f, 1f);
                break;
        }
    }

    private float ApplyDeadZone(float value)
    {
        float abs = MathF.Abs(value);
        if (abs < DeadZone) return 0f;
        float sign = MathF.Sign(value);
        return sign * (abs - DeadZone) / (1f - DeadZone);
    }
}
