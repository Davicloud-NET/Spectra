using SpectraEngine.Core.Scene;
using System.Collections.Generic;

namespace SpectraEngine.Core.Inspection;

// The rows a script shows: its file and its two switches.
internal static class ScriptRows
{
    private const string ModuleHelp =
        "A module does not run when the level starts. It runs when another script asks for it.";

    // Without files the File row says nothing about its file. That is how a
    // selection of many is described, so a publish asks about one script at most.
    public static void Describe(SceneNode node, Script script, ScriptFileCheck? files, List<PropertyRow> into)
    {
        const string group = NodeInspector.ScriptGroup;
        string note = files is not null && IsMissing(node, script, files) ? "missing" : string.Empty;

        into.Add(PropertyRow.OfAsset(group, "File", PropertyId.ScriptFile, script.Path, AssetKind.Script, note));
        into.Add(PropertyRow.OfFlag(group, "Enabled", PropertyId.ScriptEnabled, script.IsEnabled));
        into.Add(PropertyRow.OfFlag(group, "Module", PropertyId.ScriptModule, script.IsModule)
            with { Help = ModuleHelp });
    }

    // Asks the content sources a play session reads the file from. Nothing
    // caches a script, so unlike a material this looks at the disk.
    private static bool IsMissing(SceneNode node, Script script, ScriptFileCheck files) =>
        node.Owner?.Assets is { } assets && !files.Exists(assets.Content, script.Path);
}
