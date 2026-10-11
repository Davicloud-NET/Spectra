using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>The rows a property panel shows for a node's script.</summary>
public sealed class ScriptInspectorTests
{
    private static List<PropertyRow> Describe(params SceneNode[] nodes)
    {
        var rows = new List<PropertyRow>();
        NodeInspector.Describe(nodes, rows);
        return rows;
    }

    private static List<PropertyRow> ScriptRows(List<PropertyRow> rows) =>
        rows.Where(row => row.Group == NodeInspector.ScriptGroup).ToList();

    private static SceneNode Opener() => new("Opener") { Script = new Script("Scripts/door.luau") };

    [Fact]
    public void A_script_node_shows_its_file_and_its_two_switches()
    {
        var node = new SceneNode("Opener")
        {
            Script = new Script("Scripts/door.luau") { IsEnabled = false, IsModule = true },
        };

        List<PropertyRow> rows = ScriptRows(Describe(node));

        rows.Select(row => row.Name).ShouldBe(["File", "Enabled", "Module"]);
        rows.Select(row => row.Id).ShouldBe(
            [PropertyId.ScriptFile, PropertyId.ScriptEnabled, PropertyId.ScriptModule]);

        rows[0].Text.ShouldBe("Scripts/door.luau");
        rows[1].Kind.ShouldBe(PropertyKind.Boolean);
        rows[1].Flag.ShouldBeFalse();
        rows[2].Kind.ShouldBe(PropertyKind.Boolean);
        rows[2].Flag.ShouldBeTrue();
        rows.ShouldAllBe(row => row.IsEditable);
    }

    [Fact]
    public void The_file_row_is_one_that_picks_a_script_file()
    {
        PropertyRow file = ScriptRows(Describe(Opener()))[0];

        file.Kind.ShouldBe(PropertyKind.Asset);
        file.Asset.ShouldBe(AssetKind.Script);
        file.Note.ShouldBeEmpty("a node in no level has no project to look in");
    }

    [Fact]
    public void A_new_script_shows_switched_on_and_no_module()
    {
        List<PropertyRow> rows = ScriptRows(Describe(Opener()));

        rows[1].Flag.ShouldBeTrue();
        rows[2].Flag.ShouldBeFalse();
    }

    [Fact]
    public void The_module_row_says_what_a_module_is()
    {
        PropertyRow module = Describe(new SceneNode("Shared") { Script = new Script("Scripts/shared.luau") })
            .Single(row => row.Id == PropertyId.ScriptModule);

        module.Help.ShouldContain("another script asks for it");
    }

    [Fact]
    public void A_node_with_no_script_grows_no_script_group()
    {
        Describe(new SceneNode("Plain")).ShouldNotContain(row => row.Group == NodeInspector.ScriptGroup);
        Describe(new SceneNode("Lamp") { Light = new Light() })
            .ShouldNotContain(row => row.Group == NodeInspector.ScriptGroup);
    }

    [Fact]
    public void A_script_with_no_children_has_no_place_so_it_shows_no_transform()
    {
        SceneNode node = Opener();

        NodeInspector.HasTransformRows(node).ShouldBeFalse();
        Describe(node).Select(row => row.Group).Distinct().ShouldBe(
            [NodeInspector.NodeGroup, NodeInspector.ScriptGroup]);
    }

    [Fact]
    public void A_script_with_children_is_a_parent_of_things_and_keeps_its_transform()
    {
        SceneNode node = Opener();
        node.CreateChild("Lamp");

        NodeInspector.HasTransformRows(node).ShouldBeTrue();
        Describe(node).Select(row => row.Group).Distinct().ShouldBe(
            [NodeInspector.NodeGroup, NodeInspector.TransformGroup, NodeInspector.ScriptGroup]);
    }

    [Fact]
    public void A_part_that_carries_a_script_stands_somewhere_and_keeps_its_transform()
    {
        var part = new SceneNode("Door")
        {
            BrushKind = BrushKind.Part,
            Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f)),
            Script = new Script("Scripts/door.luau"),
        };

        NodeInspector.HasTransformRows(part).ShouldBeTrue();
        Describe(part).ShouldContain(row => row.Id == PropertyId.Position);
    }

    [Fact]
    public void Every_other_node_with_no_children_keeps_its_transform()
    {
        NodeInspector.HasTransformRows(new SceneNode("Marker")).ShouldBeTrue();
        NodeInspector.HasTransformRows(new SceneNode("Lamp") { Light = new Light() }).ShouldBeTrue();
        NodeInspector.HasTransformRows(new SceneNode("Relay") { Entity = new EntityData("logic_relay") })
            .ShouldBeTrue();
    }

    [Fact]
    public void The_script_group_comes_after_the_entity_and_before_the_material()
    {
        // The panel lays out in PropertyId order, and a merged selection sorts by it.
        var node = new SceneNode("Door")
        {
            BrushKind = BrushKind.Part,
            Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f)),
            Entity = new EntityData("func_door"),
            Script = new Script("Scripts/door.luau"),
        };

        List<PropertyRow> rows = Describe(node);

        rows.Select(row => row.Group).Distinct().ShouldBe(
        [
            NodeInspector.NodeGroup,
            NodeInspector.TransformGroup,
            NodeInspector.BrushGroup,
            NodeInspector.BehaviorGroup,
            NodeInspector.EntityGroup,
            NodeInspector.ScriptGroup,
            NodeInspector.MaterialGroup,
        ]);
        rows.Select(row => (int)row.Id).ShouldBeInOrder();
    }

    [Fact]
    public void Two_scripts_that_agree_merge_and_two_that_differ_say_which_rows_do()
    {
        var a = new SceneNode("A") { Script = new Script("Scripts/door.luau") };
        var b = new SceneNode("B") { Script = new Script("Scripts/door.luau") { IsEnabled = false } };

        List<PropertyRow> rows = ScriptRows(Describe(a, b));

        rows.Count.ShouldBe(3);
        rows.Single(row => row.Id == PropertyId.ScriptFile).IsMixed.ShouldBeFalse();
        rows.Single(row => row.Id == PropertyId.ScriptEnabled).IsMixed.ShouldBeTrue();
        rows.Single(row => row.Id == PropertyId.ScriptModule).IsMixed.ShouldBeFalse();
        rows.ShouldAllBe(row => row.PresentCount == 2 && row.SelectionCount == 2);
    }

    [Fact]
    public void A_selection_where_only_some_nodes_are_scripts_says_how_many_carry_the_rows()
    {
        var script = new SceneNode("Opener") { Script = new Script("Scripts/door.luau") };
        var plain = new SceneNode("Plain");

        List<PropertyRow> rows = ScriptRows(Describe(script, plain));

        rows.Count.ShouldBe(3);
        rows.ShouldAllBe(row => row.PresentCount == 1 && row.SelectionCount == 2 && row.IsPartial);
    }

    [Fact]
    public void A_script_selected_beside_a_part_leaves_the_transform_rows_to_the_part()
    {
        var part = new SceneNode("Door") { Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f)) };

        PropertyRow position = Describe(Opener(), part).Single(row => row.Id == PropertyId.Position);

        position.PresentCount.ShouldBe(1);
        position.SelectionCount.ShouldBe(2);
    }

    [Fact]
    public void A_file_the_project_has_carries_no_note_and_one_it_lacks_says_missing()
    {
        using var project = new ScriptFolder();
        project.Write("Scripts/door.luau");
        SceneNode here = project.Scene.Root.CreateChild("Here");
        here.Script = new Script("Scripts/door.luau");
        SceneNode gone = project.Scene.Root.CreateChild("Gone");
        gone.Script = new Script("Scripts/nobody_wrote_this.luau");

        ScriptRows(Describe(here))[0].Note.ShouldBeEmpty();
        ScriptRows(Describe(gone))[0].Note.ShouldBe("missing");
    }

    [Fact]
    public void A_file_deleted_while_its_node_is_selected_says_missing_on_the_next_look()
    {
        using var project = new ScriptFolder();
        string full = project.Write("Scripts/door.luau");
        SceneNode node = project.Scene.Root.CreateChild("Opener");
        node.Script = new Script("Scripts/door.luau");
        ScriptRows(Describe(node))[0].Note.ShouldBeEmpty();

        File.Delete(full);

        ScriptRows(Describe(node))[0].Note.ShouldBe("missing");
    }

    [Fact]
    public void A_publish_that_keeps_its_answer_does_not_ask_the_disk_again_until_the_answer_is_old()
    {
        // The rows are built on every publish, many times a second, on the
        // thread that draws. A project on a slow share would stall it.
        using var project = new ScriptFolder();
        string full = project.Write("Scripts/door.luau");
        SceneNode node = project.Scene.Root.CreateChild("Opener");
        node.Script = new Script("Scripts/door.luau");
        var clock = new ManualClock();
        var files = new ScriptFileCheck(TimeSpan.FromSeconds(0.5), clock);
        NoteOf(node, files).ShouldBeEmpty();

        File.Delete(full);
        clock.Advance(0.4);
        NoteOf(node, files).ShouldBeEmpty("the kept answer is not old yet");

        clock.Advance(0.2);
        NoteOf(node, files).ShouldBe("missing");
    }

    [Fact]
    public void A_kept_answer_is_for_one_path_so_another_file_is_asked_at_once()
    {
        using var project = new ScriptFolder();
        project.Write("Scripts/door.luau");
        SceneNode node = project.Scene.Root.CreateChild("Opener");
        node.Script = new Script("Scripts/door.luau");
        var files = new ScriptFileCheck(TimeSpan.FromSeconds(0.5), new ManualClock());
        NoteOf(node, files).ShouldBeEmpty();

        node.Script = new Script("Scripts/nobody_wrote_this.luau");

        NoteOf(node, files).ShouldBe("missing");
    }

    [Fact]
    public void A_kept_answer_is_for_one_level_so_another_levels_file_is_asked_at_once()
    {
        using var here = new ScriptFolder();
        here.Write("Scripts/door.luau");
        using var elsewhere = new ScriptFolder();
        SceneNode has = here.Scene.Root.CreateChild("Opener");
        has.Script = new Script("Scripts/door.luau");
        SceneNode lacks = elsewhere.Scene.Root.CreateChild("Opener");
        lacks.Script = new Script("Scripts/door.luau");
        var files = new ScriptFileCheck(TimeSpan.FromSeconds(0.5), new ManualClock());

        NoteOf(has, files).ShouldBeEmpty();
        NoteOf(lacks, files).ShouldBe("missing");
    }

    private static string NoteOf(SceneNode node, ScriptFileCheck files)
    {
        var rows = new List<PropertyRow>();
        NodeInspector.Describe([node], rows, scriptFiles: files);
        return ScriptRows(rows)[0].Note;
    }

    [Fact]
    public void A_path_typed_with_other_slashes_finds_the_same_file()
    {
        using var project = new ScriptFolder();
        project.Write("Scripts/Doors/vault.luau");
        SceneNode node = project.Scene.Root.CreateChild("Opener");
        node.Script = new Script(@"\Scripts\Doors\vault.luau");

        PropertyRow file = ScriptRows(Describe(node))[0];

        file.Text.ShouldBe("Scripts/Doors/vault.luau");
        file.Note.ShouldBeEmpty();
    }

    [Fact]
    public void A_selection_of_many_scripts_does_not_ask_the_disk_about_each()
    {
        // The rows are built on every publish, on the thread that draws.
        using var project = new ScriptFolder();
        SceneNode a = project.Scene.Root.CreateChild("A");
        a.Script = new Script("Scripts/gone.luau");
        SceneNode b = project.Scene.Root.CreateChild("B");
        b.Script = new Script("Scripts/gone.luau");

        ScriptRows(Describe(a, b))[0].Note.ShouldBeEmpty();
    }

    // A scene that reads its content from a folder made for one test.
    private sealed class ScriptFolder : IDisposable
    {
        private readonly string _root =
            Path.Combine(Path.GetTempPath(), "spectra-script-rows-" + Guid.NewGuid().ToString("N"));

        public ScriptFolder()
        {
            Directory.CreateDirectory(_root);
            Scene = new Scene("Scripts")
            {
                Assets = new AssetManager(NullLogger<AssetManager>.Instance, _root, hotReloadEnabled: false),
            };
        }

        public Scene Scene { get; }

        public string Write(string contentPath)
        {
            string full = Path.Combine(_root, contentPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full).ShouldNotBeNull());
            File.WriteAllText(full, "print(\"hello\")\n");
            return full;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A locked temp folder is not a test failure.
            }
        }
    }
}
