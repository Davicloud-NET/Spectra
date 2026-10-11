using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Icons;
using SpectraEngine.Editor.Shell;
using System.Numerics;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// A row's icon in the scene tree, once the node's class and its kind of
/// light have crossed from the engine in a snapshot.
/// </summary>
public sealed class SceneTreeIconTests
{
    // The engine's side: a scene whose changes are logged, and the tree the
    // shell builds from what the log hands over.
    private sealed class Rig
    {
        private readonly SceneChangeLog _log = new();
        private long _frame;

        public Rig()
        {
            _log.Observe(Scene);
            _log.Drain();
            Tree.Schemas = BuiltinClasses.Catalog;
        }

        public Scene Scene { get; } = new("Tree");

        public SceneTreeModel Tree { get; } = new(new EngineHost(NullLogger.Instance), NullLogger.Instance);

        public SceneNode Add(SceneNode node)
        {
            Scene.Root.AddChild(node);
            return node;
        }

        // One published snapshot: ids and values, never a node.
        public void Publish()
        {
            (IReadOnlyList<SceneChange> changes, bool overflowed) = _log.Drain();
            overflowed.ShouldBeFalse();
            Tree.ApplyChanges(new FrameSnapshot { FrameNumber = ++_frame, Changes = changes });
        }

        public IconLook LookOf(SceneNode node)
        {
            Tree.TryGetNode(node.Id, out SceneTreeNode row).ShouldBeTrue(node.Name);
            return row.Look;
        }
    }

    private static SceneNode Part(string name, string? className = null) => new(name)
    {
        BrushKind = BrushKind.Part,
        Brush = Brush.CreateBox(-Vector3.One, Vector3.One),
        Entity = className is null ? null : new EntityData(className),
    };

    private static IconLook ClassLook(string className) => EditorIcons.ForEntity(BuiltinClasses.Catalog, className);

    [Fact]
    public void A_door_shows_the_door_icon_and_not_the_part_icon()
    {
        var rig = new Rig();
        SceneNode door = rig.Add(Part("Door", "func_door"));
        SceneNode crate = rig.Add(Part("Crate"));

        rig.Publish();

        rig.LookOf(door).ShouldBe(ClassLook("func_door"));
        rig.LookOf(door).Icon.ShouldNotBe(rig.LookOf(crate).Icon);
        rig.LookOf(crate).ShouldBe(EditorIcons.ForNode(SceneNodeKind.BrushPart, null, null, null));
    }

    [Fact]
    public void A_relay_shows_the_relay_icon()
    {
        var rig = new Rig();
        SceneNode relay = rig.Add(new SceneNode("Relay") { Entity = new EntityData("logic_relay") });
        SceneNode timer = rig.Add(new SceneNode("Timer") { Entity = new EntityData("logic_timer") });

        rig.Publish();

        rig.LookOf(relay).ShouldBe(ClassLook("logic_relay"));
        rig.LookOf(timer).ShouldBe(ClassLook("logic_timer"));
        rig.LookOf(relay).Icon.ShouldNotBe(rig.LookOf(timer).Icon);
        rig.LookOf(relay).Tint.ShouldBe(rig.LookOf(timer).Tint);
    }

    [Fact]
    public void A_sun_shows_the_sun_icon_and_a_lamp_the_lamp_icon()
    {
        var rig = new Rig();
        SceneNode sun = rig.Add(new SceneNode("Sun") { Light = new Light { Kind = LightKind.Directional } });
        SceneNode lamp = rig.Add(new SceneNode("Lamp") { Light = new Light { Kind = LightKind.Point } });

        rig.Publish();

        rig.LookOf(sun).ShouldBe(EditorIcons.ForLight(LightKind.Directional));
        rig.LookOf(lamp).ShouldBe(EditorIcons.ForLight(LightKind.Point));
        rig.LookOf(sun).Icon.ShouldNotBe(rig.LookOf(lamp).Icon);
    }

    [Fact]
    public void A_class_from_another_game_shows_the_unknown_icon()
    {
        var rig = new Rig();
        SceneNode stranger = rig.Add(new SceneNode("Stranger") { Entity = new EntityData("from_another_game") });

        rig.Publish();

        rig.LookOf(stranger).ShouldBe(ClassLook("from_another_game"));
    }

    [Fact]
    public void An_entity_with_no_class_yet_shows_the_unknown_icon_as_it_does_in_the_3d_view()
    {
        var rig = new Rig();
        SceneNode blank = rig.Add(new SceneNode("Blank") { Entity = new EntityData() });

        rig.Publish();

        rig.LookOf(blank).Icon.ShouldBe(WorldIcons.ForEntity(BuiltinClasses.Catalog, ""));
        rig.LookOf(blank).Icon.ShouldBe(WorldIcons.UnknownEntity);
    }

    [Fact]
    public void Making_a_part_a_door_changes_its_row_and_removing_the_entity_changes_it_back()
    {
        var rig = new Rig();
        SceneNode slab = rig.Add(Part("Slab"));
        rig.Publish();
        IconLook part = rig.LookOf(slab);

        slab.Entity = new EntityData("func_door");
        rig.Publish();
        rig.LookOf(slab).ShouldBe(ClassLook("func_door"));

        slab.Entity = null;
        rig.Publish();
        rig.LookOf(slab).ShouldBe(part);
    }

    [Fact]
    public void Changing_a_lights_kind_changes_its_row()
    {
        var rig = new Rig();
        SceneNode lamp = rig.Add(new SceneNode("Lamp") { Light = new Light { Kind = LightKind.Point } });
        rig.Publish();

        var spot = SetLightCommand.Settings.From(lamp.Light.ShouldNotBeNull()) with { Kind = LightKind.Spot };
        SetLightCommand command = SetLightCommand.Capture(lamp, spot);
        command.Do(rig.Scene);
        rig.Publish();
        rig.LookOf(lamp).ShouldBe(EditorIcons.ForLight(LightKind.Spot));

        command.Undo(rig.Scene);
        rig.Publish();
        rig.LookOf(lamp).ShouldBe(EditorIcons.ForLight(LightKind.Point));
    }

    [Fact]
    public void Converting_a_block_to_a_part_changes_its_row()
    {
        var rig = new Rig();
        SceneNode block = rig.Add(new SceneNode("Block") { Brush = Brush.CreateBox(-Vector3.One, Vector3.One) });
        rig.Publish();
        rig.LookOf(block).ShouldBe(EditorIcons.ForNode(SceneNodeKind.BrushWorld, null, null, null));

        block.BrushKind = BrushKind.Part;
        rig.Publish();

        rig.LookOf(block).ShouldBe(EditorIcons.ForNode(SceneNodeKind.BrushPart, null, null, null));
    }

    [Fact]
    public void A_script_under_a_part_shows_the_script_icon_in_its_own_tint()
    {
        var rig = new Rig();
        SceneNode door = rig.Add(Part("Door"));
        SceneNode opener = door.AddChild(new SceneNode("Opener") { Script = new Script("Scripts/door.luau") });

        rig.Publish();

        IconLook look = rig.LookOf(opener);
        look.ShouldBe(EditorIcons.ForNode(SceneNodeKind.Script, null, null, null));
        look.Icon.ShouldBe("IconScript");
        look.Icon.ShouldNotBe(rig.LookOf(door).Icon);
        look.Tint.ShouldNotBe(rig.LookOf(door).Tint);
        rig.Tree.TryGetNode(opener.Id, out SceneTreeNode row).ShouldBeTrue();
        row.Kind.ShouldBe(SceneNodeKind.Script);
        rig.Tree.ParentOf(row).ShouldNotBeNull().Id.ShouldBe(door.Id);
    }

    [Fact]
    public void Putting_a_script_on_a_node_changes_its_row_and_taking_it_off_changes_it_back()
    {
        var rig = new Rig();
        SceneNode node = rig.Add(new SceneNode("Opener"));
        rig.Publish();
        IconLook empty = rig.LookOf(node);

        node.Script = new Script("Scripts/door.luau");
        rig.Publish();
        rig.LookOf(node).ShouldBe(EditorIcons.ForNode(SceneNodeKind.Script, null, null, null));

        node.Script = null;
        rig.Publish();
        rig.LookOf(node).ShouldBe(empty);
    }

    [Fact]
    public void A_part_that_carries_a_script_keeps_the_part_icon()
    {
        var rig = new Rig();
        SceneNode crate = rig.Add(Part("Crate"));
        crate.Script = new Script("Scripts/crate.luau");

        rig.Publish();

        rig.LookOf(crate).ShouldBe(EditorIcons.ForNode(SceneNodeKind.BrushPart, null, null, null));
    }

    [Fact]
    public void A_tree_rebuilt_from_the_whole_graph_carries_the_same_icons()
    {
        // What the tree asks for when it fell behind: every node as an Added change.
        var scene = new Scene("Rebuilt");
        SceneNode door = Part("Door", "func_door");
        var sun = new SceneNode("Sun") { Light = new Light { Kind = LightKind.Directional } };
        scene.Root.AddChild(door);
        scene.Root.AddChild(sun);

        var flattened = new List<SceneChange>();
        SceneTreeModel.Flatten(scene.Root, flattened);

        var tree = new SceneTreeModel(new EngineHost(NullLogger.Instance), NullLogger.Instance)
        {
            Schemas = BuiltinClasses.Catalog,
        };
        tree.ApplyChanges(new FrameSnapshot { FrameNumber = 1, Changes = flattened });

        tree.TryGetNode(door.Id, out SceneTreeNode doorRow).ShouldBeTrue();
        tree.TryGetNode(sun.Id, out SceneTreeNode sunRow).ShouldBeTrue();
        doorRow.Look.ShouldBe(ClassLook("func_door"));
        sunRow.Look.ShouldBe(EditorIcons.ForLight(LightKind.Directional));
    }

    [Fact]
    public void A_catalogue_that_arrives_after_the_nodes_gives_each_row_its_class_icon()
    {
        var rig = new Rig();
        rig.Tree.Schemas = null;
        SceneNode door = rig.Add(Part("Door", "func_door"));
        rig.Publish();
        rig.LookOf(door).ShouldBe(EditorIcons.PlainEntity);

        rig.Tree.Schemas = BuiltinClasses.Catalog;

        rig.LookOf(door).ShouldBe(ClassLook("func_door"));
    }

    [Fact]
    public void A_row_says_its_icon_changed_so_the_list_redraws_it()
    {
        var rig = new Rig();
        SceneNode slab = rig.Add(Part("Slab"));
        rig.Publish();
        rig.Tree.TryGetNode(slab.Id, out SceneTreeNode row).ShouldBeTrue();

        var raised = new List<string?>();
        row.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        slab.Entity = new EntityData("func_door");
        rig.Publish();

        raised.ShouldContain(nameof(SceneTreeNode.Look));
    }
}
