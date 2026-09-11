using System;
using System.IO;
using MoonSharp.Interpreter;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics;
using VEngine.Engine.UI;

namespace VEngine.Engine.Scripting;

/// <summary>
/// A Scene driven by a Lua script. Loads a .lua file and calls its
/// create(), update(dt), and draw() functions at the appropriate times.
///
/// Usage:
///   var game = new Game("My Game", 800, 480);
///   game.Start(new LuaScene("scripts/game.lua"));
///
/// Lua scripts have access to the full engine API via LuaBindings.
/// </summary>
public class LuaScene : Scene
{
    private readonly string _scriptPath;
    private Script _lua = null!;
    private DynValue? _createFn;
    private DynValue? _updateFn;
    private DynValue? _drawFn;
    private DynValue? _drawBgFn;
    private bool _createFailed;
    private FileSystemWatcher? _watcher;
    private volatile bool _reloadRequested;
    private PluginManager? _plugins;

    /// <summary>
    /// Create a LuaScene that loads and runs the specified .lua file.
    /// Path is resolved relative to Assets/ folder.
    /// </summary>
    public LuaScene(string luaPath)
    {
        _scriptPath = luaPath;
    }

    public override void Create()
    {
        _lua = new Script(CoreModules.Preset_SoftSandbox | CoreModules.LoadMethods);

        // Set up require() to load Lua modules from the Assets folder
        var loader = new AssetScriptLoader();
        loader.ModulePaths = new[] { "?", "?.lua" };
        _lua.Options.ScriptLoader = loader;

        // Register CLR types that Lua will interact with
        UserData.RegisterType<Entity>();
        UserData.RegisterType<KinematicEntity>();
        UserData.RegisterType<Sprite>();
        UserData.RegisterType<Text>();
        UserData.RegisterType<TimerHandle>();
        UserData.RegisterType<TweenHandle>();
        UserData.RegisterType<Group>();
        UserData.RegisterType<ParticleEmitter>();
        UserData.RegisterType<Tilemap>();
        UserData.RegisterType<Dialogue>();
        UserData.RegisterType<DialogueBox>();
        UserData.RegisterType<Sequence>();
        UserData.RegisterType<PortalEffect>();
        UserData.RegisterType<Physics.Fluid.FluidSystem>();
        UserData.RegisterType<Physics.Fluid.GridFluidSystem>();
        UserData.RegisterType<GifSprite>();
        UserData.RegisterType<VideoSprite>();
        UserData.RegisterType<Rain>();
        UserData.RegisterType<AfterImage>();
        UserData.RegisterType<Physics.WaterBody>();

        // Register all engine API bindings
        LuaBindings.Register(_lua, this);

        // Load and execute the script
        string fullPath;
        if (Path.IsPathRooted(_scriptPath))
            fullPath = _scriptPath;
        else
            fullPath = Eng.Asset(_scriptPath);

        var source = File.ReadAllText(fullPath);
        _lua.DoString(source, codeFriendlyName: Path.GetFileName(_scriptPath));

        // Cache the lifecycle functions
        _createFn = GetFunction("create");
        _updateFn = GetFunction("update");
        _drawFn = GetFunction("draw");
        _drawBgFn = GetFunction("draw_bg");

        // Call create()
        if (_createFn != null)
        {
            try { _lua.Call(_createFn); }
            catch (ScriptRuntimeException ex) { Console.WriteLine($"[Lua Error] {ex.DecoratedMessage}"); _createFailed = true; }
            catch (SyntaxErrorException ex) { Console.WriteLine($"[Lua Syntax Error] {ex.DecoratedMessage}"); _createFailed = true; }
        }

        // Load plugins from Assets/plugins/
        _plugins = new PluginManager(_lua);
        RegisterPluginBindings(_lua, _plugins);
        _plugins.LoadAll();
        _plugins.NotifySceneLoaded(_scriptPath);

        // Hot-reload: watch the scripts directory for changes
        StartFileWatcher();
    }

    public override void Update(float dt)
    {
        // Hot-reload: F5 triggers manual reload, file watcher triggers auto-reload
        if (_reloadRequested || Eng.Input.IsPressed(SDL2.SDL.SDL_Scancode.SDL_SCANCODE_F5))
        {
            _reloadRequested = false;
            Console.WriteLine("[Lua] Hot-reloading...");
            Eng.SwitchScene(new LuaScene(_scriptPath));
            return;
        }

        if (_createFailed) return;

        // Update all entities (physics, behaviors, etc.)
        base.Update(dt);

        // Call Lua update(dt)
        if (_updateFn != null)
            SafeCall(_updateFn, DynValue.NewNumber(dt));

        // Update plugins
        _plugins?.Update(dt);
    }

    public override void Draw()
    {
        if (_createFailed) return;
        // Call Lua draw_bg() for background drawing (before entities)
        if (_drawBgFn != null)
            SafeCall(_drawBgFn);

        // Draw all entities
        base.Draw();

        // Call Lua draw() for overlay drawing (HUD, debug viz — after entities)
        if (_drawFn != null)
            SafeCall(_drawFn);

        // Draw plugins
        _plugins?.Draw();
    }

    /// <summary>
    /// Add an entity to this scene. Called by LuaBindings when creating entities from Lua.
    /// </summary>
    internal void AddEntity(Entity entity)
    {
        Add(entity);
    }

    /// <summary>
    /// Remove and destroy an entity from this scene.
    /// </summary>
    internal void RemoveEntity(Entity entity)
    {
        Remove(entity);
    }

    public override void Destroy()
    {
        _plugins?.Destroy();
        StopFileWatcher();
        base.Destroy();
    }

    private void StartFileWatcher()
    {
        try
        {
            // Watch the Assets/scripts directory (or wherever the script lives)
            string fullPath;
            if (Path.IsPathRooted(_scriptPath))
                fullPath = _scriptPath;
            else
                fullPath = Eng.Asset(_scriptPath);

            var dir = Path.GetDirectoryName(fullPath);
            if (dir == null || !Directory.Exists(dir)) return;

            _watcher = new FileSystemWatcher(dir, "*.lua")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false,
            };
            _watcher.Changed += OnScriptFileChanged;
            _watcher.Created += OnScriptFileChanged;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lua] File watcher failed: {ex.Message}");
        }
    }

    private void StopFileWatcher()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private static void RegisterPluginBindings(Script lua, PluginManager plugins)
    {
        lua.Globals["plugins_list"] = (Func<Table>)(() =>
        {
            var t = new Table(lua);
            int idx = 1;
            foreach (var p in plugins.Plugins)
            {
                var pt = new Table(lua);
                pt["name"] = DynValue.NewString(p.Name);
                pt["version"] = DynValue.NewString(p.Version);
                pt["enabled"] = DynValue.NewBoolean(p.Enabled);
                pt["file"] = DynValue.NewString(System.IO.Path.GetFileName(p.FilePath));
                t[idx++] = DynValue.NewTable(pt);
            }
            return t;
        });

        lua.Globals["plugins_enable"] = (Action<string, bool>)((name, enabled) =>
            plugins.SetEnabled(name, enabled));

        lua.Globals["plugins_count"] = (Func<int>)(() => plugins.Plugins.Count);
    }

    private void OnScriptFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: just set the flag, the main thread checks it in Update
        _reloadRequested = true;
    }

    private DynValue? GetFunction(string name)
    {
        var val = _lua.Globals.Get(name);
        return val.Type == DataType.Function ? val : null;
    }

    private void SafeCall(DynValue fn, params DynValue[] args)
    {
        try
        {
            _lua.Call(fn, args);
        }
        catch (ScriptRuntimeException ex)
        {
            Console.WriteLine($"[Lua Error] {ex.DecoratedMessage}");
        }
        catch (SyntaxErrorException ex)
        {
            Console.WriteLine($"[Lua Syntax Error] {ex.DecoratedMessage}");
        }
        catch (Exception ex)
        {
            // Catch C# exceptions from bindings (NullRef, InvalidCast, etc.)
            Console.WriteLine($"[Lua Binding Error] {ex.GetType().Name}: {ex.Message}");
        }
    }
}

/// <summary>
/// Custom MoonSharp script loader that resolves require("scripts.player")
/// to Assets/scripts/player.lua.
/// </summary>
internal class AssetScriptLoader : MoonSharp.Interpreter.Loaders.ScriptLoaderBase
{
    public override bool ScriptFileExists(string name)
    {
        var path = ResolvePath(name);
        return File.Exists(path);
    }

    public override object LoadFile(string file, Table globalContext)
    {
        var path = ResolvePath(file);
        return File.ReadAllText(path);
    }

    public override string ResolveModuleName(string modname, Table globalContext)
    {
        // MoonSharp calls this to resolve require("scripts.player") -> a key for LoadFile
        return modname;
    }

    private static string ResolvePath(string moduleName)
    {
        // Convert dot notation to path: "scripts.player" -> "scripts/player.lua"
        var relative = moduleName.Replace('.', '/');
        if (!relative.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            relative += ".lua";
        return Path.Combine(Eng.AssetsPath, relative);
    }
}
