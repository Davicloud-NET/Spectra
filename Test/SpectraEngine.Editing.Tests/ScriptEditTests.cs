using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editing.Undo;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Writing a script's file, its enabled switch and its module switch: the
/// command and the property editor path over it.
/// </summary>
public sealed class ScriptEditTests
{
    private static SceneNode ScriptNode(string name, string path = "Scripts/door.luau") =>
        new(name) { Script = new Script(path) };

    private sealed class Rig
    {
        public Rig(params SceneNode[] nodes)
        {
            Scene = new Scene("scripts");
            foreach (SceneNode node in nodes)
                Scene.Root.AddChild(node);

            Undo = new UndoStack(Scene);
            Nodes = nodes;
        }

        public Scene Scene { get; }
        public UndoStack Undo { get; }
        public IReadOnlyList<SceneNode> Nodes { get; }

        public int Apply(PropertyId id, bool flag) =>
            PropertyEditor.Apply(Undo, Nodes, new PropertyEdit { Id = id, Flag = flag });

        public int ApplyFile(string path) =>
            PropertyEditor.Apply(Undo, Nodes, new PropertyEdit { Id = PropertyId.ScriptFile, Text = path });
    }

    [Fact]
    public void A_new_file_is_written_and_undo_and_redo_walk_it_both_ways()
    {
        SceneNode node = ScriptNode("opener");
        var rig = new Rig(node);

        rig.ApplyFile("Scripts/gate.luau").ShouldBe(1);
        node.Script.ShouldBe(new Script("Scripts/gate.luau"));
        rig.Undo.UndoName.ShouldBe("Script");

        rig.Undo.Undo().ShouldBeTrue();
        node.Script.ShouldBe(new Script("Scripts/door.luau"));

        rig.Undo.Redo().ShouldBeTrue();
        node.Script.ShouldBe(new Script("Scripts/gate.luau"));
    }

    [Fact]
    public void A_typed_path_is_stored_as_the_path_every_asset_is_known_by()
    {
        SceneNode node = ScriptNode("opener");
        var rig = new Rig(node);

        rig.ApplyFile(@"\Scripts\Doors\vault.luau ").ShouldBe(1);

        node.Script.ShouldNotBeNull().Path.ShouldBe("Scripts/Doors/vault.luau");
    }

    [Fact]
    public void Switching_a_script_off_is_written_and_undo_and_redo_walk_it_both_ways()
    {
        SceneNode node = ScriptNode("opener");
        var rig = new Rig(node);

        rig.Apply(PropertyId.ScriptEnabled, false).ShouldBe(1);
        node.Script.ShouldNotBeNull().IsEnabled.ShouldBeFalse();

        rig.Undo.Undo().ShouldBeTrue();
        node.Script.ShouldNotBeNull().IsEnabled.ShouldBeTrue();

        rig.Undo.Redo().ShouldBeTrue();
        node.Script.ShouldNotBeNull().IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Making_a_script_a_module_is_written_and_undo_and_redo_walk_it_both_ways()
    {
        SceneNode node = ScriptNode("shared");
        var rig = new Rig(node);

        rig.Apply(PropertyId.ScriptModule, true).ShouldBe(1);
        node.Script.ShouldNotBeNull().IsModule.ShouldBeTrue();

        rig.Undo.Undo().ShouldBeTrue();
        node.Script.ShouldNotBeNull().IsModule.ShouldBeFalse();

        rig.Undo.Redo().ShouldBeTrue();
        node.Script.ShouldNotBeNull().IsModule.ShouldBeTrue();
    }

    [Fact]
    public void Each_row_writes_its_own_value_and_no_other()
    {
        SceneNode node = ScriptNode("opener");
        var rig = new Rig(node);

        rig.Apply(PropertyId.ScriptEnabled, false).ShouldBe(1);
        node.Script.ShouldBe(new Script("Scripts/door.luau") { IsEnabled = false });

        rig.Apply(PropertyId.ScriptModule, true).ShouldBe(1);
        node.Script.ShouldBe(new Script("Scripts/door.luau") { IsEnabled = false, IsModule = true });

        rig.ApplyFile("Scripts/gate.luau").ShouldBe(1);
        node.Script.ShouldBe(new Script("Scripts/gate.luau") { IsEnabled = false, IsModule = true });
    }

    [Fact]
    public void Undo_restores_the_script_the_node_had_rather_than_a_new_one()
    {
        SceneNode node = ScriptNode("shared");
        node.Script = new Script("Scripts/shared.luau") { IsEnabled = false, IsModule = true };
        Script authored = node.Script;
        var rig = new Rig(node);

        rig.ApplyFile("Scripts/other.luau").ShouldBe(1);
        rig.Undo.Undo().ShouldBeTrue();

        node.Script.ShouldBeSameAs(authored);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../outside.luau")]
    [InlineData(@"C:\Game\Assets\Scripts\door.luau")]
    public void A_path_that_names_no_file_under_the_content_root_is_refused(string path)
    {
        // A script has to name a file, so the node keeps the one it has.
        SceneNode node = ScriptNode("opener");
        var rig = new Rig(node);

        rig.ApplyFile(path).ShouldBe(0);

        node.Script.ShouldBe(new Script("Scripts/door.luau"));
        rig.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void A_commit_that_changes_nothing_records_nothing()
    {
        // The panel commits on blur, so tabbing through the rows lands here.
        var rig = new Rig(ScriptNode("opener"));

        rig.ApplyFile("Scripts/door.luau").ShouldBe(0);
        rig.ApplyFile(@"Scripts\door.luau").ShouldBe(0, "the same file, spelled with the other slash");
        rig.Apply(PropertyId.ScriptEnabled, true).ShouldBe(0);
        rig.Apply(PropertyId.ScriptModule, false).ShouldBe(0);

        rig.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void One_edit_over_many_scripts_is_one_undo_entry_and_skips_those_that_hold_the_value()
    {
        SceneNode a = ScriptNode("a", "Scripts/a.luau");
        SceneNode b = ScriptNode("b", "Scripts/b.luau");
        SceneNode off = ScriptNode("off", "Scripts/off.luau");
        off.Script = off.Script.ShouldNotBeNull() with { IsEnabled = false };
        var rig = new Rig(a, b, off);

        rig.Apply(PropertyId.ScriptEnabled, false).ShouldBe(2);
        rig.Undo.UndoCount.ShouldBe(1);

        rig.Undo.Undo().ShouldBeTrue();
        a.Script.ShouldNotBeNull().IsEnabled.ShouldBeTrue();
        b.Script.ShouldNotBeNull().IsEnabled.ShouldBeTrue();
        off.Script.ShouldNotBeNull().IsEnabled.ShouldBeFalse();
        a.Script.ShouldNotBeNull().Path.ShouldBe("Scripts/a.luau");
        b.Script.ShouldNotBeNull().Path.ShouldBe("Scripts/b.luau");
    }

    [Fact]
    public void A_node_with_no_script_is_left_alone()
    {
        var group = new SceneNode("group");
        var lamp = new SceneNode("lamp") { Light = new Light() };
        var rig = new Rig(group, lamp);

        rig.ApplyFile("Scripts/door.luau").ShouldBe(0);
        rig.Apply(PropertyId.ScriptEnabled, false).ShouldBe(0);
        rig.Apply(PropertyId.ScriptModule, true).ShouldBe(0);

        group.Script.ShouldBeNull();
        lamp.Script.ShouldBeNull();
        rig.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void The_command_puts_a_script_on_a_node_and_undo_takes_it_off_again()
    {
        var node = new SceneNode("opener");
        var rig = new Rig(node);
        var kinds = new List<SceneNodeKind>();
        rig.Scene.NodeKindChanged += changed => kinds.Add(SceneNodeClassifier.Classify(changed));

        rig.Undo.Execute(SetScriptCommand.Capture(node, new Script("Scripts/door.luau")));
        node.Script.ShouldBe(new Script("Scripts/door.luau"));
        rig.Scene.ScriptNodes.ShouldBe([node]);

        rig.Undo.Undo().ShouldBeTrue();
        node.Script.ShouldBeNull();
        rig.Scene.ScriptNodes.ShouldBeEmpty();

        rig.Undo.Redo().ShouldBeTrue();
        rig.Scene.ScriptNodes.ShouldBe([node]);

        kinds.ShouldBe([SceneNodeKind.Script, SceneNodeKind.Empty, SceneNodeKind.Script]);
    }

    [Fact]
    public void Cancelling_restores_a_node_that_left_the_scene_during_the_gesture()
    {
        SceneNode node = ScriptNode("opener");
        var rig = new Rig(node);

        rig.Undo.BeginTransaction("Script");
        rig.Undo.Execute(SetScriptCommand.Capture(node, new Script("Scripts/gate.luau")));
        node.Script.ShouldNotBeNull().Path.ShouldBe("Scripts/gate.luau");

        rig.Scene.Root.RemoveChild(node);
        rig.Undo.CancelTransaction();

        node.Script.ShouldBe(new Script("Scripts/door.luau"));
        rig.Undo.Count.ShouldBe(0);
    }

    [Fact]
    public void An_ordinary_undo_ignores_a_node_that_is_not_in_the_scene()
    {
        SceneNode node = ScriptNode("opener");
        var rig = new Rig(node);
        rig.Apply(PropertyId.ScriptEnabled, false).ShouldBe(1);

        rig.Scene.Root.RemoveChild(node);
        rig.Undo.Undo().ShouldBeTrue();

        node.Script.ShouldNotBeNull().IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void A_missing_target_is_a_no_op_rather_than_a_throw()
    {
        // History behind an undone delete names absent nodes.
        var scene = new Scene("scripts");
        var command = new SetScriptCommand(Guid.NewGuid(), null, new Script("Scripts/door.luau"));

        Should.NotThrow(() => command.Do(scene));
        Should.NotThrow(() => command.Undo(scene));
        Should.NotThrow(() => command.RollBack(scene));
    }

    [Fact]
    public void The_command_follows_a_node_rebuilt_under_the_same_id()
    {
        // Undo of a delete recreates the node, so the id is all that lasts.
        SceneNode node = ScriptNode("opener");
        var rig = new Rig(node);
        rig.Apply(PropertyId.ScriptModule, true).ShouldBe(1);

        rig.Scene.Root.RemoveChild(node);
        var rebuilt = new SceneNode("opener", node.Id)
        {
            Script = new Script("Scripts/door.luau") { IsModule = true },
        };
        rig.Scene.Root.AddChild(rebuilt);

        rig.Undo.Undo().ShouldBeTrue();

        rebuilt.Script.ShouldNotBeNull().IsModule.ShouldBeFalse();
    }

    [Fact]
    public void An_edit_through_the_editor_reaches_the_selection_as_one_undo_entry()
    {
        var scene = new Scene("Editor");
        SceneNode opener = ScriptNode("opener");
        scene.Root.AddChild(opener);
        var renderer = new CompilingRenderer();
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));
        var host = new SceneEditorHost(
            NullLoggerFactory.Instance, scene, renderer,
            new InputManager(NullLogger<InputManager>.Instance));
        scene.Selection.Select(opener);

        host.ApplyProperty(new PropertyEdit { Id = PropertyId.ScriptFile, Text = "Scripts/gate.luau" }).ShouldBe(1);

        opener.Script.ShouldNotBeNull().Path.ShouldBe("Scripts/gate.luau");
        host.UndoDepth.ShouldBe(1);

        host.Apply(EditorHostCommand.Undo);
        opener.Script.ShouldNotBeNull().Path.ShouldBe("Scripts/door.luau");
    }
}
