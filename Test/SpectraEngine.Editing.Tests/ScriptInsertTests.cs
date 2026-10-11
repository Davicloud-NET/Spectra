using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Hosting;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Insert Script through the editor host: the file it makes, the node it
/// adds, where both go, and what undo does with them.
/// </summary>
public sealed class ScriptInsertTests : IDisposable
{
    private const string Template = "print(\"Hello from \" .. script.Parent.Name)\n";

    // A project's Assets folder, made for one test.
    private readonly string _assets =
        Path.Combine(Path.GetTempPath(), "spectra-script-insert-" + Guid.NewGuid().ToString("N"), "Assets");

    private readonly Scene _scene = new("Lobby");
    private readonly SceneEditorHost _host;

    public ScriptInsertTests()
    {
        Directory.CreateDirectory(_assets);

        var renderer = new CompilingRenderer();

        // The host reads the viewport size in its constructor; zero breaks picking.
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));

        _host = new SceneEditorHost(
            NullLoggerFactory.Instance, _scene, renderer, new InputManager(NullLogger<InputManager>.Instance));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_assets).ShouldNotBeNull(), recursive: true);
        }
        catch (IOException)
        {
            // A locked temp folder is not a test failure.
        }
    }

    private string FullPath(string contentPath) =>
        Path.Combine(_assets, contentPath.Replace('/', Path.DirectorySeparatorChar));

    private SceneNode AddPart(string name)
    {
        SceneNode part = _scene.Root.CreateChild(name);
        part.BrushKind = BrushKind.Part;
        part.Brush = Brush.CreateBox(-Vector3.One, Vector3.One);
        return part;
    }

    private SceneNode AddSelectedPart(string name)
    {
        SceneNode part = AddPart(name);
        _scene.Selection.Select(part);
        return part;
    }

    private ScriptInsertReport Insert() => _host.InsertScript(_assets, Template);

    [Fact]
    public void On_a_selected_node_it_adds_a_script_under_that_node()
    {
        SceneNode part = AddSelectedPart("Coin");

        ScriptInsertReport report = Insert();

        report.Added.ShouldBeTrue(report.Describe());
        SceneNode script = part.Children.ShouldHaveSingleItem();
        script.Id.ShouldBe(report.NodeId);
        script.Name.ShouldBe("Script");
        script.Script.ShouldBe(new Script("Scripts/Coin.luau"));
        script.LocalTransform.ShouldBe(Transform.Identity);
    }

    [Fact]
    public void Its_file_is_made_from_the_template_and_named_after_the_parent()
    {
        AddSelectedPart("Coin");

        ScriptInsertReport report = Insert();

        report.ContentPath.ShouldBe("Scripts/Coin.luau");
        report.MadeFile.ShouldBeTrue();
        File.ReadAllText(FullPath("Scripts/Coin.luau")).ShouldBe(Template);
    }

    [Fact]
    public void The_new_script_is_what_is_selected_afterwards()
    {
        SceneNode part = AddSelectedPart("Coin");

        Insert();

        _scene.Selection.Items.ShouldBe([part.Children[0]]);
    }

    [Fact]
    public void The_status_line_says_the_file_was_made_and_where()
    {
        AddSelectedPart("Coin");

        Insert().Describe().ShouldBe("Added a script to 'Coin' and made its file, Assets/Scripts/Coin.luau.");
    }

    [Fact]
    public void With_nothing_selected_it_adds_a_level_script_under_the_root()
    {
        AddPart("Coin");

        ScriptInsertReport report = Insert();

        report.IsLevelScript.ShouldBeTrue();
        _scene.Root.Children[^1].Script.ShouldBe(new Script("Scripts/Lobby.luau"));
        report.Describe().ShouldBe("Added a level script and made its file, Assets/Scripts/Lobby.luau.");
        File.Exists(FullPath("Scripts/Lobby.luau")).ShouldBeTrue();
    }

    [Fact]
    public void With_several_nodes_selected_it_does_nothing_and_says_what_to_do()
    {
        SceneNode a = AddPart("A");
        SceneNode b = AddPart("B");
        SceneNode c = AddPart("C");
        _scene.Selection.Apply([a, b, c], SelectionUpdate.Replace);

        ScriptInsertReport report = Insert();

        report.Added.ShouldBeFalse();
        report.Describe().ShouldBe(
            "Insert script did nothing: 3 nodes are selected. Select the one the script should go under.");
        a.Children.ShouldBeEmpty();
        _host.UndoDepth.ShouldBe(0);

        // A guess at which node was meant would have left a file behind.
        Directory.Exists(FullPath("Scripts")).ShouldBeFalse();
    }

    [Fact]
    public void It_is_one_history_entry_and_undo_takes_the_node_and_leaves_the_file()
    {
        SceneNode part = AddSelectedPart("Coin");

        Insert();
        _host.UndoDepth.ShouldBe(1);

        _host.Apply(EditorHostCommand.Undo);

        part.Children.ShouldBeEmpty();
        _host.UndoDepth.ShouldBe(0);
        File.ReadAllText(FullPath("Scripts/Coin.luau")).ShouldBe(Template);
    }

    [Fact]
    public void Redo_puts_the_same_node_back_naming_the_same_file()
    {
        SceneNode part = AddSelectedPart("Coin");
        Guid id = Insert().NodeId;
        _host.Apply(EditorHostCommand.Undo);

        _host.Apply(EditorHostCommand.Redo);

        SceneNode back = part.Children.ShouldHaveSingleItem();
        back.Id.ShouldBe(id);
        back.Script.ShouldBe(new Script("Scripts/Coin.luau"));
        Directory.GetFiles(FullPath("Scripts")).Length.ShouldBe(1, "a redo makes no second file");
    }

    [Fact]
    public void The_same_insert_twice_gives_two_scripts_with_a_file_each()
    {
        SceneNode part = AddSelectedPart("Coin");

        Insert();
        _scene.Selection.Select(part);
        ScriptInsertReport second = Insert();

        second.ContentPath.ShouldBe("Scripts/Coin2.luau");
        part.Children.Select(child => child.Script.ShouldNotBeNull().Path)
            .ShouldBe(["Scripts/Coin.luau", "Scripts/Coin2.luau"]);
        _host.UndoDepth.ShouldBe(2);
    }

    [Fact]
    public void Two_parts_of_one_name_do_not_come_to_share_a_file()
    {
        SceneNode first = AddSelectedPart("Part");
        Insert();
        SceneNode second = AddSelectedPart("Part");

        Insert();

        first.Children[0].Script.ShouldNotBeNull().Path.ShouldBe("Scripts/Part.luau");
        second.Children[0].Script.ShouldNotBeNull().Path.ShouldBe("Scripts/Part2.luau");
    }

    [Fact]
    public void A_script_somebody_wrote_under_that_name_is_left_alone()
    {
        Directory.CreateDirectory(FullPath("Scripts"));
        File.WriteAllText(FullPath("Scripts/Coin.luau"), "-- an afternoon's work\n");
        AddSelectedPart("Coin");

        Insert().ContentPath.ShouldBe("Scripts/Coin2.luau");

        File.ReadAllText(FullPath("Scripts/Coin.luau")).ShouldBe("-- an afternoon's work\n");
    }

    [Theory]
    [InlineData("", "Scripts/Script.luau")]
    [InlineData("Front Door", "Scripts/Front_Door.luau")]
    [InlineData("a/b:c?", "Scripts/a_b_c.luau")]
    [InlineData("AUX", "Scripts/AUX_.luau")]
    public void A_parent_whose_name_is_no_file_name_still_gets_a_script(string name, string expected)
    {
        SceneNode part = AddSelectedPart("placeholder");
        part.Name = name;

        ScriptInsertReport report = Insert();

        report.Added.ShouldBeTrue(report.Describe());
        report.ContentPath.ShouldBe(expected);
        File.Exists(FullPath(expected)).ShouldBeTrue();
    }

    [Fact]
    public void While_the_level_plays_it_is_refused_with_the_way_out_and_makes_no_file()
    {
        SceneNode part = AddSelectedPart("Coin");
        _host.Suspend();

        ScriptInsertReport report = Insert();

        report.Added.ShouldBeFalse();
        report.Describe().ShouldBe("Insert script did nothing: the level is playing. Stop it first.");
        report.Describe().ShouldBe(_host.MakeEntity("func_door").Message.Replace("Make entity", "Insert script"));
        part.Children.ShouldBeEmpty();
        Directory.Exists(FullPath("Scripts")).ShouldBeFalse();
    }

    [Fact]
    public void When_the_file_cannot_be_written_nothing_is_added_and_the_line_says_why()
    {
        // A file where the Scripts folder should be.
        File.WriteAllText(FullPath("Scripts"), "in the way");
        SceneNode part = AddSelectedPart("Coin");

        ScriptInsertReport report = Insert();

        report.Added.ShouldBeFalse();
        report.Describe().ShouldStartWith(
            "Insert script did nothing: Assets/Scripts/Coin.luau could not be written. ");
        part.Children.ShouldBeEmpty();
        _host.UndoDepth.ShouldBe(0);
        _scene.Selection.Items.ShouldBe([part], "the selection is as it was");
    }

    [Fact]
    public void A_script_that_is_there_is_added_under_the_node_it_was_dropped_on()
    {
        SceneNode part = AddPart("Coin");
        SceneNode other = AddSelectedPart("Other");

        ScriptInsertReport report = _host.AttachScript("Scripts/Spin.luau", part.Id);

        report.Added.ShouldBeTrue(report.Describe());
        report.MadeFile.ShouldBeFalse();
        report.Describe().ShouldBe("Scripts/Spin.luau added to 'Coin' as a script.");
        part.Children.ShouldHaveSingleItem().Script.ShouldBe(new Script("Scripts/Spin.luau"));
        other.Children.ShouldBeEmpty();
        _scene.Selection.Items.ShouldBe([part.Children[0]]);
        Directory.Exists(FullPath("Scripts")).ShouldBeFalse("attaching writes nothing");
    }

    [Fact]
    public void A_dropped_script_is_one_history_entry()
    {
        SceneNode part = AddPart("Coin");

        _host.AttachScript("Scripts/Spin.luau", part.Id);
        _host.UndoDepth.ShouldBe(1);

        _host.Apply(EditorHostCommand.Undo);
        part.Children.ShouldBeEmpty();

        _host.Apply(EditorHostCommand.Redo);
        part.Children.ShouldHaveSingleItem().Script.ShouldBe(new Script("Scripts/Spin.luau"));
    }

    [Fact]
    public void Dropped_in_the_viewport_over_a_part_it_goes_under_that_part()
    {
        SceneNode part = AddPart("Coin");
        _scene.Camera.Position = new Vector3(0f, 0f, 10f);
        _scene.Camera.LookAt(Vector3.Zero);

        ScriptInsertReport report = _host.AttachScript("Scripts/Spin.luau", viewportPoint: new Vector2(640f, 360f));

        report.Added.ShouldBeTrue(report.Describe());
        report.ParentName.ShouldBe("Coin");
        part.Children.ShouldHaveSingleItem().Script.ShouldBe(new Script("Scripts/Spin.luau"));
    }

    [Fact]
    public void Dropped_in_the_viewport_over_nothing_it_becomes_a_level_script()
    {
        AddPart("Coin");
        _scene.Camera.Position = new Vector3(0f, 0f, 10f);
        _scene.Camera.LookAt(new Vector3(0f, 0f, 20f));

        ScriptInsertReport report = _host.AttachScript("Scripts/Spin.luau", viewportPoint: new Vector2(640f, 360f));

        report.IsLevelScript.ShouldBeTrue();
        report.Describe().ShouldBe("Scripts/Spin.luau added as a level script.");
        _scene.Root.Children[^1].Script.ShouldBe(new Script("Scripts/Spin.luau"));
    }

    [Fact]
    public void Dropped_on_a_lamps_icon_it_goes_under_the_lamp_and_not_the_wall_behind_it()
    {
        // A lamp is not in the spatial index. A click finds it by its icon.
        SceneNode wall = AddPart("Wall");
        wall.LocalPosition = new Vector3(0f, 0f, -5f);
        SceneNode lamp = _scene.Root.CreateChild("Lamp");
        lamp.Light = new Light { Kind = LightKind.Point, Range = 6f };
        _scene.Camera.Position = new Vector3(0f, 0f, 10f);
        _scene.Camera.LookAt(Vector3.Zero);

        ScriptInsertReport report = _host.AttachScript("Scripts/Flicker.luau", viewportPoint: new Vector2(640f, 360f));

        report.ParentName.ShouldBe("Lamp");
        lamp.Children.ShouldHaveSingleItem().Script.ShouldBe(new Script("Scripts/Flicker.luau"));
        wall.Children.ShouldBeEmpty();
    }

    [Fact]
    public void Dropped_on_a_point_entitys_icon_with_nothing_behind_it_goes_under_the_entity()
    {
        SceneNode relay = _scene.Root.CreateChild("Relay");
        relay.Entity = new EntityData("logic_relay");
        _scene.Camera.Position = new Vector3(0f, 0f, 10f);
        _scene.Camera.LookAt(Vector3.Zero);

        ScriptInsertReport report = _host.AttachScript("Scripts/Count.luau", viewportPoint: new Vector2(640f, 360f));

        report.IsLevelScript.ShouldBeFalse();
        report.Describe().ShouldBe("Scripts/Count.luau added to 'Relay' as a script.");
        relay.Children.ShouldHaveSingleItem().Script.ShouldBe(new Script("Scripts/Count.luau"));
    }

    [Fact]
    public void Dropped_on_a_part_a_long_way_off_it_still_goes_under_that_part()
    {
        // As far as a click reaches, which is further than an insert looks for a floor.
        SceneNode part = AddPart("Tower");
        _scene.Camera.Position = new Vector3(0f, 0f, 400f);
        _scene.Camera.LookAt(Vector3.Zero);

        ScriptInsertReport report = _host.AttachScript("Scripts/Spin.luau", viewportPoint: new Vector2(640f, 360f));

        report.ParentName.ShouldBe("Tower");
        part.Children.ShouldHaveSingleItem();
    }

    [Fact]
    public void With_no_file_named_nothing_is_added_and_the_line_is_a_sentence()
    {
        ScriptInsertReport report = _host.AttachScript(" ");

        report.Added.ShouldBeFalse();
        report.Describe().ShouldBe("Insert script did nothing: no file was given.");
    }

    [Fact]
    public void With_no_node_and_no_point_it_goes_by_the_selection_as_insert_does()
    {
        SceneNode part = AddSelectedPart("Coin");

        _host.AttachScript("Scripts/Spin.luau").ParentName.ShouldBe("Coin");
        part.Children.ShouldHaveSingleItem();

        _scene.Selection.Apply([part, AddPart("Other")], SelectionUpdate.Replace);
        _host.AttachScript("Scripts/Spin.luau").Describe().ShouldBe(
            "Scripts/Spin.luau was not added: 2 nodes are selected. Select the one the script should go under.");
    }

    [Fact]
    public void A_dropped_path_is_stored_the_way_a_level_stores_it()
    {
        SceneNode part = AddPart("Coin");

        _host.AttachScript(@"\Scripts\Doors\vault.luau", part.Id).ContentPath.ShouldBe("Scripts/Doors/vault.luau");
    }

    [Theory]
    [InlineData("../../elsewhere.luau")]
    [InlineData(@"C:\elsewhere\spin.luau")]
    public void A_path_outside_the_assets_folder_is_refused(string path)
    {
        SceneNode part = AddPart("Coin");

        ScriptInsertReport report = _host.AttachScript(path, part.Id);

        report.Added.ShouldBeFalse();
        report.Refused.ShouldBe("that is not a path inside the Assets folder.");
        part.Children.ShouldBeEmpty();
    }

    [Fact]
    public void A_drop_on_a_node_that_has_left_the_level_is_refused()
    {
        ScriptInsertReport report = _host.AttachScript("Scripts/Spin.luau", Guid.NewGuid());

        report.Describe().ShouldBe(
            "Scripts/Spin.luau was not added: the node it was dropped on is no longer in the level.");
        _host.UndoDepth.ShouldBe(0);
    }

    [Fact]
    public void A_drop_while_the_level_plays_is_refused_with_the_way_out()
    {
        SceneNode part = AddPart("Coin");
        _host.Suspend();

        _host.AttachScript("Scripts/Spin.luau", part.Id).Describe()
            .ShouldBe("Scripts/Spin.luau was not added: the level is playing. Stop it first.");
        part.Children.ShouldBeEmpty();
    }

    [Fact]
    public void Pressed_twice_in_a_row_the_second_script_lands_beside_the_first()
    {
        // The first insert leaves its script selected. The second must not
        // nest under it: the part gets two scripts, and each its own file.
        SceneNode part = AddSelectedPart("Coin");

        Insert();
        ScriptInsertReport second = Insert();

        second.Added.ShouldBeTrue(second.Describe());
        second.Describe().ShouldBe("Added a script to 'Coin' and made its file, Assets/Scripts/Coin2.luau.");
        part.Children.Select(child => child.Script.ShouldNotBeNull().Path)
            .ShouldBe(["Scripts/Coin.luau", "Scripts/Coin2.luau"]);
        part.Children.ShouldAllBe(child => child.Children.Count == 0);
        _scene.Selection.Items.ShouldBe([part.Children[1]]);
    }

    [Fact]
    public void With_a_level_script_selected_the_next_one_is_a_level_script_too()
    {
        Insert();

        ScriptInsertReport second = Insert();

        second.IsLevelScript.ShouldBeTrue();
        _scene.Root.Children.Select(child => child.Script.ShouldNotBeNull().Path)
            .ShouldBe(["Scripts/Lobby.luau", "Scripts/Lobby2.luau"]);
    }

    [Fact]
    public void A_script_dropped_on_a_script_goes_under_it_because_that_is_where_it_was_put()
    {
        SceneNode part = AddSelectedPart("Coin");
        SceneNode first = _scene.Root.CreateChild("Loader");
        first.Script = new Script("Scripts/Loader.luau");

        _host.AttachScript("Scripts/shared/Timer.luau", first.Id).ParentName.ShouldBe("Loader");

        first.Children.ShouldHaveSingleItem().Script.ShouldBe(new Script("Scripts/shared/Timer.luau"));
        part.Children.ShouldBeEmpty();
    }

    [Fact]
    public void A_selected_script_that_has_children_is_a_parent_and_takes_the_new_one()
    {
        SceneNode loader = _scene.Root.CreateChild("Loader");
        loader.Script = new Script("Scripts/Loader.luau");
        loader.CreateChild("Lamp");
        _scene.Selection.Select(loader);

        Insert().ParentName.ShouldBe("Loader");

        loader.Children.Count.ShouldBe(2);
    }
}
