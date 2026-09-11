using System;
using System.Collections.Generic;
using SDL2;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics;
using VEngine.Engine.Graphics.GL;
using VEngine.Engine.Input;
using VEngine.Engine.Math;

namespace VEngine.Engine.UI;

/// <summary>
/// Displays dialogue with typewriter reveal, portraits, and branching choices.
/// Add to a scene, call Start() with a Dialogue tree, and it handles the rest.
///
/// Usage:
///   var box = new DialogueBox();
///   box.SetPortrait("Guard", "portraits/guard.png");
///   box.Start(dialogue);
///   scene.Add(box);
/// </summary>
public class DialogueBox : Entity
{
    // ── Visual Configuration ──────────────────────────────────

    /// <summary>Characters revealed per second.</summary>
    public float TypingSpeed = 30f;

    /// <summary>Font size for dialogue text.</summary>
    public int FontSize = 14;

    /// <summary>Font path (relative to Assets). Default: engine's bundled font.</summary>
    public string FontPath = "ui/default-font.ttf";

    /// <summary>Dialogue text color.</summary>
    public Color TextColor = Color.White;

    /// <summary>Speaker name color.</summary>
    public Color SpeakerColor = new(255, 220, 100);

    /// <summary>Box background color.</summary>
    public Color BoxColor = new(20, 20, 30, 220);

    /// <summary>Box border color. Set alpha to 0 to hide.</summary>
    public Color BorderColor = new(80, 80, 100, 200);

    /// <summary>Unselected choice color.</summary>
    public Color ChoiceColor = Color.White;

    /// <summary>Selected/hovered choice color.</summary>
    public Color ChoiceSelectedColor = new(255, 220, 100);

    /// <summary>Margin from screen edges.</summary>
    public float BoxMargin = 10;

    /// <summary>Box height in pixels.</summary>
    public float BoxHeight = 80;

    /// <summary>Padding inside the box.</summary>
    public float BoxPadding = 12;

    /// <summary>Portrait display size in pixels.</summary>
    public float PortraitSize = 64;

    // ── Portraits ─────────────────────────────────────────────

    private readonly Dictionary<string, GLTexture> _portraits = new();

    /// <summary>Register a portrait image for a speaker name.</summary>
    public void SetPortrait(string speaker, string path)
    {
        if (!System.IO.Path.IsPathRooted(path))
            path = Eng.Asset(path);
        _portraits[speaker] = Eng.GL.GetOrCreateTexture(path);
    }

    // ── State ─────────────────────────────────────────────────

    private Dialogue? _dialogue;
    private int _nodeIndex;
    private bool _active;
    private float _revealTimer;
    private int _revealedChars;
    private int _totalChars;
    private readonly List<string> _lines = new();
    private string? _currentSpeaker;
    private string? _portraitKey;
    private (string Text, string Label)[]? _choices;
    private int _selectedChoice;
    private float _inputCooldown;

    /// <summary>Whether dialogue is currently active.</summary>
    public bool IsActive => _active;

    /// <summary>Fires when the dialogue reaches the end (no more nodes).</summary>
    public Action? OnComplete;

    // ── Constructor ───────────────────────────────────────────

    public DialogueBox() : base(0, 0)
    {
        ScrollFactor = new Vec2(0, 0);
        Layer = 10;
    }

    // ── Public API ────────────────────────────────────────────

    /// <summary>Start displaying a dialogue tree.</summary>
    public void Start(Dialogue dialogue)
    {
        _dialogue = dialogue;
        _nodeIndex = 0;
        _active = true;
        Visible = true;
        AdvanceToNextVisual();
    }

    /// <summary>Stop the dialogue immediately.</summary>
    public void Stop()
    {
        _active = false;
        Visible = false;
        _choices = null;
    }

    // ── Lifecycle ─────────────────────────────────────────────

    public override void Update(float dt)
    {
        if (!_active || _dialogue == null) return;

        // Typewriter advance
        if (_revealedChars < _totalChars)
        {
            _revealTimer += dt * TypingSpeed;
            _revealedChars = System.Math.Min(_totalChars, (int)_revealTimer);
        }

        // Input with cooldown — IsDown so held key repeats, IsPressed for instant response
        _inputCooldown -= dt;
        bool keyHeld = Eng.Input.IsDown(SDL.SDL_Scancode.SDL_SCANCODE_SPACE)
                    || Eng.Input.IsDown(SDL.SDL_Scancode.SDL_SCANCODE_RETURN)
                    || (Eng.Gamepad.Connected && Eng.Gamepad.IsDown(
                           SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A));
        bool keyJustPressed = Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_SPACE)
                           || Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_RETURN)
                           || (Eng.Gamepad.Connected && Eng.Gamepad.IsPressed(
                                  SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A));
        bool advance = keyJustPressed || (keyHeld && _inputCooldown <= 0);
        if (advance) _inputCooldown = 0.12f;

        if (_choices != null && _revealedChars >= _totalChars)
        {
            // Choice navigation
            bool up = Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_UP)
                   || Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_W)
                   || (Eng.Gamepad.Connected && Eng.Gamepad.IsPressed(
                          SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP));
            bool down = Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_DOWN)
                     || Eng.Input.IsPressed(SDL.SDL_Scancode.SDL_SCANCODE_S)
                     || (Eng.Gamepad.Connected && Eng.Gamepad.IsPressed(
                            SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN));

            if (up && _selectedChoice > 0) _selectedChoice--;
            if (down && _selectedChoice < _choices.Length - 1) _selectedChoice++;

            // Mouse hover + click
            var atlas = GetAtlas();
            float choiceY = ComputeChoiceY(atlas);
            for (int i = 0; i < _choices.Length; i++)
            {
                if (Eng.Mouse.Y >= choiceY && Eng.Mouse.Y < choiceY + atlas.LineHeight)
                {
                    _selectedChoice = i;
                    if (Eng.Mouse.IsPressed(MouseButton.Left)) advance = true;
                }
                choiceY += atlas.LineHeight;
            }

            if (advance) SelectChoice();
        }
        else if (advance)
        {
            if (_revealedChars < _totalChars)
                _revealedChars = _totalChars; // skip typewriter
            else
            {
                _nodeIndex++;
                AdvanceToNextVisual();
            }
        }
    }

    public override void Draw()
    {
        if (!_active) return;

        var atlas = GetAtlas();

        // Compute box height dynamically based on content
        float contentH = BoxPadding * 2;
        if (_currentSpeaker != null) contentH += atlas.LineHeight + 2;
        contentH += _lines.Count * atlas.LineHeight;
        if (_choices != null && _revealedChars >= _totalChars)
            contentH += 4 + _choices.Length * atlas.LineHeight;
        float actualHeight = System.Math.Max(BoxHeight, contentH);

        float boxX = BoxMargin;
        float boxY = Eng.Height - actualHeight - BoxMargin;
        float boxW = Eng.Width - BoxMargin * 2;

        // Background panel
        Eng.GL.FillRect(boxX, boxY, boxW, actualHeight,
            BoxColor.R / 255f, BoxColor.G / 255f, BoxColor.B / 255f, BoxColor.A / 255f);
        if (BorderColor.A > 0)
            Eng.GL.DrawRect(boxX, boxY, boxW, actualHeight,
                BorderColor.R / 255f, BorderColor.G / 255f, BorderColor.B / 255f, BorderColor.A / 255f);

        float textX = boxX + BoxPadding;
        float textY = boxY + BoxPadding;

        // Portrait
        if (_portraitKey != null && _portraits.TryGetValue(_portraitKey, out var portrait))
        {
            float pY = boxY + (actualHeight - PortraitSize) / 2f;
            Eng.GL.DrawTexture(portrait, 0, 0, portrait.Width, portrait.Height,
                textX, pY, PortraitSize, PortraitSize, 1, 1, 1, 1);
            textX += PortraitSize + BoxPadding;
        }

        // Speaker name
        if (_currentSpeaker != null)
        {
            atlas.DrawText(_currentSpeaker, textX, textY,
                SpeakerColor.R / 255f, SpeakerColor.G / 255f, SpeakerColor.B / 255f, SpeakerColor.A / 255f);
            textY += atlas.LineHeight + 2;
        }

        // Text with typewriter reveal
        int remaining = _revealedChars;
        float lineY = textY;
        foreach (var line in _lines)
        {
            if (remaining <= 0) break;
            int chars = System.Math.Min(line.Length, remaining);
            atlas.DrawText(line[..chars], textX, lineY,
                TextColor.R / 255f, TextColor.G / 255f, TextColor.B / 255f, TextColor.A / 255f);
            remaining -= chars;
            lineY += atlas.LineHeight;
        }

        // Choices (after text fully revealed)
        if (_choices != null && _revealedChars >= _totalChars)
        {
            lineY += 4;
            for (int i = 0; i < _choices.Length; i++)
            {
                bool selected = i == _selectedChoice;
                string prefix = selected ? "> " : "  ";
                var color = selected ? ChoiceSelectedColor : ChoiceColor;
                atlas.DrawText(prefix + _choices[i].Text, textX, lineY,
                    color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
                lineY += atlas.LineHeight;
            }
        }
    }

    // ── Internal ──────────────────────────────────────────────

    private void AdvanceToNextVisual()
    {
        while (_active && _nodeIndex < _dialogue!.Nodes.Count)
        {
            var node = _dialogue.Nodes[_nodeIndex];
            switch (node.Type)
            {
                case Dialogue.Node.Kind.Label:
                    _nodeIndex++;
                    continue;
                case Dialogue.Node.Kind.Goto:
                    if (node.Target != null && _dialogue.Labels.TryGetValue(node.Target, out int idx))
                        _nodeIndex = idx;
                    else
                        _nodeIndex++;
                    continue;
                case Dialogue.Node.Kind.Call:
                    node.Callback?.Invoke();
                    _nodeIndex++;
                    continue;
                case Dialogue.Node.Kind.SetVar:
                    if (node.VarName != null)
                        _dialogue.Vars[node.VarName] = node.VarValue!;
                    _nodeIndex++;
                    continue;
                case Dialogue.Node.Kind.IfGoto:
                    if (node.Predicate != null && node.Predicate(_dialogue) &&
                        node.Target != null && _dialogue.Labels.TryGetValue(node.Target, out int gotoIdx))
                        _nodeIndex = gotoIdx;
                    else
                        _nodeIndex++;
                    continue;
                case Dialogue.Node.Kind.Say:
                case Dialogue.Node.Kind.Ask:
                    ShowNode(node);
                    return;
            }
        }

        _active = false;
        Visible = false;
        OnComplete?.Invoke();
    }

    private void ShowNode(Dialogue.Node node)
    {
        _currentSpeaker = node.Speaker;
        _portraitKey = node.Portrait ?? node.Speaker;
        _choices = node.Type == Dialogue.Node.Kind.Ask ? node.Choices : null;
        _selectedChoice = 0;
        _revealTimer = 0;
        _revealedChars = 0;

        var atlas = GetAtlas();
        float textAreaWidth = Eng.Width - BoxMargin * 2 - BoxPadding * 2;
        if (_portraitKey != null && _portraits.ContainsKey(_portraitKey))
            textAreaWidth -= PortraitSize + BoxPadding;

        WrapText(node.Text ?? "", atlas, textAreaWidth);
    }

    private void SelectChoice()
    {
        if (_choices == null || _dialogue == null) return;
        var target = _choices[_selectedChoice].Label;
        if (_dialogue.Labels.TryGetValue(target, out int idx))
            _nodeIndex = idx;
        else
            _nodeIndex++;
        _choices = null;
        AdvanceToNextVisual();
    }

    private void WrapText(string text, GlyphAtlas atlas, float maxWidth)
    {
        _lines.Clear();
        _totalChars = 0;
        if (string.IsNullOrEmpty(text)) return;

        var words = text.Split(' ');
        string currentLine = "";
        foreach (var word in words)
        {
            string test = currentLine.Length > 0 ? currentLine + " " + word : word;
            if (atlas.MeasureWidth(test) > maxWidth && currentLine.Length > 0)
            {
                _lines.Add(currentLine);
                _totalChars += currentLine.Length;
                currentLine = word;
            }
            else
            {
                currentLine = test;
            }
        }
        if (currentLine.Length > 0)
        {
            _lines.Add(currentLine);
            _totalChars += currentLine.Length;
        }
    }

    private GlyphAtlas GetAtlas() => GlyphAtlas.Get(Eng.Asset(FontPath), FontSize);

    private float ComputeChoiceY(GlyphAtlas atlas)
    {
        float contentH = BoxPadding * 2;
        if (_currentSpeaker != null) contentH += atlas.LineHeight + 2;
        contentH += _lines.Count * atlas.LineHeight;
        if (_choices != null) contentH += 4 + _choices.Length * atlas.LineHeight;
        float actualHeight = System.Math.Max(BoxHeight, contentH);
        float boxY = Eng.Height - actualHeight - BoxMargin;
        float textY = boxY + BoxPadding;
        // Portrait shifts textX not textY — no adjustment needed here
        if (_currentSpeaker != null) textY += atlas.LineHeight + 2;
        return textY + _lines.Count * atlas.LineHeight + 4;
    }
}
