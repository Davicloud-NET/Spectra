using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// One table of what scripts can reach, built on the host's stack before the
// host is sealed. A step that fails is a mistake in the runtime itself, so
// it throws.
internal readonly ref struct ApiTable
{
    private readonly LuauHost _host;
    private readonly LuauStack _stack;

    public ApiTable(LuauHost host)
    {
        _host = host;
        _stack = host.Stack;
        Check(_stack.NewTable());
    }

    public ApiTable Function(ReadOnlySpan<byte> name, LuauHostFunction function)
    {
        Check(_host.PushFunction(_stack, name, function));
        Check(_stack.SetField(-2, name));
        return this;
    }

    public ApiTable Value(ReadOnlySpan<byte> name, in LuauValue value)
    {
        Check(_host.Push(_stack, in value));
        Check(_stack.SetField(-2, name));
        return this;
    }

    public ApiTable Frame(ReadOnlySpan<byte> name, CFrame value)
    {
        Check(FrameValue.Push(_stack, value));
        Check(_stack.SetField(-2, name));
        return this;
    }

    public void SetAsGlobal(ReadOnlySpan<byte> name) => Check(_stack.SetGlobal(name));

    public void SetAsVectorMetatable() => Check(_stack.SetVectorMetatable());

    public void SetAsMetatableOf(int tag) => Check(_stack.SetUserdataMetatable(tag));

    public void SetAsObjectMetatable(int tag) => Check(_host.Objects.SetMetatable(_stack, tag));

    public MethodTable KeepAsMethods()
    {
        LuauReference? kept = _host.Keep(_stack, -1);
        if (kept is null)
            throw new InvalidOperationException("The script API could not be set up: a table of methods was not kept.");

        _stack.Pop();
        return new MethodTable(kept);
    }

    private void Check(bool isDone)
    {
        if (isDone)
            return;

        string reason = _stack.GetText(-1) ?? "no reason given";
        _stack.Pop();
        throw new InvalidOperationException($"The script API could not be set up: {reason}");
    }
}
