using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SDL2;
using VEngine.Engine.Core;

namespace VEngine.Engine.Scripting;

/// <summary>
/// Declarative JSON plugin — authored without writing any code.
/// A plugin is a set of triggers (when this happens) and actions (do this).
///
/// Example (plugins/scene_refresh.json):
/// {
///   "name": "Scene Refresh",
///   "version": "1.1",
///   "triggers": [
///     { "on": "key_pressed", "key": "F1", "do": [{"toast": "Reloading"}, "reload_scene"] }
///   ]
/// }
///
/// See docs/plugins.md for the full trigger/action reference.
/// </summary>
public class DeclarativePlugin
{
    public string Name = "Unknown";
    public string Version = "1.0";
    public string Description = "";
    public string FilePath = "";
    public bool Enabled = true;

    /// <summary>Plugin-local variables (set_var / get_var).</summary>
    public readonly Dictionary<string, object> Vars = new();

    /// <summary>Per-plugin state used by cycle_scene, toggles, etc.</summary>
    public readonly Dictionary<string, int> CycleIndex = new();

    /// <summary>List of parsed triggers. Evaluated each frame.</summary>
    public readonly List<Trigger> Triggers = new();

    /// <summary>Persistent runtime state — keyed by action config, holds spawned entities like particle emitters.</summary>
    internal readonly Dictionary<string, object> EffectState = new();

    public class Trigger
    {
        public string On = "";                 // key_pressed, key_held, every_frame, mouse_pressed, mouse_held, timer, event, scene_loaded
        public string? Key;                    // for key triggers
        public string? Button;                 // for mouse triggers: "left", "right", "middle"
        public float Interval;                 // for timer triggers
        public float Accumulator;              // timer state
        public string? EventName;              // for event triggers
        public string? SceneName;              // for scene_loaded triggers
        public bool HasFired;                  // for scene_loaded (fire once per load)
        public List<JsonElement> Actions = new();
    }

    /// <summary>Load a JSON plugin from disk.</summary>
    public static DeclarativePlugin? FromFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var plugin = new DeclarativePlugin { FilePath = filePath };

            if (root.TryGetProperty("name", out var nameEl)) plugin.Name = nameEl.GetString() ?? "Unknown";
            else plugin.Name = Path.GetFileNameWithoutExtension(filePath);

            if (root.TryGetProperty("version", out var verEl)) plugin.Version = verEl.GetString() ?? "1.0";
            if (root.TryGetProperty("description", out var descEl)) plugin.Description = descEl.GetString() ?? "";

            if (root.TryGetProperty("triggers", out var triggersEl) && triggersEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var trigEl in triggersEl.EnumerateArray())
                {
                    var trig = ParseTrigger(trigEl);
                    if (trig != null) plugin.Triggers.Add(trig);
                }
            }

            return plugin;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Plugin] Failed to load '{filePath}': {ex.Message}");
            return null;
        }
    }

    private static Trigger? ParseTrigger(JsonElement el)
    {
        if (!el.TryGetProperty("on", out var onEl)) return null;
        var trig = new Trigger { On = onEl.GetString() ?? "" };

        if (el.TryGetProperty("key", out var keyEl)) trig.Key = keyEl.GetString();
        if (el.TryGetProperty("button", out var btnEl)) trig.Button = btnEl.GetString();
        if (el.TryGetProperty("every", out var everyEl)) trig.Interval = (float)everyEl.GetDouble();
        if (el.TryGetProperty("event", out var evtEl)) trig.EventName = evtEl.GetString();
        if (el.TryGetProperty("scene", out var sceneEl)) trig.SceneName = sceneEl.GetString();

        if (el.TryGetProperty("do", out var doEl))
        {
            if (doEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in doEl.EnumerateArray())
                    // Clone the element so it survives after the JsonDocument is disposed
                    trig.Actions.Add(JsonDocument.Parse(action.GetRawText()).RootElement.Clone());
            }
            else
            {
                trig.Actions.Add(JsonDocument.Parse(doEl.GetRawText()).RootElement.Clone());
            }
        }

        return trig;
    }

    /// <summary>Update all triggers: check key input, timers, etc. Called each frame.</summary>
    public void Update(float dt)
    {
        if (!Enabled) return;

        foreach (var trig in Triggers)
        {
            switch (trig.On)
            {
                case "key_pressed":
                    if (trig.Key != null && KeyPressed(trig.Key))
                        ExecuteActions(trig.Actions);
                    break;
                case "key_held":
                    if (trig.Key != null && KeyDown(trig.Key))
                        ExecuteActions(trig.Actions);
                    break;
                case "mouse_pressed":
                    if (trig.Button != null && MousePressed(trig.Button))
                        ExecuteActions(trig.Actions);
                    break;
                case "mouse_held":
                    if (trig.Button != null && MouseDown(trig.Button))
                        ExecuteActions(trig.Actions);
                    break;
                case "every_frame":
                    ExecuteActions(trig.Actions);
                    break;
                case "timer":
                    if (trig.Interval > 0)
                    {
                        trig.Accumulator += dt;
                        while (trig.Accumulator >= trig.Interval)
                        {
                            trig.Accumulator -= trig.Interval;
                            ExecuteActions(trig.Actions);
                        }
                    }
                    break;
            }
        }
    }

    private static bool MousePressed(string buttonName) => Eng.Mouse.IsPressed(ParseButton(buttonName));
    private static bool MouseDown(string buttonName) => Eng.Mouse.IsDown(ParseButton(buttonName));

    private static Input.MouseButton ParseButton(string name) => name.ToUpperInvariant() switch
    {
        "LEFT" or "L" => Input.MouseButton.Left,
        "RIGHT" or "R" => Input.MouseButton.Right,
        "MIDDLE" or "M" => Input.MouseButton.Middle,
        _ => Input.MouseButton.Left
    };

    /// <summary>Called when an event is emitted — checks event triggers.</summary>
    public void OnEvent(string eventName)
    {
        if (!Enabled) return;
        foreach (var trig in Triggers)
        {
            if (trig.On == "event" && trig.EventName == eventName)
                ExecuteActions(trig.Actions);
        }
    }

    /// <summary>Called once when a scene is loaded — fires scene_loaded triggers.</summary>
    public void OnSceneLoaded(string? sceneName)
    {
        if (!Enabled) return;
        foreach (var trig in Triggers)
        {
            if (trig.On == "scene_loaded" &&
                (trig.SceneName == null || trig.SceneName == sceneName))
                ExecuteActions(trig.Actions);
        }
    }

    // ── Action Dispatch ────────────────────────────────────────

    private void ExecuteActions(List<JsonElement> actions)
    {
        foreach (var action in actions)
            ExecuteAction(action);
    }

    private void ExecuteAction(JsonElement action)
    {
        try
        {
            // Action can be a plain string ("reload_scene") or an object with parameters
            if (action.ValueKind == JsonValueKind.String)
            {
                ExecuteNamedAction(action.GetString() ?? "", default);
                return;
            }

            if (action.ValueKind != JsonValueKind.Object) return;

            // Object: first property name is the action name, value is the parameter(s)
            foreach (var prop in action.EnumerateObject())
            {
                ExecuteNamedAction(prop.Name, prop.Value);
                break; // only the first property counts
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Plugin:{Name}] Action error: {ex.Message}");
        }
    }

    private void ExecuteNamedAction(string name, JsonElement param)
    {
        switch (name)
        {
            case "toast":
                Toast(param.ValueKind == JsonValueKind.String ? param.GetString() ?? "" : param.GetRawText());
                break;

            case "log":
                Console.WriteLine($"[{Name}] {(param.ValueKind == JsonValueKind.String ? param.GetString() : param.GetRawText())}");
                break;

            case "reload_scene":
            case "reload":
                // No param — reload current scene by switching to the same one
                // There's no direct reload API, so we use SwitchScene with the current scene type
                ReloadCurrentScene();
                break;

            case "switch_scene":
                if (param.ValueKind == JsonValueKind.String)
                {
                    var path = param.GetString();
                    if (!string.IsNullOrEmpty(path))
                        Eng.SwitchScene(new LuaScene(path));
                }
                break;

            case "cycle_scene":
                if (param.ValueKind == JsonValueKind.Array)
                {
                    var scenes = new List<string>();
                    foreach (var s in param.EnumerateArray())
                        if (s.ValueKind == JsonValueKind.String)
                            scenes.Add(s.GetString() ?? "");
                    if (scenes.Count > 0)
                    {
                        if (!CycleIndex.TryGetValue("scene", out int idx)) idx = 0;
                        idx = (idx + 1) % scenes.Count;
                        CycleIndex["scene"] = idx;
                        Eng.SwitchScene(new LuaScene(scenes[idx]));
                    }
                }
                break;

            case "play_sound":
                if (param.ValueKind == JsonValueKind.String)
                {
                    try { Eng.Audio.PlaySound(param.GetString() ?? ""); }
                    catch (Exception ex) { Console.WriteLine($"[Plugin:{Name}] play_sound failed: {ex.Message}"); }
                }
                break;

            case "play_music":
                if (param.ValueKind == JsonValueKind.String)
                {
                    try { Eng.Audio.PlayMusic(param.GetString() ?? ""); }
                    catch (Exception ex) { Console.WriteLine($"[Plugin:{Name}] play_music failed: {ex.Message}"); }
                }
                else if (param.ValueKind == JsonValueKind.Object &&
                         param.TryGetProperty("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String)
                {
                    var path = pathEl.GetString() ?? "";
                    float crossfade = 0;
                    if (param.TryGetProperty("crossfade", out var cfEl)) crossfade = (float)cfEl.GetDouble();
                    try
                    {
                        if (crossfade > 0) Eng.Audio.CrossFadeMusic(path, crossfade);
                        else Eng.Audio.PlayMusic(path);
                    }
                    catch (Exception ex) { Console.WriteLine($"[Plugin:{Name}] play_music failed: {ex.Message}"); }
                }
                break;

            case "stop_music":
                Eng.Audio.StopMusic();
                break;

            case "quit":
                Eng.Quit();
                break;

            case "screenshot":
                Eng.CaptureScreen();
                break;

            case "screenshot_with_notify":
                // Bundled action: flash screen, capture, show notification card with thumbnail
                DeclarativePluginHost.ScreenshotWithNotify?.Invoke();
                break;

            case "spawn_particles":
                DeclarativePluginHost.SpawnParticles?.Invoke(this, param);
                break;

            case "set_var":
                if (param.ValueKind == JsonValueKind.Object &&
                    param.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                {
                    var varName = nameEl.GetString()!;
                    object? value = null;
                    if (param.TryGetProperty("value", out var valEl))
                    {
                        value = valEl.ValueKind switch
                        {
                            JsonValueKind.String => valEl.GetString(),
                            JsonValueKind.Number => valEl.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => null
                        };
                    }
                    Vars[varName] = value!;
                }
                break;

            case "emit":
                // Emit a named event — caught by event triggers in this or other plugins
                if (param.ValueKind == JsonValueKind.String)
                    DeclarativePluginHost.Emit(param.GetString() ?? "");
                break;

            case "if":
                ExecuteIf(param);
                break;

            case "toggle_plugin":
                if (param.ValueKind == JsonValueKind.String)
                    DeclarativePluginHost.TogglePlugin(param.GetString() ?? "");
                break;

            default:
                Console.WriteLine($"[Plugin:{Name}] Unknown action: {name}");
                break;
        }
    }

    private void ExecuteIf(JsonElement param)
    {
        if (param.ValueKind != JsonValueKind.Object) return;

        // Support simple conditions: { "var": "foo", "equals": "bar", "then": [...], "else": [...] }
        bool condition = false;

        if (param.TryGetProperty("var", out var varEl) && varEl.ValueKind == JsonValueKind.String)
        {
            var varName = varEl.GetString()!;
            Vars.TryGetValue(varName, out var actualValue);

            if (param.TryGetProperty("equals", out var eqEl))
            {
                object? expected = eqEl.ValueKind switch
                {
                    JsonValueKind.String => eqEl.GetString(),
                    JsonValueKind.Number => eqEl.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                };
                condition = Equals(actualValue, expected);
            }
            else if (param.TryGetProperty("truthy", out _))
            {
                condition = actualValue switch
                {
                    null => false,
                    bool b => b,
                    double d => d != 0,
                    string s => !string.IsNullOrEmpty(s),
                    _ => true
                };
            }
        }

        var branch = condition ? "then" : "else";
        if (param.TryGetProperty(branch, out var branchEl))
        {
            if (branchEl.ValueKind == JsonValueKind.Array)
                foreach (var a in branchEl.EnumerateArray())
                    ExecuteAction(a);
            else
                ExecuteAction(branchEl);
        }
    }

    // ── Helpers ────────────────────────────────────────────────

    private void Toast(string message)
    {
        DeclarativePluginHost.ShowToast(message);
    }

    private static void ReloadCurrentScene()
    {
        // Re-emit a scene switch using the same scene path if it was a LuaScene
        // We track the current scene type via the Game object — for now, assume LuaScene
        // and rely on a convention. Alternatively plugins can use switch_scene with the path.
        DeclarativePluginHost.ReloadScene();
    }

    private static bool KeyPressed(string keyName)
    {
        return Eng.Input.IsPressed(ScancodeFromName(keyName));
    }

    private static bool KeyDown(string keyName)
    {
        return Eng.Input.IsDown(ScancodeFromName(keyName));
    }

    private static SDL.SDL_Scancode ScancodeFromName(string name)
    {
        // Map common key names to SDL scancodes. Case-insensitive.
        return name.ToUpperInvariant() switch
        {
            "SPACE" => SDL.SDL_Scancode.SDL_SCANCODE_SPACE,
            "RETURN" or "ENTER" => SDL.SDL_Scancode.SDL_SCANCODE_RETURN,
            "ESCAPE" or "ESC" => SDL.SDL_Scancode.SDL_SCANCODE_ESCAPE,
            "TAB" => SDL.SDL_Scancode.SDL_SCANCODE_TAB,
            "BACKSPACE" => SDL.SDL_Scancode.SDL_SCANCODE_BACKSPACE,
            "LEFT" => SDL.SDL_Scancode.SDL_SCANCODE_LEFT,
            "RIGHT" => SDL.SDL_Scancode.SDL_SCANCODE_RIGHT,
            "UP" => SDL.SDL_Scancode.SDL_SCANCODE_UP,
            "DOWN" => SDL.SDL_Scancode.SDL_SCANCODE_DOWN,
            "F1" => SDL.SDL_Scancode.SDL_SCANCODE_F1,
            "F2" => SDL.SDL_Scancode.SDL_SCANCODE_F2,
            "F3" => SDL.SDL_Scancode.SDL_SCANCODE_F3,
            "F4" => SDL.SDL_Scancode.SDL_SCANCODE_F4,
            "F5" => SDL.SDL_Scancode.SDL_SCANCODE_F5,
            "F6" => SDL.SDL_Scancode.SDL_SCANCODE_F6,
            "F7" => SDL.SDL_Scancode.SDL_SCANCODE_F7,
            "F8" => SDL.SDL_Scancode.SDL_SCANCODE_F8,
            "F9" => SDL.SDL_Scancode.SDL_SCANCODE_F9,
            "F10" => SDL.SDL_Scancode.SDL_SCANCODE_F10,
            "F11" => SDL.SDL_Scancode.SDL_SCANCODE_F11,
            "F12" => SDL.SDL_Scancode.SDL_SCANCODE_F12,
            var s when s.Length == 1 && s[0] >= 'A' && s[0] <= 'Z'
                => (SDL.SDL_Scancode)(SDL.SDL_Scancode.SDL_SCANCODE_A + (s[0] - 'A')),
            var s when s.Length == 1 && s[0] >= '0' && s[0] <= '9'
                => s[0] == '0' ? SDL.SDL_Scancode.SDL_SCANCODE_0
                                : (SDL.SDL_Scancode)(SDL.SDL_Scancode.SDL_SCANCODE_1 + (s[0] - '1')),
            _ => SDL.SDL_Scancode.SDL_SCANCODE_UNKNOWN
        };
    }
}

/// <summary>
/// Static host for declarative plugins — provides shared services like toasts
/// and cross-plugin event emission. Set by PluginManager at init time.
/// </summary>
internal static class DeclarativePluginHost
{
    public static Action<string>? ShowToastHandler;
    public static Action? ReloadSceneHandler;
    public static Action<string>? EmitEventHandler;
    public static Action<string>? TogglePluginHandler;
    public static Action? ScreenshotWithNotify;
    public static Action<DeclarativePlugin, JsonElement>? SpawnParticles;

    public static void ShowToast(string message) => ShowToastHandler?.Invoke(message);
    public static void ReloadScene() => ReloadSceneHandler?.Invoke();
    public static void Emit(string eventName) => EmitEventHandler?.Invoke(eventName);
    public static void TogglePlugin(string name) => TogglePluginHandler?.Invoke(name);
}
