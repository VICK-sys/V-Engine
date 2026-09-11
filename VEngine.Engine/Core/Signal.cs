using System;
using System.Collections.Generic;

namespace VEngine.Engine.Core;

/// <summary>
/// Lightweight publish-subscribe signal with no arguments.
/// Fully safe to add, remove, or clear listeners during Emit().
/// Uses a cached snapshot array — zero allocation after warmup.
/// </summary>
public class Signal
{
    private readonly List<Action> _listeners = new();
    private Action[]? _snapshot;
    private bool _snapshotDirty = true;

    public Action Subscribe(Action listener)
    {
        _listeners.Add(listener);
        _snapshotDirty = true;
        return () => { _listeners.Remove(listener); _snapshotDirty = true; };
    }

    public void Unsubscribe(Action listener) { _listeners.Remove(listener); _snapshotDirty = true; }
    public void Clear() { _listeners.Clear(); _snapshotDirty = true; }
    public int Count => _listeners.Count;

    public void Emit()
    {
        if (_listeners.Count == 0) return;
        if (_snapshotDirty) { _snapshot = _listeners.ToArray(); _snapshotDirty = false; }
        var snap = _snapshot!;
        int count = snap.Length;
        for (int i = 0; i < count; i++)
            snap[i]();
    }
}

/// <summary>Signal with one argument. Zero-allocation emit after warmup.</summary>
public class Signal<T>
{
    private readonly List<Action<T>> _listeners = new();
    private Action<T>[]? _snapshot;
    private bool _snapshotDirty = true;

    public Action Subscribe(Action<T> listener)
    {
        _listeners.Add(listener);
        _snapshotDirty = true;
        return () => { _listeners.Remove(listener); _snapshotDirty = true; };
    }

    public void Unsubscribe(Action<T> listener) { _listeners.Remove(listener); _snapshotDirty = true; }
    public void Clear() { _listeners.Clear(); _snapshotDirty = true; }
    public int Count => _listeners.Count;

    public void Emit(T value)
    {
        if (_listeners.Count == 0) return;
        if (_snapshotDirty) { _snapshot = _listeners.ToArray(); _snapshotDirty = false; }
        var snap = _snapshot!;
        int count = snap.Length;
        for (int i = 0; i < count; i++)
            snap[i](value);
    }
}

/// <summary>Signal with two arguments. Zero-allocation emit after warmup.</summary>
public class Signal<T1, T2>
{
    private readonly List<Action<T1, T2>> _listeners = new();
    private Action<T1, T2>[]? _snapshot;
    private bool _snapshotDirty = true;

    public Action Subscribe(Action<T1, T2> listener)
    {
        _listeners.Add(listener);
        _snapshotDirty = true;
        return () => { _listeners.Remove(listener); _snapshotDirty = true; };
    }

    public void Unsubscribe(Action<T1, T2> listener) { _listeners.Remove(listener); _snapshotDirty = true; }
    public void Clear() { _listeners.Clear(); _snapshotDirty = true; }
    public int Count => _listeners.Count;

    public void Emit(T1 value1, T2 value2)
    {
        if (_listeners.Count == 0) return;
        if (_snapshotDirty) { _snapshot = _listeners.ToArray(); _snapshotDirty = false; }
        var snap = _snapshot!;
        int count = snap.Length;
        for (int i = 0; i < count; i++)
            snap[i](value1, value2);
    }
}
