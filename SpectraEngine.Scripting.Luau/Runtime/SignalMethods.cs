namespace SpectraEngine.Scripting.Luau.Runtime;

// Connect, Once and Wait of a signal and Disconnect of a connection: one set
// of functions that every signal of a runtime shares.
internal sealed class SignalMethods
{
    private readonly LuauHost _host;
    private readonly WaitingThreads _waiting;

    public SignalMethods(LuauHost host, WaitingThreads waiting)
    {
        _host = host;
        _waiting = waiting;

        OfSignal = new ApiTable(host)
            .Function("Connect"u8, stack => Connect(stack, "Connect", isOnce: false))
            .Function("Once"u8, stack => Connect(stack, "Once", isOnce: true))
            .Function("Wait"u8, Wait)
            .KeepAsMethods();

        OfConnection = new ApiTable(host)
            .Function("Disconnect"u8, Disconnect)
            .KeepAsMethods();
    }

    public MethodTable OfSignal { get; }

    public MethodTable OfConnection { get; }

    private int Connect(LuauStack stack, string method, bool isOnce)
    {
        if (!TryGetSelf(stack, out ScriptSignal? signal))
            return ScriptArguments.NoSelf(stack, method);
        if (_host.KeepFunction(stack, 2) is not { } function)
            return ScriptArguments.Wrong(stack, 2, method, "function", isMethod: true);

        ScriptConnection connection = signal.Connect(function, isOnce);
        return ScriptResult.Of(_host.Objects.Push(stack, ScriptTags.Object, connection));
    }

    // The thread yields here. The next fire resumes it with the values the
    // signal was fired with, which Wait then returns.
    private int Wait(LuauStack stack)
    {
        if (!TryGetSelf(stack, out ScriptSignal? signal))
            return ScriptArguments.NoSelf(stack, "Wait");

        LuauThread thread = _host.ThreadOf(stack);
        if (!thread.IsAlive)
            return stack.Fail("this thread cannot wait for a signal");

        // Before the wait is kept: the next fire would resume a thread that
        // never yielded wherever it waits by then.
        if (!stack.CanYield)
            return stack.Fail(WaitingThreads.CannotYield);

        signal.Await(_waiting, thread);
        return LuauStack.Yield();
    }

    private int Disconnect(LuauStack stack)
    {
        if (!TryGetSelf(stack, out ScriptConnection? connection))
            return ScriptArguments.NoSelf(stack, "Disconnect");

        connection.Disconnect();
        return 0;
    }

    private bool TryGetSelf<T>(LuauStack stack, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? self)
        where T : ScriptObject
    {
        self = _host.Objects.TryGet(stack, 1, ScriptTags.Object, out object? target) == LuauObjectLookup.Found
            ? target as T
            : null;
        return self is not null;
    }
}
