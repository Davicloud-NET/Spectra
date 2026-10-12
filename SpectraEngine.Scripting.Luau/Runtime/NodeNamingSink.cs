using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What the host reports through. The host knows a script by its file, and a
// file may be on many nodes. So while a script is being compiled, an error
// is given the name of the node it is on. What the level's sink throws ends
// here, since a report is made in the middle of a tick.
internal sealed class NodeNamingSink(IScriptSink sink) : IScriptSink
{
    private ScriptMessage? _held;

    // The node whose script is being compiled, or null.
    public SceneNode? Loading { get; set; }

    // While a module's file runs, the error that came last waits here. The
    // module's own failure comes last, and whoever runs it says whether it
    // is told. Any other goes on before whatever is reported next.
    public bool HoldsErrors { get; set; }

    // The error of a script that did not compile, as a standing problem of its node.
    public static ScriptMessage OnNode(in ScriptMessage message, SceneNode node) => message with
    {
        Text = $"{message.Text}. The script on '{node.Name}' did not start.",
        ProblemNode = node.Id,
    };

    // Hands over the error that waits, which is then not told from here.
    public ScriptMessage? TakeHeld()
    {
        ScriptMessage? held = _held;
        _held = null;
        return held;
    }

    public void Flush()
    {
        if (TakeHeld() is { } held)
            Forward(in held);
    }

    // Tells a message now, also while errors are held.
    public void Tell(in ScriptMessage message)
    {
        Flush();
        Forward(in message);
    }

    public void Report(in ScriptMessage message)
    {
        bool isError = message.Severity == ScriptSeverity.Error;
        ScriptMessage named = isError && Loading is { } node ? OnNode(in message, node) : message;

        Flush();
        if (isError && HoldsErrors)
            _held = named;
        else
            Forward(in named);
    }

    private void Forward(in ScriptMessage message)
    {
        try
        {
            sink.Report(in message);
        }
        catch (Exception) // boundary: the sink is where a failure is told, and it is what failed
        {
        }
    }
}
