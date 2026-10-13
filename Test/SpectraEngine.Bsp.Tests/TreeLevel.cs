using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

// A small level that plays, and the door a script changes its tree through.
// The level has a world brush, a turned group with two parts, a lamp and an
// entity. What its entities log is kept in the order it came.
internal sealed class TreeLevel
{
    public TreeLevel(EntityCatalog? catalog = null)
    {
        Wall = EntityMotion.BrushNode(Scene.Root, "Wall", BrushKind.World, new Vector3(0f, 0f, 10f));
        Group = Scene.Root.CreateChild("Group");
        Group.LocalPosition = new Vector3(4f, 0f, -2f);
        Group.LocalRotation = Quaternion.CreateFromYawPitchRoll(0.7f, 0f, 0f);
        Crate = EntityMotion.BrushNode(Group, "Crate", BrushKind.Part, new Vector3(1f, 0.5f, 2f));
        Barrel = EntityMotion.BrushNode(Group, "Barrel", BrushKind.Part, new Vector3(-1f, 0.5f, 0f));
        Lamp = Scene.Root.CreateChild("Lamp");
        Lamp.LocalPosition = new Vector3(0f, 4f, 0f);
        Lamp.Light = new Light { Kind = LightKind.Point, Range = 12f };
        Sign = EntityRuntime.Place(Scene.Root, "Sign", "lifecycle");

        World = new EntityWorld(Scene, new CapturingLogger(), catalog ?? EntityMotion.Catalog(Log));
    }

    public Scene Scene { get; } = new("Tree");

    public EntityWorld World { get; }

    public List<string> Log { get; } = [];

    public SceneNode Wall { get; }

    public SceneNode Group { get; }

    public SceneNode Crate { get; }

    public SceneNode Barrel { get; }

    public SceneNode Lamp { get; }

    public SceneNode Sign { get; }

    public List<NodeCopy> Copies { get; } = [];

    public List<SceneNode> Destroyed { get; } = [];

    // Starts the level and hands back the door to its tree.
    public TreeWrites Play()
    {
        World.Activate();
        return new ScriptWrites(World).Tree;
    }

    public void Tick() => World.Tick(EntityMotion.Dt);

    // The level as a save writes it.
    public byte[] Saved() => MapWriter.Write(MapSceneBinder.FromScene(Scene));

    public SceneNode Make(TreeWrites tree, string className)
    {
        tree.TryMake(className, out SceneNode? made, out string? refusal).ShouldBeTrue(refusal);
        return made.ShouldNotBeNull();
    }

    public SceneNode Clone(TreeWrites tree, SceneNode source)
    {
        tree.TryClone(source, Copies, out SceneNode? copy, out string? refusal).ShouldBeTrue(refusal);
        return copy.ShouldNotBeNull();
    }

    public void Destroy(TreeWrites tree, SceneNode node) =>
        tree.TryDestroy(node, Destroyed, out string? refusal).ShouldBeTrue(refusal);

    public static void Parent(TreeWrites tree, SceneNode node, SceneNode? parent) =>
        tree.TrySetParent(node, parent, out string? refusal).ShouldBeTrue(refusal);

    // Why a parent was refused. The node keeps the one it has.
    public static string Refused(TreeWrites tree, SceneNode node, SceneNode? parent)
    {
        SceneNode? before = node.Parent;
        tree.TrySetParent(node, parent, out string? refusal).ShouldBeFalse();
        node.Parent.ShouldBeSameAs(before);
        return refusal.ShouldNotBeNull();
    }
}
