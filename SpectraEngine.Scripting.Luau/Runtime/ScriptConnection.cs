namespace SpectraEngine.Scripting.Luau.Runtime;

// One listener of a signal: a function a script connected, or a thread that
// waits for the signal to fire once.
internal sealed class ScriptConnection : ScriptObject
{
    private readonly ScriptSignal _signal;
    private readonly LuauThread _waiter;
    private readonly bool _isOnce;
    private LuauFunction? _function;

    public ScriptConnection(ScriptSignal signal, LuauFunction function, bool isOnce)
    {
        _signal = signal;
        _function = function;
        _isOnce = isOnce;
    }

    public ScriptConnection(ScriptSignal signal, LuauThread waiter)
    {
        _signal = signal;
        _waiter = waiter;
        _isOnce = true;
    }

    public override string ClassName => "RBXScriptConnection";

    public override string Name => "Connection";

    public override MethodTable Methods => _signal.SharedMethods.OfConnection;

    public bool IsConnected { get; private set; } = true;

    public override int ReadProperty(LuauStack stack, ReadOnlySpan<byte> name) =>
        name.SequenceEqual("Connected"u8) ? ScriptResult.Of(stack.Push(IsConnected)) : NoSuchProperty;

    // Calls the function on a thread of its own, or lets the waiting thread
    // go on. Either gets the values the signal was fired with.
    public void Run(ReadOnlySpan<LuauValue> arguments)
    {
        // First, so a listener that fires the signal again is not run twice.
        if (_isOnce)
            IsConnected = false;

        LuauResume outcome = _function is { } function ? function.Start(arguments) : _waiter.Resume(arguments);

        // Refused: its script was switched off, or the thread was cancelled.
        if (outcome == LuauResume.Refused || !IsConnected)
            Disconnect();
    }

    public void Disconnect()
    {
        IsConnected = false;

        // The function may be running now. Its own call keeps it alive.
        _function?.Dispose();
        _function = null;
        _signal.OnDisconnected();
    }
}
