using Microsoft.Extensions.Logging.Abstractions;

using Spectra.Kitchen.Tests;

using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editor.Render.Tests;

// A project's Assets folder with scripts among other files, and a level that
// reads its content from it, for the sheets that show scripts.
internal sealed class ScriptProjectFixture : IDisposable
{
    public const string Spin = "Scripts/Spin.luau";
    public const string Gone = "Scripts/Nobody_wrote_this.luau";

    // The scripts, in the order a picker shows them.
    public static readonly string[] Scripts =
    [
        "Scripts/Door.luau", "Scripts/Lobby.luau", Spin, "Scripts/shared/Timer.luau",
    ];

    public ScriptProjectFixture()
    {
        Write("Scripts/Door.luau", "print(\"open\")\nprint(\"close\")\n");
        Write("Scripts/Lobby.luau", "print(\"Hello from \" .. script.Parent.Name)\n");
        Write(Spin, "-- turns its parent\nprint(\"spin\")\n");
        Write("Scripts/shared/Timer.luau", "return {}\n");
        Write("Scripts/notes.txt", "not a script\n");
        File.WriteAllBytes(FullPath("Scripts/brick.png"), TempProject.Png());
        Write("Scripts/wall.spectramat", "shader lit\n");
        Write("Scripts/crate.obj", "o crate\n");

        Level = new Scene("Lobby")
        {
            Assets = new AssetManager(NullLogger<AssetManager>.Instance, Root, hotReloadEnabled: false),
        };
    }

    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), "spectra-script-sheets-" + Guid.NewGuid().ToString("N"));

    // The folder that holds the scripts beside files of other kinds.
    public string ScriptsFolder => Path.Combine(Root, "Scripts");

    // A level whose content is this folder, so a script row can say whether
    // its file is there.
    public Scene Level { get; }

    // A part with a script under it, which is what Insert Script leaves.
    public SceneNode ScriptUnderAPart(string file)
    {
        SceneNode part = Level.Root.CreateChild("Coin");
        SceneNode script = part.CreateChild("Script");
        script.Script = new Script(file);
        return script;
    }

    public string FullPath(string relative) =>
        Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A locked temp folder is not a test failure.
        }
    }

    private void Write(string relative, string text)
    {
        string full = FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full).ShouldNotBeNull());
        File.WriteAllText(full, text);
    }
}
