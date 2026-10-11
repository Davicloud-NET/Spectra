namespace SpectraEngine.Scripting.Luau.Runtime;

// Something a script connects a function to or waits for, as it does with
// Heartbeat. The host fires it with the values every listener gets.
internal sealed class ScriptSignal : ScriptObject
{
    private readonly List<ScriptConnection> _connections = [];
    private int _fireDepth;
    private bool _hasDisconnected;

    public ScriptSignal(string name, SignalMethods methods)
    {
        Name = name;
        SharedMethods = methods;
    }

    public override string ClassName => "RBXScriptSignal";

    // "Signal Heartbeat", as Roblox prints one.
    public override string Name { get; }

    public SignalMethods SharedMethods { get; }

    public override MethodTable Methods => SharedMethods.OfSignal;

    public int ListenerCount => _connections.Count;

    public ScriptConnection Connect(LuauFunction function, bool isOnce)
    {
        ScriptConnection connection = new(this, function, isOnce);
        _connections.Add(connection);
        return connection;
    }

    // The thread goes on at the next fire, with what the signal was fired
    // with, unless it has left the wait by then.
    public void Await(WaitingThreads waiting, LuauThread thread) =>
        _connections.Add(new ScriptConnection(this, waiting, waiting.Add(WaitingThread.ForSignal(thread))));

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

    // Not while a fire walks the list by index.
    private void DropDisconnected()
    {
        if (_fireDepth > 0 || !_hasDisconnected)
            return;

        _hasDisconnected = false;
        _connections.RemoveAll(static connection => !connection.IsConnected);
    }
}
