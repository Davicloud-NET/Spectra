using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Loads and starts a level's scripts, in the order of the scene tree. One
// that cannot start says which node it is on, and the rest still run.
[ScriptGlobals]
internal sealed partial class ScriptStarter(LuauHost host, IScriptSource source, NodeNamingSink sink)
{
    public void StartAll(Scene scene)
    {
        foreach (SceneNode node in scene.Root.Traverse())
        {
            // A close asked for from another thread takes the state when the
            // script that was running comes back.
            if (host.IsClosed)
                return;

            // A module runs when a script asks for it.
            if (node.Script is { IsEnabled: true, IsModule: false } script)
                Start(node, script);
        }
    }

    /// <summary>
    /// The node of the script that is running. Each script has its own. It is nil in a module
    /// that was asked for by path, which has no node.
    /// </summary>
    // By hand: it is a global of one script and not of the state, so it is
    // set when the script is loaded.
    [ScriptProperty(Name = "script", Type = "Instance", ByHand = true)]
    private static LuauValue ScriptOf(SceneNode node) => LuauValue.Object(ScriptTags.Object, node);

    private void Start(SceneNode node, Script script)
    {
        if (source.Read(script.Path, out string? problem) is not { } text)
        {
            Report(node, script, problem is null
                ? $"'{node.Name}' names a script file that is not in the level's content, so it did not start."
                : $"{problem} The script on '{node.Name}' did not start.");
            return;
        }

        sink.Loading = node;
        LuauScript? loaded = host.Load(script.Path, text);
        sink.Loading = null;

        // The host has reported a script that did not compile.
        if (loaded is null)
            return;

        if (loaded.SetGlobal("script"u8, ScriptOf(node)))
            loaded.Start();
        else
            Report(node, script, $"The script on '{node.Name}' could not be given its node, so it did not start.");
    }

    // A script that did not start stays wrong until the level is played again.
    private void Report(SceneNode node, Script script, string text) =>
        sink.Report(new ScriptMessage(ScriptSeverity.Error, text, script.Path, 0) { ProblemNode = node.Id });
}
