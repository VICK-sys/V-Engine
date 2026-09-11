using System;
using System.Collections.Generic;
using SDL2;
using VEngine.Engine.Graphics.GL;

namespace VEngine.Engine.Core;

/// <summary>
/// Quake-style dropdown debug console. Toggle with ` (backtick) by default.
/// Register commands with RegisterCommand. Shows recent log output and a command prompt.
///
/// Usage:
///   Eng.Console.RegisterCommand("spawn", args => Spawn(args[0]));
///   Eng.Console.Log("Hello from game code");
///   Eng.Console.Toggle();
/// </summary>
public class DebugConsole
{
    private bool _enabled;
    private string _input = "";
    private int _cursorPos;
    private readonly List<string> _lines = new();
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private float _cursorBlink;
    private const int MaxLines = 30;
    private const int MaxInputLength = 256;

    // Commands
    public delegate void CommandHandler(string[] args);
    private readonly Dictionary<string, CommandHandler> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _commandHelp = new(StringComparer.OrdinalIgnoreCase);

    // Font
    private IntPtr _font;
    private readonly Dictionary<string, (GLTexture tex, int w, int h)> _lineCache = new();

    /// <summary>True when the console is open and receiving input.</summary>
    public bool Enabled => _enabled;

    public DebugConsole()
    {
        RegisterBuiltins();
    }

    /// <summary>Toggle the console open/closed.</summary>
    public void Toggle()
    {
        _enabled = !_enabled;
        if (_enabled)
        {
            SDL.SDL_StartTextInput();
        }
        else
        {
            SDL.SDL_StopTextInput();
            _input = "";
            _cursorPos = 0;
            _historyIndex = -1;
        }
    }

    /// <summary>Register a command callable via the console.</summary>
    public void RegisterCommand(string name, CommandHandler handler, string help = "")
    {
        _commands[name] = handler;
        if (!string.IsNullOrEmpty(help))
            _commandHelp[name] = help;
    }

    /// <summary>Unregister a command.</summary>
    public void UnregisterCommand(string name)
    {
        _commands.Remove(name);
        _commandHelp.Remove(name);
    }

    /// <summary>Append a line to the console log.</summary>
    public void Log(string message)
    {
        _lines.Add(message);
        if (_lines.Count > MaxLines)
            _lines.RemoveAt(0);
    }

    /// <summary>Execute a command string (as if typed into the prompt).</summary>
    public void Execute(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        Log("> " + line);
        _history.Add(line);
        if (_history.Count > 64) _history.RemoveAt(0);

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        var name = parts[0];
        var args = new string[parts.Length - 1];
        Array.Copy(parts, 1, args, 0, args.Length);

        if (_commands.TryGetValue(name, out var handler))
        {
            try { handler(args); }
            catch (Exception ex) { Log("Error: " + ex.Message); }
        }
        else
        {
            Log($"Unknown command: {name}. Type 'help' for a list.");
        }
    }

    internal void HandleTextInput(char c)
    {
        if (!_enabled) return;
        // Backtick closes the console — don't insert
        if (c == '`' || c == '~') return;
        if (_input.Length >= MaxInputLength) return;

        _input = _input.Insert(_cursorPos, c.ToString());
        _cursorPos++;
    }

    internal void HandleKeyPress(SDL.SDL_Scancode key)
    {
        if (!_enabled) return;

        switch (key)
        {
            case SDL.SDL_Scancode.SDL_SCANCODE_RETURN:
            case SDL.SDL_Scancode.SDL_SCANCODE_KP_ENTER:
                if (_input.Length > 0)
                {
                    Execute(_input);
                    _input = "";
                    _cursorPos = 0;
                    _historyIndex = -1;
                }
                break;
            case SDL.SDL_Scancode.SDL_SCANCODE_BACKSPACE:
                if (_cursorPos > 0)
                {
                    _input = _input.Remove(_cursorPos - 1, 1);
                    _cursorPos--;
                }
                break;
            case SDL.SDL_Scancode.SDL_SCANCODE_DELETE:
                if (_cursorPos < _input.Length)
                    _input = _input.Remove(_cursorPos, 1);
                break;
            case SDL.SDL_Scancode.SDL_SCANCODE_LEFT:
                if (_cursorPos > 0) _cursorPos--;
                break;
            case SDL.SDL_Scancode.SDL_SCANCODE_RIGHT:
                if (_cursorPos < _input.Length) _cursorPos++;
                break;
            case SDL.SDL_Scancode.SDL_SCANCODE_HOME:
                _cursorPos = 0;
                break;
            case SDL.SDL_Scancode.SDL_SCANCODE_END:
                _cursorPos = _input.Length;
                break;
            case SDL.SDL_Scancode.SDL_SCANCODE_UP:
                // History back
                if (_history.Count > 0)
                {
                    if (_historyIndex == -1) _historyIndex = _history.Count - 1;
                    else if (_historyIndex > 0) _historyIndex--;
                    _input = _history[_historyIndex];
                    _cursorPos = _input.Length;
                }
                break;
            case SDL.SDL_Scancode.SDL_SCANCODE_DOWN:
                // History forward
                if (_historyIndex >= 0 && _historyIndex < _history.Count - 1)
                {
                    _historyIndex++;
                    _input = _history[_historyIndex];
                    _cursorPos = _input.Length;
                }
                else
                {
                    _historyIndex = -1;
                    _input = "";
                    _cursorPos = 0;
                }
                break;
            case SDL.SDL_Scancode.SDL_SCANCODE_TAB:
                TryAutocomplete();
                break;
            case SDL.SDL_Scancode.SDL_SCANCODE_ESCAPE:
                Toggle();
                break;
        }
    }

    private void TryAutocomplete()
    {
        if (string.IsNullOrEmpty(_input)) return;
        var matches = new List<string>();
        foreach (var cmd in _commands.Keys)
            if (cmd.StartsWith(_input, StringComparison.OrdinalIgnoreCase))
                matches.Add(cmd);
        if (matches.Count == 1)
        {
            _input = matches[0];
            _cursorPos = _input.Length;
        }
        else if (matches.Count > 1)
        {
            Log("Matches: " + string.Join(", ", matches));
        }
    }

    internal void Update(float realDt)
    {
        if (_enabled)
            _cursorBlink += realDt;
    }

    internal void Draw()
    {
        if (!_enabled) return;
        EnsureFont();
        if (_font == IntPtr.Zero) return;

        int consoleHeight = Eng.Height / 2;
        // Background
        Eng.GL.FillRect(0, 0, Eng.Width, consoleHeight, 0, 0, 0, 0.85f);
        // Border
        Eng.GL.FillRect(0, consoleHeight - 2, Eng.Width, 2, 0.4f, 0.5f, 0.7f, 1f);

        int lineHeight = 16;
        int y = consoleHeight - 30; // input row

        // Render input line: "> <text>_"
        string promptLine = "> " + _input;
        if ((int)(_cursorBlink * 2) % 2 == 0)
            promptLine = promptLine.Insert(_cursorPos + 2, "_");
        RenderText(promptLine, 6, y);

        // Render log lines, newest at bottom just above the prompt
        y -= lineHeight + 4;
        for (int i = _lines.Count - 1; i >= 0 && y > 0; i--)
        {
            RenderText(_lines[i], 6, y);
            y -= lineHeight;
        }
    }

    private void RenderText(string text, int x, int y)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Cache textures per unique line (bounded — oldest entries get evicted via ClearCache)
        if (!_lineCache.TryGetValue(text, out var cached))
        {
            var color = new Math.Color(220, 220, 230, 255);
            var surface = SDL_ttf.TTF_RenderUTF8_Blended(_font, text, color);
            if (surface == IntPtr.Zero) return;
            var tex = GLTexture.FromSurface(Eng.GL.Api, surface);
            cached = (tex, tex.Width, tex.Height);
            _lineCache[text] = cached;

            // Evict oldest if cache grows too large
            if (_lineCache.Count > 200)
            {
                var oldest = "";
                foreach (var k in _lineCache.Keys) { oldest = k; break; }
                _lineCache[oldest].tex.Dispose();
                _lineCache.Remove(oldest);
            }
        }

        Eng.GL.DrawTexture(cached.tex, 0, 0, cached.w, cached.h,
            x, y, cached.w, cached.h, 1, 1, 1, 1);
    }

    private void EnsureFont()
    {
        if (_font != IntPtr.Zero) return;
        try { _font = Graphics.FontCache.GetDefault(13); }
        catch (Exception ex) { Console.WriteLine($"[Console] Font load failed: {ex.Message}"); }
    }

    private void RegisterBuiltins()
    {
        RegisterCommand("help", args =>
        {
            Log("Commands:");
            foreach (var (name, help) in _commandHelp)
                Log($"  {name} - {help}");
            foreach (var name in _commands.Keys)
                if (!_commandHelp.ContainsKey(name)) Log($"  {name}");
        }, "list all commands");

        RegisterCommand("clear", args => _lines.Clear(), "clear the console log");

        RegisterCommand("quit", args => Eng.Quit(), "exit the game");

        RegisterCommand("fps", args => Log($"FPS: {1f / System.Math.Max(Eng.Time.Elapsed, 0.0001f):F0}"), "show current FPS");

        RegisterCommand("scene", args =>
        {
            if (args.Length == 0) Log("Usage: scene <lua_path>");
            else Eng.SwitchScene(new Scripting.LuaScene(args[0]));
        }, "switch to a Lua scene");

        RegisterCommand("echo", args => Log(string.Join(" ", args)), "print arguments to console");
    }

    internal void Shutdown()
    {
        foreach (var kv in _lineCache) kv.Value.tex.Dispose();
        _lineCache.Clear();
    }
}
