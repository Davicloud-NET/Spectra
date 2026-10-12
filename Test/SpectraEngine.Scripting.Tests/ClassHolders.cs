using System.Numerics;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

// A level with a node of every class of nodes under workspace.Kinds, each
// named after its class. Each is a leaf, and the script's own node is named
// like no member: a child answers to its name as a member does, and a test
// that asks an object for every name would take it for one.
internal static class ClassHolders
{
    // Plays the level for a tick with this script under a part, and hands
    // back what it printed.
    public static string[] Run(string text)
    {
        using var level = new ScriptLevel();
        SceneNode kinds = level.Scene.Root.CreateChild("Kinds");
        level.Part(NodeClassName.Part, Vector3.Zero, kinds);
        SpatialTestHelpers.CreateMeshNode(kinds, NodeClassName.MeshPart, Vector3.Zero);
        kinds.CreateChild(NodeClassName.Folder);
        kinds.CreateChild(NodeClassName.ModuleScript).Script = new Script("Scripts/module.luau") { IsModule = true };
        foreach (LightKind kind in Enum.GetValues<LightKind>())
            kinds.CreateChild(NodeClass.NameOf(NodeClass.OfLight(kind))).Light = new Light { Kind = kind };

        level.AddScript(level.Part("Holder", new Vector3(0f, 2f, 0f)), text, "Asker");
        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        return level.Printed;
    }
}
