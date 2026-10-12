using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>Something that happens in the level, which a script connects a function to or waits for.</summary>
// The host fires it with the values every listener gets.
[ScriptClass(LuauDefinitions.SignalType, TypeParameters = "T... = ...any")]
internal sealed partial class ScriptSignal : ScriptObject
{
    private readonly List<ScriptConnection> _connections = [];
    private readonly SignalHub _hub;
    private int _fireDepth;
    private bool _hasDisconnected;

    public ScriptSignal(string name, SignalHub hub)
    {
        Name = name;
        _hub = hub;
    }

    // "Signal Heartbeat", as Roblox prints one.
    public override string Name { get; }

    public int ListenerCount => _connections.Count;

    public ScriptConnection Connect(LuauFunction function, bool isOnce)
    {
        ScriptConnection connection = new(this, function, isOnce);
        _connections.Add(connection);
        return connection;
    }

    // Runs the listeners in the order they connected. One that connects
    // during the fire waits for the next one. One that disconnects during it
    // is not run.
    public void Fire(ReadOnlySpan<LuauValue> arguments)
    {
        int count = _connections.Count;
        if (count == 0)
            return;

        _fireDepth++;
        try
        {
            for (int i = 0; i < count; i++)
            {
                if (_connections[i].IsConnected)
                    _connections[i].Run(arguments);
            }
        }
        finally
        {
            _fireDepth--;
            DropDisconnected();
        }
    }

    public void OnDisconnected()
    {
        _hasDisconnected = true;
        DropDisconnected();
    }

    /// <summary>Calls the function every time the signal fires, with what it was fired with.</summary>
    [ScriptMethod(Parameters = "callback: (T...) -> ()", Returns = "RBXScriptConnection")]
    private int Connect(LuauStack stack) => Listen(stack, "Connect", isOnce: false);

    /// <summary>Calls the function the next time the signal fires, and not again.</summary>
    [ScriptMethod(Parameters = "callback: (T...) -> ()", Returns = "RBXScriptConnection")]
    private int Once(LuauStack stack) => Listen(stack, "Once", isOnce: true);

    /// <summary>Waits until the signal fires next, and returns what it was fired with.</summary>
    // The thread yields here. The next fire resumes it with the values the
    // signal was fired with, which Wait then returns.
    [ScriptMethod(Returns = "T...")]
    private int Wait(LuauStack stack)
    {
        LuauThread thread = _hub.Host.ThreadOf(stack);
        if (!thread.IsAlive)
            return stack.Fail("this thread cannot wait for a signal");

        // Before the wait is kept: the next fire would resume a thread that
        // never yielded wherever it waits by then.
        if (!stack.CanYield)
            return stack.Fail(WaitingThreads.CannotYield);

        WaitingThreads waiting = _hub.Waiting;
        _connections.Add(new ScriptConnection(this, waiting, waiting.Add(WaitingThread.ForSignal(thread))));
        return LuauStack.Yield();
    }

    private int Listen(LuauStack stack, string method, bool isOnce)
    {
        if (_hub.Host.KeepFunction(stack, 2) is not { } function)
            return ScriptArguments.Wrong(stack, 2, method, "function", isMethod: true);

        ScriptConnection connection = Connect(function, isOnce);
        return ScriptResult.Of(_hub.Host.Objects.Push(stack, ScriptTags.Object, connection));
    }

    // Not while a fire walks the list by index.
    private void DropDisconnected()
    {
        if (_fireDepth > 0 || !_hasDisconnected)
            return;

        _hasDisconnected = false;
        _connections.RemoveAll(static connection => !connection.IsConnected);
    }
}
