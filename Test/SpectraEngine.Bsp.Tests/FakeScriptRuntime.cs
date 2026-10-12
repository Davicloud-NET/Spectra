using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

// Runs no script. Logs one line per call, into a list a test can share with
// other recorders to assert the order across them.
internal sealed class FakeScriptRuntime : IScriptRuntime
{
    private readonly List<string> _log;

    public FakeScriptRuntime(List<string>? log = null) => _log = log ?? [];

    // What a script would do when the level starts, such as ask for a wake.
    public Action? OnStart { get; set; }

    // What a thread that woke would do next.
    public Action<long>? OnWake { get; set; }

    // What a runtime would do with a wake that was dropped. It answers
    // whether what waited for the wake was ended.
    public Func<long, bool>? OnDrop { get; set; }

    public bool IsClosed { get; private set; }

    public void StartScripts()
    {
        _log.Add("start scripts");
        OnStart?.Invoke();
    }

    public void Wake(long token)
    {
        _log.Add($"wake {token}");
        OnWake?.Invoke(token);
    }

    public bool Drop(long token)
    {
        _log.Add($"drop {token}");
        return OnDrop?.Invoke(token) ?? true;
    }

    public void BeforeSimulation() => _log.Add("before simulation");

    public void AfterSimulation() => _log.Add("after simulation");

    public void Close()
    {
        IsClosed = true;
        _log.Add("close scripts");
    }
}
