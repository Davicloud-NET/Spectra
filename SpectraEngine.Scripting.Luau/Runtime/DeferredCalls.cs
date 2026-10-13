using SpectraEngine.Core.Entities;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The calls of listeners that wait for their turn in the level's queue. A
// signal fired here calls nobody where it stands: each listener it has at
// that moment gets a wake of its own, in the one order of wires, thinks and
// waiting threads. The queue hands wakes back in the order they were made,
// so the calls are kept in a line and the first is the one that is due.
internal sealed class DeferredCalls
{
    // The low half of a waiting thread's token is its slot, which is never
    // this large. So the bit tells a call's token from a thread's.
    private const long Mark = 1L << 31;

    private readonly EntityWorld _world;
    private readonly Queue<Call> _calls = new();
    private uint _lastSerial;

    public DeferredCalls(EntityWorld world) => _world = world;

    // Queues a call of every listener with no value.
    public void Fire(ScriptSignal signal) => Fire(signal, hasValue: false, default);

    // Queues a call of every listener with the one value.
    public void Fire(ScriptSignal signal, in LuauValue value) => Fire(signal, hasValue: true, in value);

    // A wake the queue hands back. False when it is no call's.
    public bool TryRun(long token)
    {
        if ((token & Mark) == 0)
            return false;

        // One that was disconnected since the fire is not called, which is
        // Roblox's rule for a deferred signal.
        if (TryTake(token, out Call call) && call.Listener.IsConnected)
        {
            if (call.HasValue)
                call.Listener.Run([call.Value]);
            else
                call.Listener.Run([]);
        }

        return true;
    }

    // A wake the queue dropped, because one tick made more than it sends.
    // The listener does not hear that fire. False when it is no call's.
    public bool TryDrop(long token)
    {
        if ((token & Mark) == 0)
            return false;

        TryTake(token, out _);
        return true;
    }

    public void Clear() => _calls.Clear();

    private void Fire(ScriptSignal signal, bool hasValue, in LuauValue value)
    {
        // Queueing runs no script, so the listeners hold still.
        int count = signal.ListenerCount;
        if (count == 0 || !_world.IsActive)
            return;

        for (int i = 0; i < count; i++)
        {
            ScriptConnection listener = signal.ListenerAt(i);
            if (!listener.IsConnected)
                continue;

            // With no room in the queue the listener does not hear this fire.
            long token = ((long)++_lastSerial << 32) | Mark;
            if (_world.QueueScriptWake(token, 0))
                _calls.Enqueue(new Call(token, listener, hasValue, value));
        }
    }

    // The call the token names. One in front of it never came back from the
    // queue and is let go. A token older than the first names none.
    private bool TryTake(long token, out Call call)
    {
        while (_calls.TryPeek(out call))
        {
            int ahead = (int)((uint)(token >> 32) - (uint)(call.Token >> 32));
            if (ahead < 0)
                break;

            _calls.Dequeue();
            if (ahead == 0)
                return true;
        }

        call = default;
        return false;
    }

    private readonly struct Call(long token, ScriptConnection listener, bool hasValue, LuauValue value)
    {
        public long Token { get; } = token;

        public ScriptConnection Listener { get; } = listener;

        public bool HasValue { get; } = hasValue;

        public LuauValue Value { get; } = value;
    }
}
