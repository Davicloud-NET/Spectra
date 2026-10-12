namespace SpectraEngine.Scripting.Luau.Runtime;

// What every signal of a runtime shares: the host that keeps a listener's
// function, and the threads that wait.
internal sealed class SignalHub(LuauHost host, WaitingThreads waiting)
{
    public LuauHost Host { get; } = host;

    public WaitingThreads Waiting { get; } = waiting;
}
