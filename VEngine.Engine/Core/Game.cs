using System;
using SDL2;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Core;

public class Game
{
    private IntPtr _window;
    private GLRenderer _gl = null!;
    private bool _running;
    private bool _fullscreen;

    public int Width { get; }
    public int Height { get; }
    public string Title { get; }

    /// <summary>Get or set fullscreen mode (borderless desktop fullscreen).</summary>
    public bool Fullscreen
    {
        get => _fullscreen;
        set
        {
            _fullscreen = value;
            SDL.SDL_SetWindowFullscreen(_window,
                value ? (uint)SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP : 0);
            SDL.SDL_GetWindowSize(_window, out int winW, out int winH);
            _gl?.UpdateViewport(winW, winH);
        }
    }

    /// <summary>Toggle between windowed and fullscreen.</summary>
    public void ToggleFullscreen() => Fullscreen = !_fullscreen;

    private double _targetFps;
    private double _fixedDeltaTime;

    /// <summary>Target update rate. Default 60. Changing at runtime affects physics tick rate.</summary>
    public int TargetFps
    {
        get => (int)_targetFps;
        set { _targetFps = System.Math.Max(1, value); _fixedDeltaTime = 1.0 / _targetFps; }
    }

    /// <summary>Fixed timestep duration in seconds (1 / TargetFps).</summary>
    public double FixedDeltaTime => _fixedDeltaTime;

    private Scene? _currentScene;

    /// <summary>The currently active scene (or null if none).</summary>
    public Scene? CurrentScene => _currentScene;
    private Scene? _pendingScene;
    private readonly Stack<Scene> _sceneStack = new();

    // Transition
    private Transition? _transition;
    private float _transitionTimer;
    private float _transitionProgress;
    private bool _transitionSwapped;

    private string? _pendingCapture;

    private const float VolumeStep = 0.1f;
    private bool _volumeTickWarned;

    public Game(string title, int width, int height, int targetFps = 60)
    {
        Title = title;
        Width = width;
        Height = height;
        _targetFps = System.Math.Max(1, targetFps);
        _fixedDeltaTime = 1.0 / _targetFps;
    }

    public void Start(Scene initialScene)
    {
        if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO | SDL.SDL_INIT_AUDIO | SDL.SDL_INIT_GAMECONTROLLER) < 0)
            throw new Exception($"SDL Init failed: {SDL.SDL_GetError()}");

        SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_MAJOR_VERSION, 3);
        SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_MINOR_VERSION, 3);
        SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_PROFILE_MASK,
            (int)SDL.SDL_GLprofile.SDL_GL_CONTEXT_PROFILE_CORE);
        SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_DOUBLEBUFFER, 1);
        SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_DEPTH_SIZE, 24);

        _window = SDL.SDL_CreateWindow(
            Title,
            SDL.SDL_WINDOWPOS_CENTERED, SDL.SDL_WINDOWPOS_CENTERED,
            Width, Height,
            SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN |
            SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE |
            SDL.SDL_WindowFlags.SDL_WINDOW_OPENGL
        );
        if (_window == IntPtr.Zero)
            throw new Exception($"Window creation failed: {SDL.SDL_GetError()}");

        _gl = new GLRenderer(_window, Width, Height);
        Eng.Init(this, _gl);
        LoadWindowIcon();
        Eng.Gamepad.Init();

        _pendingScene = initialScene;
        _running = true;

        try { Run(); }
        finally { Shutdown(); }
    }

    // ── Main Loop ───────────────────────────────────────────────

    private void Run()
    {
        var previousTime = SDL.SDL_GetPerformanceCounter();
        var frequency = (double)SDL.SDL_GetPerformanceFrequency();
        double accumulator = 0;

        while (_running)
        {
            var currentTime = SDL.SDL_GetPerformanceCounter();
            var frameTime = (currentTime - previousTime) / frequency;
            previousTime = currentTime;

            // Cap to prevent spiral of death after long pause/breakpoint
            if (frameTime > 0.25) frameTime = 0.25;
            accumulator += frameTime;

            ApplyPendingScene();
            PollEvents();
            HandleEngineActions();

            // Effects run on real time (not affected by TimeScale)
            Eng.Effects.Update((float)frameTime);

            // Fixed timestep update
            while (accumulator >= FixedDeltaTime)
            {
                FixedUpdate();
                Eng.Volume.Update((float)FixedDeltaTime);
                accumulator -= FixedDeltaTime;
            }

            Eng.Audio.CleanupFinishedChannels();
            Eng.Audio.Update((float)frameTime);
            Eng.ResolveCursorHover();
            Render((float)frameTime);
        }
    }

    internal void FixedUpdate()
    {
        Eng.Input.BeginFixedUpdate();
        try
        {
            float scaledDt = (float)FixedDeltaTime * Eng.Effects.TimeScale;
            Eng.Time.Update(scaledDt);
            Eng.Timers.Update(scaledDt);
            Eng.Tweens.Update(scaledDt);

            Eng.ResetCursorHoverFlag();
            Eng.OnPreUpdate.Emit(scaledDt);
            _currentScene?.Update(scaledDt);
            Eng.Physics.Step(scaledDt);
            Eng.Camera.Update(scaledDt);
            Eng.OnPostUpdate.Emit(scaledDt);

            UpdateTransition();
        }
        finally
        {
            Eng.Input.EndFixedUpdate();
            Eng.Mouse.ConsumeBuffered();
        }
    }

    // ── Scene Management ────────────────────────────────────────

    private void ApplyPendingScene()
    {
        if (_pendingScene == null || _transition != null) return;
        SwapScene();
    }

    private void SwapScene()
    {
        // Destroy the previous scene first while its textures are still live,
        // then unload its texture scope. Matches PopScene's ordering.
        _currentScene?.Destroy();
        _gl.EndTextureScope();
        Graphics.Sprite.ClearMetaCache();
        Eng.Timers.CancelAll();
        Eng.Tweens.CancelAll();
        Eng.Physics.Clear();
        _gl.CheckGLError("SwapScene:teardown");
        _currentScene = _pendingScene;
        _pendingScene = null;
        // Begin new scene's texture scope (tracks textures for cleanup on next swap)
        _gl.BeginTextureScope();
        _currentScene!.Create();
        _gl.CheckGLError("SwapScene:setup");
    }

    private void UpdateTransition()
    {
        if (_transition == null) return;

        _transitionTimer += (float)FixedDeltaTime;
        _transitionProgress = System.Math.Clamp(_transitionTimer / _transition.Duration, 0f, 1f);

        if (!_transitionSwapped && _transitionProgress >= 0.5f && _pendingScene != null)
        {
            SwapScene();
            _transitionSwapped = true;
        }

        if (_transitionProgress >= 1f)
            _transition = null;
    }

    public void SwitchScene(Scene scene, Transition? transition = null)
    {
        _pendingScene = scene;
        if (transition != null)
        {
            // New transition replaces any active one (no silent drop)
            _transition = transition;
            _transitionTimer = 0;
            _transitionProgress = 0;
            _transitionSwapped = false;
        }
        else if (_transition != null)
        {
            _transition = null;
        }
    }

    /// <summary>
    /// Push a scene onto the stack. The current scene is paused (its Update does not run while
    /// the pushed scene is active) but its subsystem state — physics bodies, timers, tweens,
    /// textures — is preserved so pop can resume. Texture-loading for the pushed scene is tracked
    /// in its own scope layered above the parent's. Pushed scenes are responsible for cleaning up
    /// any timers / tweens / physics bodies they register, inside their <see cref="Scene.Destroy"/>.
    /// </summary>
    public void PushScene(Scene scene)
    {
        if (_currentScene != null)
            _sceneStack.Push(_currentScene);
        _gl?.BeginTextureScope();
        _currentScene = scene;
        _currentScene.Create();
    }

    /// <summary>
    /// Pop the top scene and resume the scene underneath. The popped scene is destroyed and its
    /// texture scope is unloaded; the parent scene's subsystem state is intact.
    /// Returns false if the stack is empty (nothing to pop to).
    /// </summary>
    public bool PopScene()
    {
        if (_sceneStack.Count == 0) return false;
        _currentScene?.Destroy();
        _gl?.EndTextureScope();
        _currentScene = _sceneStack.Pop();
        return true;
    }

    /// <summary>Number of scenes on the stack (not counting the current scene).</summary>
    public int SceneStackDepth => _sceneStack.Count;

    public void Quit() => _running = false;

    /// <summary>Request a screenshot at the end of the current frame.</summary>
    internal void RequestCapture(string path) => _pendingCapture = path;

    // ── Event Processing ────────────────────────────────────────

    private void PollEvents()
    {
        Eng.Input.BeginFrame();
        Eng.Mouse.BeginFrame();
        Eng.Gamepad.BeginFrame();

        while (SDL.SDL_PollEvent(out var e) != 0)
        {
            if (e.type == SDL.SDL_EventType.SDL_QUIT)
            {
                _running = false;
                break;
            }

            if (e.type == SDL.SDL_EventType.SDL_WINDOWEVENT &&
                e.window.windowEvent == SDL.SDL_WindowEventID.SDL_WINDOWEVENT_SIZE_CHANGED &&
                e.window.data1 > 0 && e.window.data2 > 0)
            {
                _gl.UpdateViewport(e.window.data1, e.window.data2);
                Eng.PostProcess.Resize(Width, Height);
            }

            // Dispatch text input events to focused UITextInput widgets + debug console
            if (e.type == SDL.SDL_EventType.SDL_TEXTINPUT)
            {
                string text = "";
                unsafe { text = new string((sbyte*)e.text.text); }
                foreach (char c in text)
                {
                    if (Eng.Console.Enabled) Eng.Console.HandleTextInput(c);
                    else Eng.Context.TextInputTarget?.AppendChar(c);
                }
            }

            // Console key presses (cursor, history, enter, etc.)
            if (e.type == SDL.SDL_EventType.SDL_KEYDOWN && Eng.Console.Enabled)
            {
                Eng.Console.HandleKeyPress(e.key.keysym.scancode);
            }

            Eng.Input.ProcessEvent(e);
            TransformMouseEvent(ref e);
            Eng.Mouse.ProcessEvent(e);
            Eng.Gamepad.ProcessEvent(e);
        }
    }

    /// <summary>
    /// Handle engine-level actions via InputMap. These use "engine:" prefixed action names
    /// registered in Eng.Init(), so users can rebind them.
    /// </summary>
    private void HandleEngineActions()
    {
        if (Eng.Actions.Pressed("engine:debug"))
            Eng.Debug.Toggle();

        if (Eng.Actions.Pressed("engine:console"))
            Eng.Console.Toggle();

        if (Eng.Actions.Pressed("engine:fullscreen"))
            ToggleFullscreen();

        if (Eng.Actions.Pressed("engine:screenshot"))
            Eng.CaptureScreen();

        if (Eng.Actions.Pressed("engine:volume_up"))
        {
            Eng.Audio.MasterVolume = System.Math.Min(1f, Eng.Audio.MasterVolume + VolumeStep);
            Eng.Volume.Show();
            try { Eng.Audio.PlaySound("sfx/ui/volume-tick.wav", volume: 0.5f); }
                catch (Exception ex) { if (!_volumeTickWarned) { Console.WriteLine($"[Game] Volume tick unavailable: {ex.Message}"); _volumeTickWarned = true; } }
        }
        if (Eng.Actions.Pressed("engine:volume_down"))
        {
            Eng.Audio.MasterVolume = System.Math.Max(0f, Eng.Audio.MasterVolume - VolumeStep);
            Eng.Volume.Show();
            try { Eng.Audio.PlaySound("sfx/ui/volume-tick.wav", volume: 0.5f); }
                catch (Exception ex) { if (!_volumeTickWarned) { Console.WriteLine($"[Game] Volume tick unavailable: {ex.Message}"); _volumeTickWarned = true; } }
        }
    }

    private void TransformMouseEvent(ref SDL.SDL_Event e)
    {
        bool isMotion = e.type == SDL.SDL_EventType.SDL_MOUSEMOTION;
        bool isButton = e.type == SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN ||
                        e.type == SDL.SDL_EventType.SDL_MOUSEBUTTONUP;
        if (!isMotion && !isButton) return;

        SDL.SDL_GetWindowSize(_window, out int winW, out int winH);
        if (winW == Width && winH == Height) return;

        float scaleX = (float)winW / Width;
        float scaleY = (float)winH / Height;
        float scale = MathF.Min(scaleX, scaleY);
        float offsetX = (winW - Width * scale) / 2f;
        float offsetY = (winH - Height * scale) / 2f;

        if (isMotion)
        {
            e.motion.x = System.Math.Clamp((int)((e.motion.x - offsetX) / scale), 0, Width - 1);
            e.motion.y = System.Math.Clamp((int)((e.motion.y - offsetY) / scale), 0, Height - 1);
        }
        else
        {
            e.button.x = System.Math.Clamp((int)((e.button.x - offsetX) / scale), 0, Width - 1);
            e.button.y = System.Math.Clamp((int)((e.button.y - offsetY) / scale), 0, Height - 1);
        }
    }

    // ── Rendering ───────────────────────────────────────────────

    private void Render(float realDt)
    {
        _gl.BeginFrame();
        Eng.PostProcess.Begin();

        Eng.OnPreDraw.Emit();
        _currentScene?.Draw();
        _gl.FlushAll();

        Eng.Effects.Draw();
        _transition?.Draw(_transitionProgress);
        Eng.Debug.Draw(_currentScene, realDt);
        Eng.Debug.TraceFrame(realDt);
        Eng.Console.Update(realDt);
        Eng.Console.Draw();
        Eng.Volume.Draw();
        Eng.OnPostDraw.Emit();

        _gl.FlushAll();
        Eng.PostProcess.End(Eng.Time.Total);

        if (_pendingCapture != null)
        {
            ScreenCapture.PerformCapture(_pendingCapture);
            _pendingCapture = null;
        }

        _gl.EndFrame();
    }

    // ── Init Helpers ────────────────────────────────────────────

    private void LoadWindowIcon()
    {
        try
        {
            var iconPath = Eng.Asset("ui/v-engine-icon.png");
            var iconSurface = SDL.SDL_LoadBMP(iconPath);
            if (iconSurface == IntPtr.Zero)
            {
                using var stream = System.IO.File.OpenRead(iconPath);
                var image = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
                var pinned = System.Runtime.InteropServices.GCHandle.Alloc(image.Data, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    iconSurface = SDL.SDL_CreateRGBSurfaceFrom(
                        pinned.AddrOfPinnedObject(),
                        image.Width, image.Height, 32, image.Width * 4,
                        0x000000FFu, 0x0000FF00u, 0x00FF0000u, 0xFF000000u
                    );
                    SDL.SDL_SetWindowIcon(_window, iconSurface);
                    SDL.SDL_FreeSurface(iconSurface);
                }
                finally { pinned.Free(); }
            }
            else
            {
                SDL.SDL_SetWindowIcon(_window, iconSurface);
                SDL.SDL_FreeSurface(iconSurface);
            }
        }
        catch (Exception ex) { Console.WriteLine($"[Game] Window icon not loaded: {ex.Message}"); }
    }

    // ── Shutdown ─────────────────────────────────────────────────

    private void Shutdown()
    {
        _currentScene?.Destroy();
        Eng.Volume.Shutdown();
        Eng.Debug.Shutdown();
        UI.UIElement.ShutdownFonts();
        Graphics.Text.ShutdownFonts();
        Graphics.PortalEffect.Shutdown();
        Eng.Audio.Shutdown();
        Eng.Gamepad.Shutdown();
        Eng.PostProcess.Destroy();
        _gl.Dispose();
        SDL.SDL_DestroyWindow(_window);
        SDL.SDL_Quit();
    }
}
