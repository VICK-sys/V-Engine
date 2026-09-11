using System;
using System.Collections.Generic;

namespace VEngine.Engine.Core;

/// <summary>
/// A dialogue tree: a sequence of text, choices, and branching logic.
/// Build with the fluent API, then pass to a DialogueBox to display.
///
/// Usage:
///   var d = new Dialogue()
///       .Say("Halt! Who goes there?", "Guard")
///       .Say("Just a traveler.", "Player")
///       .Ask("Do you have a pass?", "Guard",
///           ("Show pass", "pass"),
///           ("Run away", "flee"))
///       .Label("pass")
///       .Say("Very well, proceed.", "Guard")
///       .Goto("done")
///       .Label("flee")
///       .Say("Stop! Guards!", "Guard")
///       .Label("done");
/// </summary>
public class Dialogue
{
    internal readonly List<Node> Nodes = new();
    internal readonly Dictionary<string, int> Labels = new();

    /// <summary>
    /// Dialogue-scoped variable store. Use SetVar/IfVar to read/write, or access directly.
    /// Values are object so booleans, ints, strings, etc. all work.
    /// </summary>
    public readonly Dictionary<string, object> Vars = new();

    internal class Node
    {
        public enum Kind { Say, Ask, Label, Goto, Call, SetVar, IfGoto }
        public Kind Type;
        public string? Text, Speaker, Portrait, Target;
        public (string Text, string Label)[]? Choices;
        public Action? Callback;
        public Func<Dialogue, bool>? Predicate; // for IfGoto
        public string? VarName;                 // for SetVar
        public object? VarValue;                // for SetVar
    }

    /// <summary>Display text with optional speaker name and portrait override.</summary>
    public Dialogue Say(string text, string? speaker = null, string? portrait = null)
    {
        Nodes.Add(new Node { Type = Node.Kind.Say, Text = text, Speaker = speaker, Portrait = portrait });
        return this;
    }

    /// <summary>Display text with choice buttons. Each choice jumps to a label.</summary>
    public Dialogue Ask(string text, string? speaker, params (string Text, string Label)[] choices)
    {
        Nodes.Add(new Node { Type = Node.Kind.Ask, Text = text, Speaker = speaker, Choices = choices });
        return this;
    }

    /// <summary>Named jump target for branching.</summary>
    public Dialogue Label(string name)
    {
        Labels[name] = Nodes.Count;
        Nodes.Add(new Node { Type = Node.Kind.Label });
        return this;
    }

    /// <summary>Unconditional jump to a label.</summary>
    public Dialogue Goto(string label)
    {
        Nodes.Add(new Node { Type = Node.Kind.Goto, Target = label });
        return this;
    }

    /// <summary>Execute a callback (trigger game logic mid-dialogue).</summary>
    public Dialogue Call(Action callback)
    {
        Nodes.Add(new Node { Type = Node.Kind.Call, Callback = callback });
        return this;
    }

    /// <summary>Set a dialogue variable to a value. Subsequent IfGoto can branch on it.</summary>
    public Dialogue SetVar(string name, object value)
    {
        Nodes.Add(new Node { Type = Node.Kind.SetVar, VarName = name, VarValue = value });
        return this;
    }

    /// <summary>Jump to a label if the given predicate returns true. Otherwise fall through.</summary>
    public Dialogue IfGoto(Func<Dialogue, bool> predicate, string label)
    {
        Nodes.Add(new Node { Type = Node.Kind.IfGoto, Predicate = predicate, Target = label });
        return this;
    }

    /// <summary>Jump to a label if the named variable equals the given value.</summary>
    public Dialogue IfVar(string name, object value, string label)
    {
        return IfGoto(d =>
            d.Vars.TryGetValue(name, out var v) && Equals(v, value), label);
    }

    /// <summary>Jump to a label if the named variable is truthy (non-null, non-false, non-zero).</summary>
    public Dialogue IfFlag(string name, string label)
    {
        return IfGoto(d =>
        {
            if (!d.Vars.TryGetValue(name, out var v) || v == null) return false;
            if (v is bool b) return b;
            if (v is int i) return i != 0;
            if (v is float f) return f != 0f;
            if (v is string s) return !string.IsNullOrEmpty(s);
            return true;
        }, label);
    }

    /// <summary>Read a variable with a default fallback.</summary>
    public T GetVar<T>(string name, T defaultValue = default!)
    {
        if (Vars.TryGetValue(name, out var v) && v is T tv) return tv;
        return defaultValue;
    }

    /// <summary>Total number of nodes.</summary>
    public int NodeCount => Nodes.Count;
}
