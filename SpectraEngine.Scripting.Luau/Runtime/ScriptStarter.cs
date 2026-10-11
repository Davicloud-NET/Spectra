using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Loads and starts a level's scripts, in the order of the scene tree. One
// that cannot start says which node it is on, and the rest still run.
internal sealed class ScriptStarter(LuauHost host, IScriptSource source, NodeNamingSink sink)
{
    public void StartAll(Scene scene)
    {
        foreach (SceneNode node in scene.Root.Traverse())
        {
            // A close asked for from another thread takes the state when the
            // script that was running comes back.
            if (host.IsClosed)
                return;

            // A module runs when a script asks for it, and nothing asks yet.
            if (node.Script is { IsEnabled: true, IsModule: false } script)
                Start(node, script);
        }
    }

    private void Start(SceneNode node, Script script)
    {
        if (source.Read(script.Path) is not { } text)
        {
            Report(script, $"'{node.Name}' names a script file that is not in the level's content, so it did not start.");
            return;
        }

        sink.Loading = node;
        LuauScript? loaded = host.Load(script.Path, text);
        sink.Loading = null;

        // The host has reported a script that did not compile.
        if (loaded is null)
            return;

        if (loaded.SetGlobal("script"u8, LuauValue.Object(ScriptTags.Object, node)))
            loaded.Start();
        else
            Report(script, $"The script on '{node.Name}' could not be given its node, so it did not start.");
    }

    private void Report(Script script, string text) =>
        sink.Report(new ScriptMessage(ScriptSeverity.Error, text, script.Path, 0));
}
