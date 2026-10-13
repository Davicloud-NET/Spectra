using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>Something that happens in the level, which a script connects a function to or waits for.</summary>
// The host fires it with the values every listener gets: where it stands,
// as RunService does, or from the level's queue, as an entity's output does.
[ScriptClass(LuauDefinitions.SignalType, TypeParameters = "T... = ...any")]
internal sealed partial class ScriptSignal : ScriptObject
{
    // With fewer listeners than this none is looked over.
    private const int FirstLookOver = 64;

    private readonly List<ScriptConnection> _connections = [];
    private readonly SignalHub _hub;
    private readonly QueuedSignal? _queued;
    private int _fireDepth;
    private bool _hasDisconnected;
    private int _lookOverAt = FirstLookOver;

    // With `queued` the level's queue calls each listener, and Fire is not used.
    public ScriptSignal(string name, SignalHub hub, QueuedSignal? queued = null)
    {
        Name = name;
        _hub = hub;
        _queued = queued;
    }

    // "Signal Heartbeat", as Roblox prints one.
    public override string Name { get; }

    public int ListenerCount => _connections.Count;

    // One listener, for a host that queues a call of each in place of
    // calling them where it stands.
    public ScriptConnection ListenerAt(int index) => _connections[index];

    // What a queued signal's fire carries, as a wire's text. Not said for a
    // signal the host fires where it stands.
    public ParameterType Sends => _queued?.Sends ?? ParameterType.NotSaid;

    // The node and the output behind a signal the level's queue fires. Null
    // for one the host fires where it stands.
    public QueuedSignal? Queued => _queued;

    public ScriptConnection Connect(LuauFunction function, bool isOnce, in ScriptPlace by = default)
    {
        ScriptConnection connection = new(this, function, isOnce) { By = by };
        Add(connection);
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

    // Ends every connection, for a signal of a node that was destroyed. A
    // thread that waits for the signal is ended: nothing would resume it.
    public void EndAll()
    {
        _fireDepth++;
        for (int i = 0; i < _connections.Count; i++)
        {
            if (_connections[i].IsConnected)
                _connections[i].End();
        }

        _fireDepth--;
        DropDisconnected();
    }

    public void OnDisconnected(ScriptConnection connection)
    {
        _queued?.Forget(connection);
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
        if ((_queued?.WhyNoListener() ?? _queued?.WhyNoWait()) is { } refused)
            return stack.Fail(refused);

        LuauThread thread = _hub.Host.ThreadOf(stack);
        if (!thread.IsAlive)
            return stack.Fail("this thread cannot wait for a signal");

        // Before the wait is kept: the next fire would resume a thread that
        // never yielded wherever it waits by then.
        if (!stack.CanYield)
            return stack.Fail(WaitingThreads.CannotYield);

        WaitingThreads waiting = _hub.Waiting;
        ScriptPlace by = _queued?.PlaceOf(stack) ?? default;
        Add(new ScriptConnection(this, waiting, waiting.Add(WaitingThread.ForSignal(thread))) { By = by });
        SayWhatWasMissed(stack);
        return LuauStack.Yield();
    }

    private void Add(ScriptConnection connection)
    {
        // A queued signal's listeners are looked over with the level's own.
        if (_queued is null && _connections.Count >= _lookOverAt)
            LetTheDeadGo();

        _connections.Add(connection);
        _queued?.Listen(connection);
    }

    // A listener whose thread was cancelled, or whose script was switched
    // off, is let go when the signal fires next. On a signal that never
    // fires again it would stay, so all are looked over each time there are
    // twice as many as were left the last time.
    private void LetTheDeadGo()
    {
        _fireDepth++;
        for (int i = 0; i < _connections.Count; i++)
        {
            if (!_connections[i].CanRun)
                _connections[i].Disconnect();
        }

        _fireDepth--;
        DropDisconnected();
        _lookOverAt = Math.Max(FirstLookOver, _connections.Count * 2);
    }

    private int Listen(LuauStack stack, string method, bool isOnce)
    {
        // Before the function is kept: a signal nothing can listen to.
        if (_queued?.WhyNoListener() is { } refused)
            return stack.Fail(refused);
        if (_hub.Host.KeepFunction(stack, 2) is not { } function)
            return ScriptArguments.Wrong(stack, 2, method, "function", isMethod: true);

        ScriptConnection connection = Connect(function, isOnce, _queued?.PlaceOf(stack) ?? default);
        SayWhatWasMissed(stack);
        return ScriptResult.Of(_hub.Host.Objects.Push(stack, ScriptTags.Connection, connection));
    }

    // An output that fired while the level spawned was heard by no script.
    // The first one that listens to it is told, at the line that does.
    private void SayWhatWasMissed(LuauStack stack)
    {
        if (_queued?.TakeMissedFire() is not { } missed)
            return;

        LuauHost host = _hub.Host;
        host.Report(ScriptReport.Line(stack.State, host.ScriptOf(stack), ScriptSeverity.Warning, missed));
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
