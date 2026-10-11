using SpectraEngine.Editor.Shell.Scripts;

namespace SpectraEngine.Editor.Tests;

// Starts nothing, and holds every start at a gate until the test opens it,
// the way a program that is slow to come up does.
internal sealed class GatedProgramStarter : IProgramStarter, IDisposable
{
    private readonly ManualResetEventSlim _gate = new();
    private readonly Semaphore _turn = new(0, int.MaxValue);
    private readonly List<(int Count, TaskCompletionSource Reached)> _watchers = [];
    private int _asked;

    public int Asked => Volatile.Read(ref _asked);

    // Done once a start stands at the gate.
    public Task Waiting => Reached(1);

    // Done once so many starts were asked for.
    public Task Reached(int count)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_watchers)
        {
            if (_asked >= count)
                reached.TrySetResult();
            else
                _watchers.Add((count, reached));
        }

        return reached.Task;
    }

    // Lets every start through, the waiting ones and the ones to come.
    public void Open() => _gate.Set();

    // Lets one waiting start through. Two that answer at once would meet in
    // a shell that expects one thread.
    public void OpenOne() => _turn.Release();

    public bool TryStart(ProgramStart start, out string failure)
    {
        lock (_watchers)
        {
            _asked++;
            foreach ((int count, TaskCompletionSource reached) in _watchers)
            {
                if (_asked >= count)
                    reached.TrySetResult();
            }
        }

        // A test that never opens the gate fails here and does not hang.
        bool opened = WaitHandle.WaitAny([_gate.WaitHandle, _turn], TimeSpan.FromSeconds(30)) != WaitHandle.WaitTimeout;
        failure = opened ? string.Empty : "The gate was never opened.";
        return opened;
    }

    public void Dispose()
    {
        _gate.Dispose();
        _turn.Dispose();
    }
}
