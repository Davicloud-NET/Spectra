using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

/// <summary>
/// A function of a script that the host keeps, to call it later: what a
/// script hands to a signal or to a timer. Dispose it to let the function go.
/// A script that is switched off lets go of all of its own.
/// </summary>
public sealed class LuauFunction : IDisposable
{
    private readonly LuauHost _host;
    private int _reference;

    internal LuauFunction(LuauHost host, int reference, LuauScript? script)
    {
        _host = host;
        _reference = reference;
        Script = script;
        script?.Track(this);
    }

    /// <summary>The script whose code handed the function over. Null when none did.</summary>
    public LuauScript? Script { get; }

    /// <summary>
    /// Calls the function on a thread of its own, so it may yield. The thread shares
    /// the globals of the function's script.
    /// </summary>
    public LuauResume Start(ReadOnlySpan<LuauValue> arguments, out LuauThread thread)
    {
        thread = default;
        if (_host.IsClosed || _reference == Lua.NilReference)
            return LuauResume.Refused;

        LuauStack stage = _host.Stack;
        if (!Push(stage))
        {
            stage.Pop();
            return LuauResume.Refused;
        }

        if (!_host.PushAll(stage, arguments))
        {
            stage.Pop();
            return LuauResume.Refused;
        }

        return _host.Threads.Start(stage.State, arguments.Length + 1, Script, out thread);
    }

    /// <summary>The same, for a caller that has no use for the thread.</summary>
    public LuauResume Start(ReadOnlySpan<LuauValue> arguments = default) => Start(arguments, out _);

    /// <summary>
    /// Pushes the function, or nil once it was let go. False: it could not be pushed, and an
    /// error value is on top instead.
    /// </summary>
    public bool Push(LuauStack stack) => !_host.IsClosed && Shim.sl_getref(stack.State, _reference) == LuaStatus.Ok;

    /// <inheritdoc/>
    public void Dispose()
    {
        int reference = _reference;
        if (reference == Lua.NilReference)
            return;

        _reference = Lua.NilReference;
        Script?.Forget(this);
        if (!_host.IsClosed)
            Lua.lua_unref(_host.Stack.State, reference);
    }
}
