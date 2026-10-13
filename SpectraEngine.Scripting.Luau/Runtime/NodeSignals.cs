using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The signals the level's queue fires: the outputs of entities, and the
// touch of a part. There is one for each node and name, so a script reads
// the same object every time. A listener is kept twice: in the level's own
// table, which a fire reads, and here under the number the queue hands back
// when the listener's turn comes.
internal sealed class NodeSignals
{
    // With fewer listeners than this none is looked over.
    private const int FirstLookOver = 64;

    private readonly SignalHub _hub;
    private readonly EntityWorld _world;
    private readonly Dictionary<Port, ScriptSignal> _signals = [];
    private readonly Dictionary<long, ScriptConnection> _listeners = [];
    private readonly List<ScriptConnection> _dead = [];
    private long _lastListener;
    private int _lookOverAt = FirstLookOver;

    public NodeSignals(SignalHub hub, EntityWorld world)
    {
        _hub = hub;
        _world = world;
    }

    public int ListenerCount => _listeners.Count;

    public ScriptSignal OfOutput(SceneNode node, EntityPort output)
    {
        var port = new Port(node.Id, output.Name);
        if (_signals.TryGetValue(port, out ScriptSignal? signal))
            return signal;

        signal = new ScriptSignal($"Signal {output.Name}", _hub, new QueuedSignal(this, node, output));
        _signals.Add(port, signal);
        return signal;
    }

    public void Listen(SceneNode node, string name, ScriptConnection connection)
    {
        if (_listeners.Count >= _lookOverAt)
            LetTheDeadGo();

        connection.Listener = ++_lastListener;
        _listeners.Add(connection.Listener, connection);
        _world.Listeners.Add(node, name, connection.Listener);
    }

    // Also for a connection that has ended already: it ends once when it is
    // told to and once more when its function returns.
    public void Forget(SceneNode node, string name, ScriptConnection connection)
    {
        if (!_listeners.Remove(connection.Listener))
            return;

        _world.Listeners.Remove(node, name, connection.Listener);
        connection.Listener = 0;
    }

    // Whether the output fired while the level spawned. True once.
    public bool TakeMissedFire(SceneNode node, string name) => _world.Listeners.TakeMissedFire(node, name);

    // A fire has come due for one listener. One that was disconnected since
    // the fire is not called, which is Roblox's rule for a deferred signal.
    public void Hear(long listener, string parameter)
    {
        if (!_listeners.TryGetValue(listener, out ScriptConnection? connection) || !connection.IsConnected)
            return;

        // An entity's own output of the name of a touch is no touch.
        if (connection.Source is { IsTouch: true })
            return;

        // The class fired what its schema does not say. No script's mistake,
        // so the warning names no line.
        if (!WireValues.Hand(connection, parameter, _world.Scene) && connection.Source?.TakeUnreadFire(parameter) is { } unread)
            _hub.Host.Report(new ScriptMessage(ScriptSeverity.Warning, unread, string.Empty, 0));
    }

    // A listener whose script was switched off, or whose thread left its
    // wait, is let go when its output fires next. On an output that never
    // fires again it would stay for good, so all are looked over each time
    // there are twice as many as were left the last time.
    private void LetTheDeadGo()
    {
        foreach (ScriptConnection connection in _listeners.Values)
        {
            if (!connection.CanRun)
                _dead.Add(connection);
        }

        // Disconnect comes back to Forget, which takes it out of the table.
        foreach (ScriptConnection connection in _dead)
            connection.Disconnect();

        _dead.Clear();
        _lookOverAt = Math.Max(FirstLookOver, _listeners.Count * 2);
    }

    // The Touched or the TouchEnded of a part or a mesh: a name of
    // TouchSignal. The level's touch pass fires it, and finds its listeners
    // where it finds those of an output.
    public ScriptSignal OfTouch(SceneNode node, string name)
    {
        var port = new Port(node.Id, name);
        if (_signals.TryGetValue(port, out ScriptSignal? signal))
            return signal;

        var touch = new QueuedSignal(this, node, new EntityPort(name, ParameterType.None), isTouch: true);
        signal = new ScriptSignal($"Signal {name}", _hub, touch);
        _signals.Add(port, signal);
        return signal;
    }

    // A touch that started or ended has come due for one listener, which is
    // handed who touches. Null hands it nothing.
    public void HearTouch(long listener, object? toucher)
    {
        if (!_listeners.TryGetValue(listener, out ScriptConnection? connection) || !connection.IsConnected)
            return;

        if (toucher is null)
            connection.Run([]);
        else
            connection.Run([LuauValue.Object(ScriptTags.Object, toucher)]);
    }

    private readonly record struct Port(Guid Node, string Name);
}
