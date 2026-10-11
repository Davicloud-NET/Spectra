using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

/// <summary>
/// A Luau value the host keeps alive past the call that produced it, such as
/// a table. Dispose it to let the value go.
/// </summary>
public sealed class LuauReference : IDisposable
{
    private readonly LuauHost _host;
    private int _reference;

    internal LuauReference(LuauHost host, int reference)
    {
        _host = host;
        _reference = reference;
    }

    /// <summary>
    /// Pushes the value, or nil once it was let go. False: it could not be pushed, and an
    /// error value is on top instead.
    /// </summary>
    public bool Push(LuauStack stack) => !_host.IsClosed && Shim.sl_getref(stack.State, _reference) == LuaStatus.Ok;

    /// <inheritdoc/>
    public void Dispose()
    {
        int reference = _reference;
        _reference = Lua.NilReference;
        if (reference != Lua.NilReference && !_host.IsClosed)
            Lua.lua_unref(_host.Stack.State, reference);
    }
}
