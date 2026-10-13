using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The signals the level's queue fires: the outputs of entities. There is one
// for each node and output, so a script reads the same object every time. A
// listener is kept twice: in the level's own table, which a fire reads, and
// here under the number the queue hands back when the listener's turn comes.
internal sealed class NodeSignals
{
    private readonly SignalHub _hub;
    private readonly EntityWorld _world;
    private readonly Dictionary<Port, ScriptSignal> _signals = [];
    private readonly Dictionary<long, ScriptConnection> _listeners = [];
    private long _lastListener;

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

    // A fire has come due for one listener. One that was disconnected since
    // the fire is not called, which is Roblox's rule for a deferred signal.
    public void Hear(long listener, string parameter)
    {
        if (_listeners.TryGetValue(listener, out ScriptConnection? connection) && connection.IsConnected)
            WireValues.Hand(connection, parameter, _world.Scene);
    }

    private readonly record struct Port(Guid Node, string Name);
}
