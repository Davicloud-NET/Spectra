using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// Keeps what scripts report, in the order it came.
internal sealed class RecordingSink : IScriptSink
{
    internal List<ScriptMessage> Messages { get; } = [];

    internal ScriptMessage Single => Messages.ShouldHaveSingleItem();

    public void Report(in ScriptMessage message) => Messages.Add(message);

    internal string[] Texts(ScriptSeverity severity) =>
        [.. Messages.Where(message => message.Severity == severity).Select(message => message.Text)];
}
