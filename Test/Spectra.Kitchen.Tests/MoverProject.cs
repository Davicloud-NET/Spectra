using System.IO;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Tests;

// A project whose one level is the demo's course with the script that has
// the mover in Luau move the player. The mover's files are the engine's
// own, copied out of its content root as a project would hold them.
internal static class MoverProject
{
    public const string Bundle = "Maps/Course.smap";

    // The script the level names, the mover it asks for, and the mover that
    // one is asked for by. No node names the last two.
    public static readonly string[] Scripts =
    [
        DemoMover.ScriptPath,
        DemoMover.MoverPath,
        "Scripts/movers/double_jump.luau",
    ];

    public static TempProject Write()
    {
        var project = new TempProject();
        foreach (string path in Scripts)
            project.WriteAsset(path, File.ReadAllBytes(ContentRoot.ResolveAbsolute(ContentRoot.Path, path)));

        var scene = new Scene("Course");
        DemoPlayArea.Build(scene, MaterialRef.Default, MaterialRef.Default, MaterialRef.Default);
        DemoMover.Build(scene);

        string bundle = Path.Combine(project.Layout.MapsPath, Path.GetFileName(Bundle));
        Directory.CreateDirectory(bundle);
        MapBundle.Save(bundle, MapSceneBinder.FromScene(scene));
        return project;
    }
}
