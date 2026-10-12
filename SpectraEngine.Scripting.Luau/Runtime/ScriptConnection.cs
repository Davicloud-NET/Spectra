using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>The link between a signal and a function a script connected to it.</summary>
// One listener of a signal: a function a script connected, or a thread
// that waits for the signal to fire once.
[ScriptClass("RBXScriptConnection")]
internal sealed partial class ScriptConnection : ScriptObject
{
    private readonly ScriptSignal _signal;
    private readonly WaitingThreads? _waiting;
    private readonly long _wait;
    private readonly bool _isOnce;
    private LuauFunction? _function;

    public ScriptConnection(ScriptSignal signal, LuauFunction function, bool isOnce)
    {
        _signal = signal;
        _function = function;
        _isOnce = isOnce;
    }

    // The thread's wait is kept with every other wait, under this token.
    public ScriptConnection(ScriptSignal signal, WaitingThreads waiting, long wait)
    {
        _signal = signal;
        _waiting = waiting;
        _wait = wait;
        _isOnce = true;
    }

    public override string Name => "Connection";

    /// <summary>Whether the function is still called when the signal fires.</summary>
    [ScriptProperty(Name = "Connected")]
    public bool IsConnected { get; private set; } = true;

    // Calls the function on a thread of its own, or lets the waiting thread
    // go on. Either gets the values the signal was fired with.
    public void Run(ReadOnlySpan<LuauValue> arguments)
    {
        // First, so a listener that fires the signal again is not run twice.
        if (_isOnce)
            IsConnected = false;

        LuauResume outcome = _function is { } function ? function.Start(arguments) : ResumeWaiter(arguments);

        // Refused: its script was switched off, or the thread was cancelled.
        if (outcome == LuauResume.Refused || !IsConnected)
            Disconnect();
    }

    /// <summary>Ends the connection. The function is not called again.</summary>
    [ScriptMethod]
    public void Disconnect()
    {
        IsConnected = false;

        // The function may be running now. Its own call keeps it alive.
        _function?.Dispose();
        _function = null;
        _signal.OnDisconnected();
    }

    // A thread that was cancelled or went on to another wait has left this
    // one, and its token names nothing.
    private LuauResume ResumeWaiter(ReadOnlySpan<LuauValue> arguments) =>
        _waiting is { } waiting && waiting.TryTake(_wait, out WaitingThread waiter)
            ? waiter.Thread.Resume(arguments)
            : LuauResume.Refused;
}
