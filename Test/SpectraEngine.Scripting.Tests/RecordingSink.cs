using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// Keeps what scripts report, in the order it came.
internal sealed class RecordingSink : IScriptSink
{
    internal List<ScriptMessage> Messages { get; } = [];

    internal ScriptMessage Single => Messages.ShouldHaveSingleItem();

    // Called with each message once it is kept, on the thread that runs the scripts.
    internal Action<ScriptMessage>? Heard { get; set; }

    public void Report(in ScriptMessage message)
    {
        Messages.Add(message);
        Heard?.Invoke(message);
    }

    internal string[] Texts(ScriptSeverity severity) =>
        [.. Messages.Where(message => message.Severity == severity).Select(message => message.Text)];
}
