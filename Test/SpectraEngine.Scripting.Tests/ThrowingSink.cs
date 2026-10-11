using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// A sink that fails on every message, as a host's own might.
internal sealed class ThrowingSink : IScriptSink
{
    public int Asked { get; private set; }

    public void Report(in ScriptMessage message)
    {
        Asked++;
        throw new InvalidOperationException("the sink is broken");
    }
}
