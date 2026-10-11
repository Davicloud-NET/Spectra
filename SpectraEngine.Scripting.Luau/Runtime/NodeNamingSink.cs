using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What the host reports through. The host knows a script by its file, and a
// file may be on many nodes. So while a script is being compiled, an error
// is given the name of the node it is on.
internal sealed class NodeNamingSink(IScriptSink sink) : IScriptSink
{
    // The node whose script is being compiled, or null.
    public SceneNode? Loading { get; set; }

    public void Report(in ScriptMessage message)
    {
        if (Loading is not { } node || message.Severity != ScriptSeverity.Error)
        {
            sink.Report(in message);
            return;
        }

        sink.Report(message with { Text = $"{message.Text}. The script on '{node.Name}' did not start." });
    }
}
