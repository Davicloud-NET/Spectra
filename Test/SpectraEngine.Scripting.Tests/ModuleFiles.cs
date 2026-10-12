using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

// Scripts and modules at paths a test names, so a path that is asked for
// has a file to start from.
internal static class ModuleFiles
{
    // A file no node names. A script reaches it by path.
    public static void FileAt(this ScriptLevel level, string path, string text) => level.Files.Add(path, text);

    // A script node named after its file.
    public static SceneNode ScriptAt(this ScriptLevel level, string path, string text, SceneNode? under = null)
    {
        level.Files.Add(path, text);

        SceneNode node = (under ?? level.Scene.Root).CreateChild(Path.GetFileNameWithoutExtension(path));
        node.Script = new Script(path);
        return node;
    }

    // A module node named after its file.
    public static SceneNode ModuleAt(this ScriptLevel level, string path, string text, SceneNode? under = null)
    {
        SceneNode node = level.ScriptAt(path, text, under);
        node.Script = new Script(path) { IsModule = true };
        return node;
    }

    // The errors the level heard, each as its place and its text.
    public static string[] ErrorsWithPlace(this ScriptLevel level) =>
    [
        .. level.Sink.Messages
            .Where(message => message.Severity == ScriptSeverity.Error)
            .Select(message => $"{message.Position}: {message.Text}"),
    ];

    // The watchdog goes by the wall clock. The short limit holds for the one
    // call that hangs, and the default is back before any other script runs.
    public static void StopTheNextHangSoon(this ScriptLevel level)
    {
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        runtime.CallLimit = TimeSpan.FromMilliseconds(100);
        level.Sink.Heard = message =>
        {
            if (message.Severity == ScriptSeverity.Error)
                runtime.CallLimit = LuauHost.DefaultCallLimit;
        };
    }
}
