using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Tests;

/// <summary>Writing an entity keyvalue: the command and the property editor path over it.</summary>
public sealed class EntityKeyvalueEditTests
{
    private static EntitySchemaCatalog Catalog(params EntitySchema[] schemas) =>
        EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(schemas));

    private static KeyvalueDescriptor Kv(string name, KeyvalueType type, string value = "") =>
        new(name, "", "", value, type, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u,
            KeyvalueDescriptor.NoChoices);

    private sealed class Rig
    {
        public Rig(EntitySchemaCatalog? schemas, params SceneNode[] nodes)
        {
            Scene = new Scene("entities") { EntitySchemas = schemas };
            foreach (SceneNode node in nodes)
                Scene.Root.AddChild(node);

            Undo = new UndoStack(Scene);
            Nodes = nodes;
        }

        public Scene Scene { get; }
        public UndoStack Undo { get; }
        public IReadOnlyList<SceneNode> Nodes { get; }

        public int Apply(PropertyEdit edit, bool inGesture = false) =>
            PropertyEditor.Apply(Undo, Nodes, edit, inGesture);
    }

    private static SceneNode Placed(string className, params (string Key, string Value)[] keyvalues)
    {
        var data = new EntityData(className);
        foreach ((string key, string value) in keyvalues)
            data.SetValue(key, value);

        return new SceneNode(className) { Entity = data };
    }

    private static string? Stored(SceneNode node, string key) =>
        node.Entity is { } entity && entity.TryGetValue(key, out string value) ? value : null;

    private static PropertyEdit Write(string key, string text, PropertyAxes axes = PropertyAxes.All) =>
        new() { Id = PropertyId.EntityKeyvalue, Key = key, Text = text, Axes = axes };

    [Fact]
    public void Undo_restores_the_exact_string()
    {
        SceneNode node = Placed("thing", ("wait", "1.50"));
        var rig = new Rig(null, node);

        rig.Apply(Write("wait", "9")).ShouldBe(1);
        Stored(node, "wait").ShouldBe("9");

        rig.Undo.Undo();
        Stored(node, "wait").ShouldBe("1.50");

        rig.Undo.Redo();
        Stored(node, "wait").ShouldBe("9");
    }

    [Fact]
    public void Undoing_an_added_key_removes_it_again()
    {
        // Restoring it as "" would leave a member in map.json nobody wrote.
        SceneNode node = Placed("thing");
        var rig = new Rig(null, node);

        rig.Apply(Write("wait", "3")).ShouldBe(1);
        node.Entity!.Keyvalues.Count.ShouldBe(1);

        rig.Undo.Undo();
        node.Entity!.Keyvalues.Count.ShouldBe(0);
    }

    [Fact]
    public void An_edited_key_keeps_its_place_in_the_authored_order()
    {
        // Keyvalue order is the file's order.
        SceneNode node = Placed("thing", ("a", "1"), ("b", "2"), ("c", "3"));
        var rig = new Rig(null, node);

        rig.Apply(Write("b", "99"));

        node.Entity!.Keyvalues[0].Key.ShouldBe("a");
        node.Entity!.Keyvalues[1].Key.ShouldBe("b");
        node.Entity!.Keyvalues[1].Value.ShouldBe("99");
        node.Entity!.Keyvalues[2].Key.ShouldBe("c");
    }

    [Fact]
    public void A_missing_target_is_a_no_op_rather_than_a_throw()
    {
        // History behind an undone delete names absent nodes.
        var scene = new Scene("entities");
        var command = new SetEntityKeyvalueCommand(Guid.NewGuid(), "wait", "1", "2");

        Should.NotThrow(() => command.Do(scene));
        Should.NotThrow(() => command.Undo(scene));
        Should.NotThrow(() => command.RollBack(scene));
    }

    [Fact]
    public void A_node_that_lost_its_entity_is_a_no_op_too()
    {
        SceneNode node = Placed("thing", ("wait", "1"));
        var scene = new Scene("entities");
        scene.Root.AddChild(node);

        var command = SetEntityKeyvalueCommand.Capture(node, "wait", "2");
        node.Entity = null;

        Should.NotThrow(() => command.Do(scene));
    }

    [Fact]
    public void Capturing_from_a_node_with_no_entity_is_refused_at_the_call()
    {
        Should.Throw<InvalidOperationException>(
            () => SetEntityKeyvalueCommand.Capture(new SceneNode("plain"), "wait", "1"));
    }

    [Fact]
    public void An_empty_key_is_refused_where_the_command_is_built()
    {
        Should.Throw<ArgumentException>(
            () => new SetEntityKeyvalueCommand(Guid.NewGuid(), "", "a", "b"));
    }

    [Fact]
    public void A_drag_over_one_key_is_one_history_entry_spanning_the_gesture()
    {
        SceneNode node = Placed("thing", ("speed", "1"));
        var rig = new Rig(null, node);

        rig.Undo.BeginTransaction("Entity Property");
        rig.Apply(Write("speed", "2"), inGesture: true);
        rig.Apply(Write("speed", "3"), inGesture: true);
        rig.Apply(Write("speed", "4"), inGesture: true);
        rig.Undo.CommitTransaction();

        rig.Undo.UndoCount.ShouldBe(1);
        Stored(node, "speed").ShouldBe("4");

        rig.Undo.Undo();
        Stored(node, "speed").ShouldBe("1");
    }

    [Fact]
    public void A_drag_never_absorbs_an_edit_to_a_different_key()
    {
        var first = new SetEntityKeyvalueCommand(Guid.NewGuid(), "speed", "1", "2");
        var other = new SetEntityKeyvalueCommand(first.NodeId, "range", "5", "6");

        first.TryAbsorb(other).ShouldBeFalse();
        first.After.ShouldBe("2");
    }

    [Fact]
    public void Two_edits_to_two_keys_stay_two_values_inside_one_transaction()
    {
        SceneNode node = Placed("thing", ("a", "1"), ("b", "2"));
        var rig = new Rig(null, node);

        rig.Undo.BeginTransaction("Entity Property");
        rig.Apply(Write("a", "10"), inGesture: true);
        rig.Apply(Write("b", "20"), inGesture: true);
        rig.Undo.CommitTransaction();

        Stored(node, "a").ShouldBe("10");
        Stored(node, "b").ShouldBe("20");

        rig.Undo.Undo();
        Stored(node, "a").ShouldBe("1");
        Stored(node, "b").ShouldBe("2");
    }

    [Fact]
    public void An_edit_that_produces_the_stored_string_records_nothing()
    {
        SceneNode node = Placed("thing", ("wait", "1.50"));
        var rig = new Rig(null, node);

        rig.Apply(Write("wait", "1.50")).ShouldBe(0);
        rig.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Writing_the_declared_default_onto_an_absent_key_records_nothing()
    {
        // The panel shows defaults for absent keys, so blurring an untouched
        // field commits the default. That must not add the key to the map.
        EntitySchemaCatalog catalog = Catalog(
            new EntitySchema("thing", keyvalues: [Kv("speed", KeyvalueType.Float, "100")]));

        SceneNode node = Placed("thing");
        var rig = new Rig(catalog, node);

        rig.Apply(Write("speed", "100")).ShouldBe(0);
        node.Entity!.Keyvalues.Count.ShouldBe(0);

        rig.Apply(Write("speed", "120")).ShouldBe(1);
        Stored(node, "speed").ShouldBe("120");
    }

    [Fact]
    public void An_edit_with_no_key_writes_nothing()
    {
        SceneNode node = Placed("thing", ("wait", "1"));
        var rig = new Rig(null, node);

        rig.Apply(new PropertyEdit { Id = PropertyId.EntityKeyvalue, Text = "9" }).ShouldBe(0);
        Stored(node, "wait").ShouldBe("1");
    }

    [Fact]
    public void A_node_with_no_entity_is_skipped_by_a_bulk_edit()
    {
        SceneNode entity = Placed("thing", ("wait", "1"));
        var plain = new SceneNode("plain");
        var rig = new Rig(null, entity, plain);

        rig.Apply(Write("wait", "5")).ShouldBe(1);
        Stored(entity, "wait").ShouldBe("5");
    }

    [Fact]
    public void A_classname_row_is_never_written_back()
    {
        SceneNode node = Placed("thing");
        var rig = new Rig(null, node);

        rig.Apply(new PropertyEdit { Id = PropertyId.EntityClassname, Text = "other" }).ShouldBe(0);
        node.Entity!.ClassName.ShouldBe("thing");
    }

    [Fact]
    public void A_per_axis_edit_leaves_the_other_components_spelled_as_they_were()
    {
        // Odd spellings on purpose: a parse and re-format would normalise them.
        SceneNode node = Placed("thing", ("offset", "1.0 2 +3"));
        var rig = new Rig(null, node);

        rig.Apply(Write("offset", "0 9 0", PropertyAxes.Y)).ShouldBe(1);

        Stored(node, "offset").ShouldBe("1.0 9 +3");
    }

    [Fact]
    public void A_per_axis_edit_preserves_the_interior_whitespace()
    {
        SceneNode node = Placed("thing", ("offset", "1   2   3"));
        var rig = new Rig(null, node);

        rig.Apply(Write("offset", "0 0 7", PropertyAxes.Z));

        Stored(node, "offset").ShouldBe("1   2   7");
    }

    [Fact]
    public void A_bulk_per_axis_edit_reaches_each_nodes_own_other_components()
    {
        SceneNode a = Placed("thing", ("offset", "1 5 1"));
        SceneNode b = Placed("thing", ("offset", "2 6 2"));
        var rig = new Rig(null, a, b);

        rig.Apply(Write("offset", "0 0 0", PropertyAxes.Y)).ShouldBe(2);

        Stored(a, "offset").ShouldBe("1 0 1");
        Stored(b, "offset").ShouldBe("2 0 2");
    }

    [Fact]
    public void A_full_mask_writes_the_text_whole()
    {
        SceneNode node = Placed("thing", ("offset", "1 2 3"));
        var rig = new Rig(null, node);

        rig.Apply(Write("offset", "4 5 6"));

        Stored(node, "offset").ShouldBe("4 5 6");
    }

    [Fact]
    public void A_value_with_no_three_components_takes_the_edit_whole()
    {
        SceneNode malformed = Placed("thing", ("offset", "wide"));
        SceneNode absent = Placed("thing");
        var rig = new Rig(null, malformed, absent);

        rig.Apply(Write("offset", "7 8 9", PropertyAxes.X)).ShouldBe(2);

        Stored(malformed, "offset").ShouldBe("7 8 9");
        Stored(absent, "offset").ShouldBe("7 8 9");
    }

    [Fact]
    public void A_per_axis_edit_that_changes_nothing_records_nothing()
    {
        SceneNode node = Placed("thing", ("offset", "1 2 3"));
        var rig = new Rig(null, node);

        rig.Apply(Write("offset", "0 2 0", PropertyAxes.Y)).ShouldBe(0);
        rig.Undo.UndoCount.ShouldBe(0);
    }
}
