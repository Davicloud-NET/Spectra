using SpectraEngine.Core.Entities;
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

    // What a function connected to an output would do when it is called.
    public Action<long>? OnHear { get; set; }

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

    public void Hear(long listener, string parameter)
    {
        _log.Add(parameter.Length == 0 ? $"hear {listener}" : $"hear {listener} '{parameter}'");
        OnHear?.Invoke(listener);
    }

    public void BeforeSimulation() => _log.Add("before simulation");

    public void AfterSimulation() => _log.Add("after simulation");

    public void Close()
    {
        IsClosed = true;
        _log.Add("close scripts");
    }

    public void HearTouch(long listener, in TouchVisitor visitor) =>
        _log.Add($"hear touch {listener} by {visitor.Id}");
}
