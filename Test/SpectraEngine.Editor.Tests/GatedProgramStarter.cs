using SpectraEngine.Editor.Shell.Scripts;

namespace SpectraEngine.Editor.Tests;

// Starts nothing, and holds every start at a gate until the test opens it,
// the way a program that is slow to come up does.
internal sealed class GatedProgramStarter : IProgramStarter, IDisposable
{
    private readonly ManualResetEventSlim _gate = new();
    private readonly TaskCompletionSource _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _asked;

    public int Asked => Volatile.Read(ref _asked);

    // Done once a start stands at the gate.
    public Task Waiting => _waiting.Task;

    public void Open() => _gate.Set();

    public bool TryStart(ProgramStart start, out string failure)
    {
        Interlocked.Increment(ref _asked);
        _waiting.TrySetResult();

        // A test that never opens the gate fails here and does not hang.
        bool opened = _gate.Wait(TimeSpan.FromSeconds(30));
        failure = opened ? string.Empty : "The gate was never opened.";
        return opened;
    }

    public void Dispose() => _gate.Dispose();
}
