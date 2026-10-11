using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// A sink that does not come back from a print of "block" until it is let go.
// That holds the thread that runs scripts inside a call into Luau.
internal sealed class BlockingSink : IScriptSink, IDisposable
{
    private readonly ManualResetEventSlim _letGo = new();

    public ManualResetEventSlim Blocked { get; } = new();

    public void Report(in ScriptMessage message)
    {
        if (message.Text != "block")
            return;

        Blocked.Set();
        _letGo.Wait();
    }

    public void LetGo() => _letGo.Set();

    public void Dispose()
    {
        _letGo.Dispose();
        Blocked.Dispose();
    }
}
