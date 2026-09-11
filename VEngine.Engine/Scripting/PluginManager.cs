using System;
using System.Collections.Generic;
using System.IO;
using MoonSharp.Interpreter;

namespace VEngine.Engine.Scripting;

/// <summary>
/// Loads .lua plugins from Assets/plugins/ and hooks them into the engine lifecycle.
/// Each plugin is a Lua file that can define:
///   plugin.name        — display name
///   plugin.version     — version string
///   plugin.on_load()   — called once when loaded
///   plugin.on_update(dt) — called each frame
///   plugin.on_draw()   — called each frame after entities
///   plugin.on_destroy() — called on scene switch
///   plugin.on_key(key) — called on key press
///
/// Usage from Lua:
///   plugins_load()         — scan and load all plugins
///   plugins_list()         — get table of loaded plugin names
///   plugins_enabled(name, bool) — enable/disable a plugin
/// </summary>
public class PluginManager
{
    private readonly Script _lua;
    private readonly List<Plugin> _plugins = new();
    private readonly List<DeclarativePlugin> _declarativePlugins = new();

    public IReadOnlyList<Plugin> Plugins => _plugins;
    public IReadOnlyList<DeclarativePlugin> DeclarativePlugins => _declarativePlugins;

    // Shared state for declarative plugins
    private string? _currentSceneForReload;
    private Core.Entity? _activeToast;
    private float _toastTimer;

    public class Plugin
    {
        public string Name = "Unknown";
        public string Version = "1.0";
        public string FilePath = "";
        public bool Enabled = true;
        public Table? Table;
        public DynValue? OnLoad;
        public DynValue? OnUpdate;
        public DynValue? OnDraw;
        public DynValue? OnDestroy;
        public DynValue? OnKey;
    }

    public PluginManager(Script lua)
    {
        _lua = lua;

        // Wire up the static host so declarative plugins can call shared services
        DeclarativePluginHost.ShowToastHandler = ShowDeclarativeToast;
        DeclarativePluginHost.ReloadSceneHandler = ReloadCurrentScene;
        DeclarativePluginHost.EmitEventHandler = EmitEvent;
        DeclarativePluginHost.TogglePluginHandler = name =>
        {
            foreach (var p in _declarativePlugins)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    p.Enabled = !p.Enabled;
            foreach (var p in _plugins)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    p.Enabled = !p.Enabled;
        };

        DeclarativePluginHost.ScreenshotWithNotify = TakeScreenshotWithNotify;
        DeclarativePluginHost.SpawnParticles = SpawnParticlesFromAction;
    }

    /// <summary>Scan Assets/plugins/ and load all .lua and .json files as plugins.</summary>
    public void LoadAll()
    {
        var pluginsDir = Path.Combine(Core.Eng.AssetsPath, "plugins");
        if (!Directory.Exists(pluginsDir))
        {
            Console.WriteLine("[Plugins] No plugins/ directory found.");
            return;
        }

        // Lua plugins (code-based, full engine access)
        var luaFiles = Directory.GetFiles(pluginsDir, "*.lua", SearchOption.TopDirectoryOnly);
        Array.Sort(luaFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var file in luaFiles)
        {
            try { LoadPlugin(file); }
            catch (Exception ex) { Console.WriteLine($"[Plugins] Error loading {Path.GetFileName(file)}: {ex.Message}"); }
        }

        // JSON declarative plugins (no code, trigger/action based)
        var jsonFiles = Directory.GetFiles(pluginsDir, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(jsonFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var file in jsonFiles)
        {
            var plugin = DeclarativePlugin.FromFile(file);
            if (plugin != null)
            {
                _declarativePlugins.Add(plugin);
                Console.WriteLine($"[Plugins] Loaded (declarative): {plugin.Name} v{plugin.Version}");
            }
        }

        Console.WriteLine($"[Plugins] Loaded {_plugins.Count} Lua + {_declarativePlugins.Count} declarative plugin(s).");

        // Call on_load for all
        foreach (var p in _plugins)
        {
            if (!p.Enabled || p.Table == null) continue;
            var fn = p.Table.Get("on_load");
            if (fn.Type != DataType.Function) continue;
            try { _lua.Call(fn); }
            catch (Exception ex) { Console.WriteLine($"[Plugin:{p.Name}] on_load error: {ex.Message}"); }
        }
    }

    private void LoadPlugin(string filePath)
    {
        var source = File.ReadAllText(filePath);
        var fileName = Path.GetFileName(filePath);

        // Execute plugin in an isolated function environment so it can't overwrite
        // other plugins' or the game script's globals. The plugin's 'return' value
        // is captured as the plugin table.
        var wrappedSource = $"local _ENV = setmetatable({{}}, {{__index = _G}})\n{source}";
        var result = _lua.DoString(wrappedSource, codeFriendlyName: fileName);

        Table? table = null;

        // If the script returns a table, use it
        if (result.Type == DataType.Table)
        {
            table = result.Table;
        }
        else
        {
            // Check if it set a global 'plugin' table
            var global = _lua.Globals.Get("plugin");
            if (global.Type == DataType.Table)
            {
                table = global.Table;
                _lua.Globals["plugin"] = DynValue.Nil; // clear for next plugin
            }
        }

        if (table == null)
        {
            Console.WriteLine($"[Plugins] {fileName}: no plugin table returned or defined.");
            return;
        }

        var p = new Plugin
        {
            FilePath = filePath,
            Table = table,
        };

        // Read metadata
        var name = table.Get("name");
        if (name.Type == DataType.String) p.Name = name.String;
        else p.Name = Path.GetFileNameWithoutExtension(filePath);

        var version = table.Get("version");
        if (version.Type == DataType.String) p.Version = version.String;

        // Cache lifecycle hooks
        p.OnLoad = GetFunc(table, "on_load");
        p.OnUpdate = GetFunc(table, "on_update");
        p.OnDraw = GetFunc(table, "on_draw");
        p.OnDestroy = GetFunc(table, "on_destroy");
        p.OnKey = GetFunc(table, "on_key");

        _plugins.Add(p);
        Console.WriteLine($"[Plugins] Loaded: {p.Name} v{p.Version} ({fileName})");
    }

    /// <summary>Call on_update(dt) on all enabled plugins.</summary>
    public void Update(float dt)
    {
        foreach (var p in _plugins)
        {
            if (!p.Enabled || p.Table == null) continue;
            var fn = p.Table.Get("on_update");
            if (fn.Type != DataType.Function) continue;
            try { _lua.Call(fn, DynValue.NewNumber(dt)); }
            catch (Exception ex) { Console.WriteLine($"[Plugin:{p.Name}] update error: {ex.Message}"); }
        }

        // Tick declarative plugins (triggers + actions)
        foreach (var p in _declarativePlugins) p.Update(dt);

        // Drive screenshot notification state machine
        UpdateScreenshotNotify(dt);

        // Toast fade
        if (_activeToast != null)
        {
            _toastTimer -= dt;
            if (_toastTimer <= 0)
            {
                _activeToast.Destroy();
                _activeToast = null;
            }
        }
    }

    /// <summary>Called when a scene finishes loading — fires scene_loaded triggers.</summary>
    public void NotifySceneLoaded(string? sceneName)
    {
        _currentSceneForReload = sceneName;
        foreach (var p in _declarativePlugins) p.OnSceneLoaded(sceneName);
    }

    // ── Declarative Plugin Shared Services ─────────────────────

    private void ShowDeclarativeToast(string message)
    {
        // Destroy previous toast
        _activeToast?.Destroy();

        var t = new Graphics.Text(message, 0, 0);
        t.ScrollFactor = new Math.Vec2(0, 0);
        t.Layer = 99;
        t.Color = new Math.Color(180, 220, 255, 220);
        // Position at bottom-center (rough estimate — text entity width isn't known immediately)
        try
        {
            t.Position.X = Core.Eng.Width / 2f - message.Length * 3.5f;
            t.Position.Y = Core.Eng.Height - 30;
        }
        catch { /* headless */ }

        // Add to current scene
        try { Core.Eng.Game.CurrentScene?.Add(t); }
        catch { /* no scene */ }

        _activeToast = t;
        _toastTimer = 1.5f;
    }

    private void ReloadCurrentScene()
    {
        if (!string.IsNullOrEmpty(_currentSceneForReload))
            Core.Eng.SwitchScene(new LuaScene(_currentSceneForReload));
    }

    private void EmitEvent(string eventName)
    {
        foreach (var p in _declarativePlugins) p.OnEvent(eventName);
    }

    /// <summary>Call on_draw() on all enabled plugins.</summary>
    public void Draw()
    {
        foreach (var p in _plugins)
        {
            if (!p.Enabled || p.Table == null) continue;
            var fn = p.Table.Get("on_draw");
            if (fn.Type != DataType.Function) continue;
            try { _lua.Call(fn); }
            catch (Exception ex) { Console.WriteLine($"[Plugin:{p.Name}] draw error: {ex.Message}"); }
        }
    }

    /// <summary>Call on_destroy() on all enabled plugins.</summary>
    public void Destroy()
    {
        foreach (var p in _plugins)
        {
            if (p.Table == null) continue;
            var fn = p.Table.Get("on_destroy");
            if (fn.Type != DataType.Function) continue;
            try { _lua.Call(fn); }
            catch (Exception ex) { Console.WriteLine($"[Plugin:{p.Name}] destroy error: {ex.Message}"); }
        }
    }

    /// <summary>Notify plugins of a key press.</summary>
    public void OnKeyPressed(string key)
    {
        foreach (var p in _plugins)
        {
            if (!p.Enabled || p.OnKey == null) continue;
            try { _lua.Call(p.OnKey, DynValue.NewString(key)); }
            catch (Exception ex) { Console.WriteLine($"[Plugin:{p.Name}] on_key error: {ex.Message}"); }
        }
    }

    /// <summary>Enable or disable a plugin by name.</summary>
    public void SetEnabled(string name, bool enabled)
    {
        foreach (var p in _plugins)
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                p.Enabled = enabled;
                Console.WriteLine($"[Plugins] {p.Name}: {(enabled ? "enabled" : "disabled")}");
                return;
            }
        }
    }

    private static DynValue? GetFunc(Table table, string name)
    {
        var val = table.Get(name);
        return val.Type == DataType.Function ? val : null;
    }

    // ── spawn_particles action ────────────────────────────────
    //
    // Fully compositional: each call creates or reuses a plugin-owned emitter,
    // positions it at the declared attachment point, and emits N particles.
    // Authors compose their own effects via every_frame / mouse_held / event triggers.

    private Graphics.ParticleEmitter GetOrCreateSpawnerFor(DeclarativePlugin plugin, System.Text.Json.JsonElement config)
    {
        // Use the raw JSON text as a cache key so each distinct spawn_particles call
        // reuses its own emitter (avoids re-creating one per frame).
        string key = "spawner:" + config.GetRawText();
        if (plugin.EffectState.TryGetValue(key, out var existing) && existing is Graphics.ParticleEmitter em)
            return em;

        var emitter = new Graphics.ParticleEmitter(0, 0);

        // Reasonable defaults
        emitter.MinSpeedX = -10; emitter.MaxSpeedX = 10;
        emitter.MinSpeedY = -10; emitter.MaxSpeedY = 10;
        emitter.GravityY = 0;
        emitter.MinLife = 0.3f; emitter.MaxLife = 0.6f;
        emitter.MinSize = 2; emitter.MaxSize = 4;
        emitter.ColorMin = new Math.Color(255, 255, 255, 255);
        emitter.ColorMax = new Math.Color(255, 255, 255, 255);
        emitter.ScaleStart = 1f; emitter.ScaleEnd = 0f;
        emitter.Layer = 98;

        ApplyEmitterConfig(emitter, config);

        try { Core.Eng.Game.CurrentScene?.Add(emitter); }
        catch { /* no scene */ }

        plugin.EffectState[key] = emitter;
        return emitter;
    }

    private void SpawnParticlesFromAction(DeclarativePlugin plugin, System.Text.Json.JsonElement config)
    {
        if (config.ValueKind != System.Text.Json.JsonValueKind.Object) return;

        var emitter = GetOrCreateSpawnerFor(plugin, config);

        // Position from "at" attachment
        (float px, float py, bool screenSpace) = ResolveAttachment(config);
        emitter.Position.X = px;
        emitter.Position.Y = py;
        emitter.ScrollFactor = screenSpace ? new Math.Vec2(0, 0) : new Math.Vec2(1, 1);

        // Count (default 1)
        int count = 1;
        if (config.TryGetProperty("count", out var cEl)) count = cEl.GetInt32();

        emitter.Emit(count);
    }

    private static (float x, float y, bool screenSpace) ResolveAttachment(System.Text.Json.JsonElement config)
    {
        if (!config.TryGetProperty("at", out var atEl))
            return (0, 0, false);

        if (atEl.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var s = atEl.GetString() ?? "";
            // "mouse" → mouse cursor (screen-space)
            if (s.Equals("mouse", StringComparison.OrdinalIgnoreCase))
                return (Core.Eng.Mouse.X, Core.Eng.Mouse.Y, true);

            // "camera" → camera center (screen-space)
            if (s.Equals("camera", StringComparison.OrdinalIgnoreCase))
            {
                try { return (Core.Eng.Width / 2f, Core.Eng.Height / 2f, true); }
                catch { return (0, 0, true); }
            }

            // "world:x,y" → fixed world position
            if (s.StartsWith("world:", StringComparison.OrdinalIgnoreCase))
            {
                var coords = s.Substring(6).Split(',');
                if (coords.Length == 2 &&
                    float.TryParse(coords[0], out float wx) &&
                    float.TryParse(coords[1], out float wy))
                    return (wx, wy, false);
            }
        }
        else if (atEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            // [x, y] → fixed world position
            float x = 0, y = 0;
            int i = 0;
            foreach (var el in atEl.EnumerateArray())
            {
                if (i == 0) x = (float)el.GetDouble();
                else if (i == 1) y = (float)el.GetDouble();
                i++;
            }
            return (x, y, false);
        }

        return (0, 0, false);
    }

    private static void ApplyEmitterConfig(Graphics.ParticleEmitter emitter, System.Text.Json.JsonElement config)
    {
        // Single "color" shortcut (both min and max)
        if (config.TryGetProperty("color", out var colorEl) && colorEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var c = ParseColor(colorEl);
            emitter.ColorMin = c;
            emitter.ColorMax = c;
        }
        if (config.TryGetProperty("color_min", out var cminEl) && cminEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            emitter.ColorMin = ParseColor(cminEl);
        if (config.TryGetProperty("color_max", out var cmaxEl) && cmaxEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            emitter.ColorMax = ParseColor(cmaxEl);

        // "size": [min, max] or a single number
        if (config.TryGetProperty("size", out var sizeEl))
        {
            if (sizeEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                int i = 0;
                foreach (var el in sizeEl.EnumerateArray())
                {
                    int v = el.GetInt32();
                    if (i == 0) emitter.MinSize = v;
                    else if (i == 1) emitter.MaxSize = v;
                    i++;
                }
            }
            else if (sizeEl.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                int v = sizeEl.GetInt32();
                emitter.MinSize = v; emitter.MaxSize = v;
            }
        }

        // "life": [min, max] or a single number
        if (config.TryGetProperty("life", out var lifeEl))
        {
            if (lifeEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                int i = 0;
                foreach (var el in lifeEl.EnumerateArray())
                {
                    float v = (float)el.GetDouble();
                    if (i == 0) emitter.MinLife = v;
                    else if (i == 1) emitter.MaxLife = v;
                    i++;
                }
            }
            else if (lifeEl.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                float v = (float)lifeEl.GetDouble();
                emitter.MinLife = v; emitter.MaxLife = v;
            }
        }

        // Velocity ranges
        if (config.TryGetProperty("velocity_x", out var vxEl) && vxEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            int i = 0;
            foreach (var el in vxEl.EnumerateArray())
            {
                float v = (float)el.GetDouble();
                if (i == 0) emitter.MinSpeedX = v;
                else if (i == 1) emitter.MaxSpeedX = v;
                i++;
            }
        }
        if (config.TryGetProperty("velocity_y", out var vyEl) && vyEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            int i = 0;
            foreach (var el in vyEl.EnumerateArray())
            {
                float v = (float)el.GetDouble();
                if (i == 0) emitter.MinSpeedY = v;
                else if (i == 1) emitter.MaxSpeedY = v;
                i++;
            }
        }

        if (config.TryGetProperty("gravity_y", out var gEl))
            emitter.GravityY = (float)gEl.GetDouble();
        if (config.TryGetProperty("layer", out var layerEl))
            emitter.Layer = layerEl.GetInt32();
    }

    private static Math.Color ParseColor(System.Text.Json.JsonElement arr)
    {
        byte r = 0, g = 0, b = 0, a = 255;
        int i = 0;
        foreach (var el in arr.EnumerateArray())
        {
            byte v = (byte)System.Math.Clamp(el.GetInt32(), 0, 255);
            if (i == 0) r = v;
            else if (i == 1) g = v;
            else if (i == 2) b = v;
            else if (i == 3) a = v;
            i++;
        }
        return new Math.Color(r, g, b, a);
    }

    // ── Screenshot With Notification ─────────────────────────

    private enum ScreenshotState { Idle, FlashWait, Capturing, FileWait, SlideIn, Hold, SlideOut }
    private ScreenshotState _ssState;
    private int _ssWaitFrames;
    private string? _ssPath;
    private Core.Entity? _ssCard, _ssThumb, _ssLabel;
    private float _ssAnimT;
    private const int CardW = 170;
    private const int CardH = 120;

    private void TakeScreenshotWithNotify()
    {
        if (_ssState != ScreenshotState.Idle) return;
        Core.Eng.Effects.Flash(255, 255, 255, 0.08f, 160);
        _ssState = ScreenshotState.FlashWait;
        _ssWaitFrames = 10;
    }

    private void UpdateScreenshotNotify(float dt)
    {
        if (_ssState == ScreenshotState.Idle) return;

        switch (_ssState)
        {
            case ScreenshotState.FlashWait:
                if (--_ssWaitFrames <= 0)
                {
                    _ssPath = Core.Eng.CaptureScreen();
                    _ssState = ScreenshotState.FileWait;
                    _ssWaitFrames = 5;
                }
                break;

            case ScreenshotState.FileWait:
                if (--_ssWaitFrames <= 0)
                {
                    if (_ssPath != null && System.IO.File.Exists(_ssPath))
                    {
                        BuildScreenshotCard();
                        _ssState = ScreenshotState.SlideIn;
                        _ssAnimT = 0;
                    }
                    else _ssWaitFrames = 3;
                }
                break;

            case ScreenshotState.SlideIn:
                _ssAnimT += dt / 0.3f;
                if (_ssAnimT >= 1) _ssAnimT = 1;
                float t = 1 - (1 - _ssAnimT) * (1 - _ssAnimT); // ease out quad
                SetCardY(OffScreenY + (TargetY - OffScreenY) * t);
                if (_ssAnimT >= 1) { _ssState = ScreenshotState.Hold; _ssAnimT = 0; }
                break;

            case ScreenshotState.Hold:
                _ssAnimT += dt;
                SetCardY(TargetY + MathF.Sin(_ssAnimT * 4) * 1.5f);
                if (_ssAnimT >= 2f) { _ssState = ScreenshotState.SlideOut; _ssAnimT = 0; }
                break;

            case ScreenshotState.SlideOut:
                _ssAnimT += dt / 0.25f;
                if (_ssAnimT >= 1) _ssAnimT = 1;
                float te = _ssAnimT * _ssAnimT; // ease in quad
                SetCardY(TargetY + (OffScreenY - TargetY) * te);
                if (_ssAnimT >= 1) CleanupScreenshotCard();
                break;
        }
    }

    private int TargetY => Core.Eng.Height - CardH - 12;
    private int OffScreenY => Core.Eng.Height + 20;

    private void BuildScreenshotCard()
    {
        int sw = Core.Eng.Width;
        int x = sw - CardW - 12;
        int y = OffScreenY;

        // Background card
        var bg = new Graphics.Sprite(x, y);
        // Use a colored rect via a 1x1 texture isn't straightforward — use a Text as placeholder background
        // Actually just build with separate Text/Rect entities via the scene
        // Simplest: render a rect-like entity via Sprite's BaseWidth/Height and no texture (draws nothing)
        // Instead use Graphics.Text with a background-colored filled space. We'll skip the BG for simplicity.

        // Thumbnail
        if (_ssPath != null)
        {
            try
            {
                var thumb = new Graphics.Sprite(x + 6, y + 6);
                thumb.LoadGraphic(_ssPath);
                // Scale to fit
                float baseW = thumb.BaseWidth, baseH = thumb.BaseHeight;
                float s = System.Math.Min((CardW - 12) / baseW, (CardH - 30) / baseH);
                thumb.ScaleX = s; thumb.ScaleY = s;
                thumb.ScrollFactor = new Math.Vec2(0, 0);
                thumb.Layer = 96;
                Core.Eng.Game.CurrentScene?.Add(thumb);
                _ssThumb = thumb;
            }
            catch (Exception ex) { Console.WriteLine($"[Screenshot] Thumbnail load failed: {ex.Message}"); }
        }

        var label = new Graphics.Text("Saved!", x + 10, y + CardH - 16);
        label.ScrollFactor = new Math.Vec2(0, 0);
        label.Layer = 97;
        label.Color = new Math.Color(180, 200, 255, 255);
        Core.Eng.Game.CurrentScene?.Add(label);
        _ssLabel = label;

        _ssCard = null; // no BG card for now; thumb + label is enough
    }

    private void SetCardY(float y)
    {
        int sw = Core.Eng.Width;
        int x = sw - CardW - 12;
        if (_ssThumb != null) { _ssThumb.Position.X = x + 6; _ssThumb.Position.Y = y + 6; }
        if (_ssLabel != null) { _ssLabel.Position.X = x + 10; _ssLabel.Position.Y = y + CardH - 16; }
    }

    private void CleanupScreenshotCard()
    {
        _ssCard?.Destroy(); _ssCard = null;
        _ssThumb?.Destroy(); _ssThumb = null;
        _ssLabel?.Destroy(); _ssLabel = null;
        _ssState = ScreenshotState.Idle;
    }
}
