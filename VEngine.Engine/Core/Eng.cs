using System;
using System.IO;
using VEngine.Engine.Audio;
using VEngine.Engine.Graphics;
using VEngine.Engine.Graphics.GL;
using VEngine.Engine.Input;
using VEngine.Engine.Math;
using VEngine.Engine.Physics;

namespace VEngine.Engine.Core;

/// <summary>
/// Holds all engine subsystems. The default instance is accessed via the static Eng class.
/// Create a custom instance for unit testing or running multiple engines.
/// </summary>
public class EngineContext
{
    public Game? Game { get; init; }
    public GLRenderer? GL { get; init; }
    public KeyboardInput Input { get; init; } = new();
    public MouseInput Mouse { get; init; } = new();
    public GamepadInput Gamepad { get; init; } = new();
    public TimeInfo Time { get; init; } = new();
    public Camera Camera { get; set; } = new();
    public AudioManager? Audio { get; init; }
    public TimerManager Timers { get; init; } = new();
    public TweenManager Tweens { get; init; } = new();
    public DebugOverlay? Debug { get; init; }
    public Effects Effects { get; init; } = new();
    public PostProcess? PostProcess { get; init; }
    public VolumeOverlay? Volume { get; init; }
    public InputMap Actions { get; init; } = new();
    public PhysicsWorld Physics { get; init; } = new();
    public Localization Locale { get; init; } = new();
    public DebugConsole Console { get; init; } = new();
    public string AssetsPath { get; init; } = "";
    public bool PixelPerfect { get; set; } = true; // mutable at runtime by design

    /// <summary>Active UITextInput receiving SDL_TEXTINPUT events. Null if no widget is focused.</summary>
    internal UI.UITextInput? TextInputTarget;
}

/// <summary>
/// Global static access to engine subsystems. Delegates to an EngineContext instance.
/// For testing, swap Eng.Context to inject mocks. For normal use, just access Eng.* directly.
/// </summary>
public static class Eng
{
    /// <summary>
    /// The current engine context. Swap this in tests to inject mock subsystems.
    /// </summary>
    public static EngineContext Context { get; set; } = new();

    // ── Subsystem Accessors ─────────────────────────────────────

    public static Game Game => Context.Game ?? throw new InvalidOperationException("Game not initialized. Call Game.Start() first.");
    public static GLRenderer GL => Context.GL ?? throw new InvalidOperationException("GLRenderer not initialized. Call Game.Start() or use InitHeadless() for testing.");
    public static KeyboardInput Input => Context.Input;
    public static MouseInput Mouse => Context.Mouse;
    public static GamepadInput Gamepad => Context.Gamepad;
    public static TimeInfo Time => Context.Time;
    public static Camera Camera => Context.Camera;
    public static AudioManager Audio => Context.Audio ?? throw new InvalidOperationException("Audio not initialized. Call Game.Start() first.");
    public static TimerManager Timers => Context.Timers;
    public static TweenManager Tweens => Context.Tweens;
    public static DebugOverlay Debug => Context.Debug ?? throw new InvalidOperationException("Debug not initialized. Call Game.Start() first.");
    public static Effects Effects => Context.Effects;
    public static PostProcess PostProcess => Context.PostProcess ?? throw new InvalidOperationException("PostProcess not initialized. Call Game.Start() first.");
    public static VolumeOverlay Volume => Context.Volume ?? throw new InvalidOperationException("Volume not initialized. Call Game.Start() first.");
    public static InputMap Actions => Context.Actions;
    public static PhysicsWorld Physics => Context.Physics;
    public static Localization Locale => Context.Locale;
    public static DebugConsole Console => Context.Console;

    /// <summary>True while a <see cref="UI.UITextInput"/> widget has focus. Use this to gate
    /// scene-level keyboard shortcuts so typing into a text field doesn't also trigger them.</summary>
    public static bool HasFocusedTextInput => Context.TextInputTarget != null;

    /// <summary>Shortcut for Eng.Locale.Text(key, args). Named "Tr" (translate) to avoid
    /// collision with the <see cref="Graphics.Text"/> render entity.</summary>
    public static string Tr(string key, params object[] args) => Context.Locale.Text(key, args);
    public static AssetLoader Loader { get; } = new();

    // ── Lifecycle Hooks ─────────────────────────────────────────

    /// <summary>Fires each fixed timestep, before the scene updates.</summary>
    public static readonly Signal<float> OnPreUpdate = new();

    /// <summary>Fires each fixed timestep, after the scene and camera update.</summary>
    public static readonly Signal<float> OnPostUpdate = new();

    /// <summary>Fires each frame, before the scene draws.</summary>
    public static readonly Signal OnPreDraw = new();

    /// <summary>Fires each frame, after all overlays draw but before post-process and present.</summary>
    public static readonly Signal OnPostDraw = new();

    // ── Convenience Properties ──────────────────────────────────

    /// <summary>Global time scale. 1 = normal, 0 = frozen.</summary>
    public static float TimeScale
    {
        get => Effects.TimeScale;
        set => Effects.TimeScale = value;
    }

    public static int Width => Game.Width;
    public static int Height => Game.Height;
    public static string AssetsPath => Context.AssetsPath;

    public static bool PixelPerfect
    {
        get => Context.PixelPerfect;
        set => Context.PixelPerfect = value;
    }

    /// <summary>Show or hide the OS mouse cursor.</summary>
    public static bool ShowCursor
    {
        get => SDL2.SDL.SDL_ShowCursor(SDL2.SDL.SDL_QUERY) == SDL2.SDL.SDL_ENABLE;
        set => SDL2.SDL.SDL_ShowCursor(value ? SDL2.SDL.SDL_ENABLE : SDL2.SDL.SDL_DISABLE);
    }

    private static IntPtr _customCursor;        // currently active cursor
    private static IntPtr _defaultCursor;        // "default" mode cursor
    private static IntPtr _hoverCursor;          // "hover" mode cursor
    private static int _defaultHotX, _defaultHotY, _defaultScale = 1;
    private static int _hoverHotX, _hoverHotY, _hoverScale = 1;
    private static bool _hoverFrameClaimed;
    private static bool _wasHoverLastFrame;

    /// <summary>
    /// Mark that something clickable is under the cursor this frame. The engine will switch
    /// to the hover cursor (if one was set via SetHoverCursor). Call from any clickable
    /// entity's Update when its hit-test passes.
    /// </summary>
    public static void MarkCursorHoverable() => _hoverFrameClaimed = true;

    /// <summary>
    /// Set the cursor used when nothing claimed hover this frame. This is the "default" cursor.
    /// </summary>
    public static void SetCursor(string? imagePath, int hotX, int hotY, int scale)
    {
        SetCursorInternal(imagePath, hotX, hotY, scale, isHover: false);
    }

    /// <summary>
    /// Set the cursor used when something marked itself hoverable via MarkCursorHoverable().
    /// Pass null to clear the hover cursor (always use the default).
    /// </summary>
    public static void SetHoverCursor(string? imagePath, int hotX = 0, int hotY = 0, int scale = 1)
    {
        if (_hoverCursor != IntPtr.Zero)
        {
            SDL2.SDL.SDL_FreeCursor(_hoverCursor);
            _hoverCursor = IntPtr.Zero;
        }

        if (string.IsNullOrEmpty(imagePath)) return;

        _hoverCursor = LoadCursorHandle(imagePath, hotX, hotY, scale);
        _hoverHotX = hotX; _hoverHotY = hotY; _hoverScale = scale;
    }

    /// <summary>
    /// Called by Game each render frame to apply the hover cursor swap. Internal.
    /// Only reads the flag — does not reset it (the flag is reset at the start of each
    /// fixed update tick so it persists across render frames between updates).
    /// </summary>
    internal static void ResolveCursorHover()
    {
        bool isHover = _hoverFrameClaimed && _hoverCursor != IntPtr.Zero;
        if (isHover != _wasHoverLastFrame)
        {
            _wasHoverLastFrame = isHover;
            var target = isHover ? _hoverCursor : _defaultCursor;
            if (target != IntPtr.Zero)
                SDL2.SDL.SDL_SetCursor(target);
        }
    }

    /// <summary>Called at the start of each fixed update tick to reset the hover flag.</summary>
    internal static void ResetCursorHoverFlag() => _hoverFrameClaimed = false;

    /// <summary>
    /// Set a custom mouse cursor from an image file (PNG/JPG/BMP). Hot-spot is the click point.
    /// Pass null to restore the default OS cursor. Scale must be a positive integer.
    /// </summary>
    public static void SetCursor(string? imagePath, int hotX = 0, int hotY = 0)
        => SetCursorInternal(imagePath, hotX, hotY, scale: 1, isHover: false);

    private static void SetCursorInternal(string? imagePath, int hotX, int hotY, int scale, bool isHover)
    {
        // Free the previous "default" cursor (we don't manage hover here)
        if (_defaultCursor != IntPtr.Zero)
        {
            SDL2.SDL.SDL_FreeCursor(_defaultCursor);
            _defaultCursor = IntPtr.Zero;
        }
        _customCursor = IntPtr.Zero;

        if (string.IsNullOrEmpty(imagePath))
        {
            var system = SDL2.SDL.SDL_CreateSystemCursor(SDL2.SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_ARROW);
            SDL2.SDL.SDL_SetCursor(system);
            return;
        }

        _defaultCursor = LoadCursorHandle(imagePath, hotX, hotY, scale);
        _customCursor = _defaultCursor;
        _defaultHotX = hotX; _defaultHotY = hotY; _defaultScale = scale;
        if (_defaultCursor != IntPtr.Zero)
            SDL2.SDL.SDL_SetCursor(_defaultCursor);
    }

    private static unsafe IntPtr LoadCursorHandle(string imagePath, int hotX, int hotY, int scale)
    {
        if (scale < 1) scale = 1;
        var fullPath = System.IO.Path.IsPathRooted(imagePath) ? imagePath : Asset(imagePath);

        StbImageSharp.ImageResult image;
        try
        {
            using var stream = System.IO.File.OpenRead(fullPath);
            image = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[Cursor] Failed to decode '{imagePath}': {ex.Message}");
            return IntPtr.Zero;
        }

        int srcW = image.Width, srcH = image.Height;
        int dstW = srcW * scale;
        int dstH = srcH * scale;

        byte[] pixels;
        if (scale == 1)
        {
            pixels = image.Data;
        }
        else
        {
            pixels = new byte[dstW * dstH * 4];
            for (int y = 0; y < dstH; y++)
            {
                int sy = y / scale;
                for (int x = 0; x < dstW; x++)
                {
                    int sx = x / scale;
                    int srcIdx = (sy * srcW + sx) * 4;
                    int dstIdx = (y * dstW + x) * 4;
                    pixels[dstIdx]     = image.Data[srcIdx];
                    pixels[dstIdx + 1] = image.Data[srcIdx + 1];
                    pixels[dstIdx + 2] = image.Data[srcIdx + 2];
                    pixels[dstIdx + 3] = image.Data[srcIdx + 3];
                }
            }
        }

        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var surface = SDL2.SDL.SDL_CreateRGBSurfaceFrom(
                pinned.AddrOfPinnedObject(),
                dstW, dstH, 32, dstW * 4,
                0x000000FF, 0x0000FF00, 0x00FF0000, 0xFF000000);

            if (surface == IntPtr.Zero)
            {
                System.Console.WriteLine($"[Cursor] SDL_CreateRGBSurfaceFrom failed: {SDL2.SDL.SDL_GetError()}");
                return IntPtr.Zero;
            }

            var cursor = SDL2.SDL.SDL_CreateColorCursor(surface, hotX * scale, hotY * scale);
            SDL2.SDL.SDL_FreeSurface(surface);

            if (cursor == IntPtr.Zero)
            {
                System.Console.WriteLine($"[Cursor] SDL_CreateColorCursor failed: {SDL2.SDL.SDL_GetError()}");
                return IntPtr.Zero;
            }
            return cursor;
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>Enable or disable vertical sync.</summary>
    public static bool VSync
    {
        get => GL.VSync;
        set => GL.VSync = value;
    }

    public static void ToggleFullscreen() => Game.ToggleFullscreen();
    public static void SwitchScene(Scene scene, Transition? transition = null) => Game.SwitchScene(scene, transition);
    /// <summary>Push a scene onto the stack (current scene paused, not destroyed). For pause menus, overlays.</summary>
    public static void PushScene(Scene scene) => Game.PushScene(scene);
    /// <summary>Pop the top scene and resume the one underneath.</summary>
    public static bool PopScene() => Game.PopScene();
    public static void Quit() => Game.Quit();

    /// <summary>
    /// Capture a screenshot at the end of the current frame (after post-processing).
    /// Returns the file path. Pass null for auto-generated timestamped filename.
    /// </summary>
    public static string CaptureScreen(string? path = null) => ScreenCapture.Capture(path);

    // ── Initialization ──────────────────────────────────────────

    /// <summary>
    /// Initialize engine subsystems without a renderer or window.
    /// For unit testing, headless servers, or CI.
    /// </summary>
    public static void InitHeadless()
    {
        Context = new EngineContext();
        // Clear lifecycle hooks to prevent listener leakage between test runs
        OnPreUpdate.Clear();
        OnPostUpdate.Clear();
        OnPreDraw.Clear();
        OnPostDraw.Clear();
    }

    /// <summary>
    /// Resolves an asset filename to its full path inside the Assets folder.
    /// </summary>
    public static string Asset(string name)
    {
        var full = Path.Combine(AssetsPath, name);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Asset not found: {full}", full);
        return full;
    }

    /// <summary>
    /// Try to resolve an asset path. Returns false if the file doesn't exist (no exception).
    /// </summary>
    public static bool TryAsset(string name, out string path)
    {
        path = Path.Combine(AssetsPath, name);
        return File.Exists(path);
    }

    /// <summary>Target update rate. Default 60.</summary>
    public static int TargetFps
    {
        get => Game.TargetFps;
        set => Game.TargetFps = value;
    }

    public static bool Fullscreen
    {
        get => Game.Fullscreen;
        set => Game.Fullscreen = value;
    }

    internal static void Init(Game game, GLRenderer glRenderer)
    {
        Context = new EngineContext
        {
            Game = game,
            GL = glRenderer,
            Audio = new AudioManager(),
            Debug = new DebugOverlay(),
            PostProcess = new PostProcess(),
            Volume = new VolumeOverlay(),
            AssetsPath = Path.Combine(AppContext.BaseDirectory, "Assets"),
        };
        Context.PostProcess.Init(game.Width, game.Height);
        StbImageSharp.StbImage.stbi_set_flip_vertically_on_load(0);
        RegisterEngineActions();
        if (SDL2.SDL_ttf.TTF_Init() < 0)
            throw new Exception($"SDL_ttf init failed: {SDL2.SDL.SDL_GetError()}");
    }

    private static void RegisterEngineActions()
    {
        Actions.Bind("engine:debug", SDL2.SDL.SDL_Scancode.SDL_SCANCODE_F3);
        Actions.Bind("engine:fullscreen", SDL2.SDL.SDL_Scancode.SDL_SCANCODE_F11);
        Actions.Bind("engine:volume_up",
            SDL2.SDL.SDL_Scancode.SDL_SCANCODE_EQUALS,
            SDL2.SDL.SDL_Scancode.SDL_SCANCODE_KP_PLUS);
        Actions.Bind("engine:volume_down",
            SDL2.SDL.SDL_Scancode.SDL_SCANCODE_MINUS,
            SDL2.SDL.SDL_Scancode.SDL_SCANCODE_KP_MINUS);
        Actions.Bind("engine:screenshot", SDL2.SDL.SDL_Scancode.SDL_SCANCODE_F12);
        Actions.Bind("engine:console", SDL2.SDL.SDL_Scancode.SDL_SCANCODE_GRAVE);
    }
}

public class TimeInfo
{
    public float Elapsed { get; private set; }
    public float Total { get; private set; }
    public int FrameCount { get; private set; }

    internal void Update(float dt)
    {
        Elapsed = dt;
        Total += dt;
        FrameCount++;
    }
}
