using System;
using MoonSharp.Interpreter;
using VEngine.Engine.Core;

namespace VEngine.Engine.Scripting;

/// <summary>
/// A Behavior that delegates to Lua callbacks. Attach to any entity to
/// give it per-entity scripted logic.
///
/// Usage from Lua:
///   local enemy = sprite("slime.png", 100, 200)
///   add_behavior(enemy, {
///       on_attach = function(self)
///           self.speed = 40
///       end,
///       update = function(self, dt)
///           set_x(self.owner, get_x(self.owner) + self.speed * dt)
///       end,
///       on_destroy = function(self)
///           log("enemy destroyed!")
///       end
///   })
/// </summary>
public class LuaBehavior : Behavior
{
    private readonly Script _lua;
    private readonly Table _table;
    private readonly DynValue? _onAttachFn;
    private readonly DynValue? _updateFn;
    private readonly DynValue? _onDestroyFn;

    public LuaBehavior(Script lua, Table table)
    {
        _lua = lua;
        _table = table;
        _onAttachFn = GetCallback("on_attach");
        _updateFn = GetCallback("update");
        _onDestroyFn = GetCallback("on_destroy");
    }

    public override void OnAttach()
    {
        // Expose the owner entity on the Lua table
        _table["owner"] = Owner;

        if (_onAttachFn != null)
            SafeCall(_onAttachFn, DynValue.FromObject(_lua, _table));
    }

    public override void Update(float dt)
    {
        if (_updateFn != null)
            SafeCall(_updateFn, DynValue.FromObject(_lua, _table), DynValue.NewNumber(dt));
    }

    public override void OnDestroy()
    {
        if (_onDestroyFn != null)
            SafeCall(_onDestroyFn, DynValue.FromObject(_lua, _table));
    }

    private DynValue? GetCallback(string name)
    {
        var val = _table.Get(name);
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
            Console.WriteLine($"[Lua Behavior Error] {ex.DecoratedMessage}");
        }
    }

    /// <summary>
    /// Register the add_behavior Lua binding. Called from LuaBindings.
    /// </summary>
    internal static void RegisterBinding(Script script)
    {
        script.Globals["add_behavior"] = (Action<Entity, Table>)((entity, table) =>
        {
            entity.AddBehavior(new LuaBehavior(script, table));
        });
    }
}
