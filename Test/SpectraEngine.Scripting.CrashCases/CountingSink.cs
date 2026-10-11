using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.CrashCases;

// Counts what scripts report and keeps the last message.
internal sealed class CountingSink : IScriptSink
{
    internal int Errors { get; private set; }

    internal ScriptMessage Last { get; private set; }

    public void Report(in ScriptMessage message)
    {
        Last = message;
        if (message.Severity == ScriptSeverity.Error)
            Errors++;
    }
}
