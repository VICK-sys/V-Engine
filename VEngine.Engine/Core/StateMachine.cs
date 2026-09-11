using System;
using System.Collections.Generic;

namespace VEngine.Engine.Core;

/// <summary>
/// Generic state machine with Enter/Update/Exit callbacks per state.
/// Use an enum or string as the state key.
/// </summary>
public class StateMachine<T> where T : notnull
{
    private readonly Dictionary<T, State> _states = new();
    private State? _current;
    private T _currentKey;

    /// <summary>The current state key.</summary>
    public T Current => _currentKey;

    /// <summary>Time spent in the current state (seconds). Resets on transition.</summary>
    public float Duration { get; private set; }

    /// <summary>Create a state machine with an initial state.</summary>
    public StateMachine(T initial)
    {
        _currentKey = initial;
    }

    /// <summary>
    /// Register callbacks for a state. Returns a builder for fluent chaining.
    /// Call .Enter(), .Update(), .Exit() in any order.
    /// </summary>
    public StateBuilder On(T state)
    {
        if (!_states.TryGetValue(state, out var s))
        {
            s = new State();
            _states[state] = s;
        }
        return new StateBuilder(s);
    }

    /// <summary>
    /// Transition to a new state. Calls Exit on the old state and Enter on the new one.
    /// No-op if already in the target state.
    /// </summary>
    public void Set(T state)
    {
        if (EqualityComparer<T>.Default.Equals(_currentKey, state)) return;
        try { _current?.OnExit?.Invoke(); } catch (Exception ex) { Console.WriteLine($"[StateMachine] Exit error: {ex.Message}"); }
        _currentKey = state;
        _current = _states.GetValueOrDefault(state);
        Duration = 0;
        try { _current?.OnEnter?.Invoke(); } catch (Exception ex) { Console.WriteLine($"[StateMachine] Enter error: {ex.Message}"); }
    }

    /// <summary>
    /// Update the current state. On first call, enters the initial state.
    /// </summary>
    public void Update(float dt)
    {
        if (_current == null && _states.TryGetValue(_currentKey, out var initial))
        {
            _current = initial;
            try { _current.OnEnter?.Invoke(); } catch (Exception ex) { Console.WriteLine($"[StateMachine] Enter error: {ex.Message}"); }
        }
        try { _current?.OnUpdate?.Invoke(dt); } catch (Exception ex) { Console.WriteLine($"[StateMachine] Update error: {ex.Message}"); }
        Duration += dt;
    }

    /// <summary>Fluent builder for registering state callbacks.</summary>
    public class StateBuilder
    {
        private readonly State _state;
        internal StateBuilder(State state) => _state = state;

        /// <summary>Called once when entering this state.</summary>
        public StateBuilder Enter(Action callback) { _state.OnEnter = callback; return this; }

        /// <summary>Called each frame while in this state.</summary>
        public StateBuilder Update(Action<float> callback) { _state.OnUpdate = callback; return this; }

        /// <summary>Called once when leaving this state.</summary>
        public StateBuilder Exit(Action callback) { _state.OnExit = callback; return this; }
    }

    internal class State
    {
        public Action? OnEnter;
        public Action<float>? OnUpdate;
        public Action? OnExit;
    }
}
