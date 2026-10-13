using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Editing the tags and attributes a node is built with: one history entry
/// for an edit over a whole selection, undo and redo to the same sets, and
/// what the rules refuse changing nothing.
/// </summary>
public sealed class TagAndAttributeEditTests
{
    private sealed class Rig
    {
        public Rig(EntitySchemaCatalog? schemas, params SceneNode[] nodes)
        {
            Scene = new Scene("marked") { EntitySchemas = schemas };
            foreach (SceneNode node in nodes)
                Scene.Root.AddChild(node);

            Undo = new UndoStack(Scene);
            Nodes = nodes;
        }

        public Scene Scene { get; }
        public UndoStack Undo { get; }
        public IReadOnlyList<SceneNode> Nodes { get; }

        public int Apply(TagEdit edit) => TagEditor.Apply(Undo, Nodes, edit);

        public int Apply(AttributeEdit edit, bool inGesture = false) =>
            AttributeEditor.Apply(Undo, Nodes, edit, inGesture);
    }

    private static MemberValue ValueOf(SceneNode node, string name) =>
        node.Attributes.TryGet(name, out MemberValue value) ? value : MemberValue.None;

    [Fact]
    public void A_tag_goes_on_every_selected_node_as_one_entry_and_undo_takes_it_off_them_all()
    {
        var a = new SceneNode("A") { Tags = TagSet.Of("Hot") };
        var b = new SceneNode("B");
        var c = new SceneNode("C") { Tags = TagSet.Of("Lava") };
        var rig = new Rig(null, a, b, c);

        rig.Apply(new TagEdit("Lava", IsOn: true)).ShouldBe(2, "C has it already");

        a.Tags.ShouldBe(["Hot", "Lava"]);
        b.Tags.ShouldBe(["Lava"]);
        c.Tags.ShouldBe(["Lava"]);
        rig.Undo.UndoCount.ShouldBe(1);
        rig.Undo.UndoName.ShouldBe("Add Tag");

        rig.Undo.Undo();
        a.Tags.ShouldBe(["Hot"]);
        b.Tags.Count.ShouldBe(0);
        c.Tags.ShouldBe(["Lava"]);

        rig.Undo.Redo();
        a.Tags.ShouldBe(["Hot", "Lava"]);
        b.Tags.ShouldBe(["Lava"]);
    }

    [Fact]
    public void A_tag_comes_off_every_selected_node_that_has_it_and_undo_puts_it_back_in_its_place()
    {
        var a = new SceneNode("A") { Tags = TagSet.Of("Lava", "Hot", "Deep") };
        var b = new SceneNode("B") { Tags = TagSet.Of("Deep") };
        var rig = new Rig(null, a, b);

        rig.Apply(new TagEdit("Hot", IsOn: false)).ShouldBe(1);

        a.Tags.ShouldBe(["Lava", "Deep"]);
        rig.Undo.UndoName.ShouldBe("Remove Tag");

        // The order is what a script reads.
        rig.Undo.Undo();
        a.Tags.ShouldBe(["Lava", "Hot", "Deep"]);
    }

    [Fact]
    public void A_tag_every_node_has_and_one_none_has_record_nothing()
    {
        var a = new SceneNode("A") { Tags = TagSet.Of("Lava") };
        var rig = new Rig(null, a);

        rig.Apply(new TagEdit("Lava", IsOn: true)).ShouldBe(0);
        rig.Apply(new TagEdit("Cold", IsOn: false)).ShouldBe(0);

        rig.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void A_tag_the_rule_refuses_and_a_node_with_no_room_change_nothing()
    {
        var full = new SceneNode("Full")
        {
            Tags = TagSet.Of([.. Enumerable.Range(0, TagLimits.MaxPerNode).Select(i => $"Tag{i}")]),
        };
        var roomy = new SceneNode("Roomy");
        var rig = new Rig(null, full, roomy);

        rig.Apply(new TagEdit("", IsOn: true)).ShouldBe(0);
        rig.Apply(new TagEdit(new string('a', TagLimits.MaxBytes + 1), IsOn: true)).ShouldBe(0);
        rig.Undo.UndoCount.ShouldBe(0);

        // The node with room takes it. The full one is left as it is.
        rig.Apply(new TagEdit("OneMore", IsOn: true)).ShouldBe(1);
        full.Tags.Count.ShouldBe(TagLimits.MaxPerNode);
        roomy.Tags.ShouldBe(["OneMore"]);
    }

    [Fact]
    public void Setting_an_attribute_writes_it_to_every_selected_node_and_undo_restores_each_ones_own()
    {
        var a = new SceneNode("A")
        {
            Attributes = AttributeSet.Empty.With("Label", MemberValue.Of("a")).With("Damage", MemberValue.Of(10d)),
        };
        var b = new SceneNode("B") { Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(20d)) };
        var c = new SceneNode("C");
        var rig = new Rig(null, a, b, c);

        rig.Apply(AttributeEdit.Set("Damage", MemberValue.Of(25d))).ShouldBe(3);

        ValueOf(a, "Damage").ShouldBe(MemberValue.Of(25d));
        ValueOf(b, "Damage").ShouldBe(MemberValue.Of(25d));
        ValueOf(c, "Damage").ShouldBe(MemberValue.Of(25d));
        a.Attributes.Select(pair => pair.Key).ShouldBe(["Label", "Damage"]);
        rig.Undo.UndoCount.ShouldBe(1);

        rig.Undo.Undo();
        ValueOf(a, "Damage").ShouldBe(MemberValue.Of(10d));
        ValueOf(b, "Damage").ShouldBe(MemberValue.Of(20d));
        c.Attributes.Count.ShouldBe(0);

        rig.Undo.Redo();
        ValueOf(c, "Damage").ShouldBe(MemberValue.Of(25d));
    }

    [Fact]
    public void A_value_of_another_kind_replaces_the_old_one()
    {
        var a = new SceneNode("A") { Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(10d)) };
        var rig = new Rig(null, a);

        rig.Apply(AttributeEdit.Set("Damage", MemberValue.Of("a lot"))).ShouldBe(1);

        ValueOf(a, "Damage").ShouldBe(MemberValue.Of("a lot"));
    }

    [Fact]
    public void One_part_of_three_numbers_is_written_and_each_nodes_other_parts_stay()
    {
        var a = new SceneNode("A") { Attributes = AttributeSet.Empty.With("Push", MemberValue.Of(new Vector3(1f, 2f, 3f))) };
        var b = new SceneNode("B") { Attributes = AttributeSet.Empty.With("Push", MemberValue.Of(new Vector3(7f, 8f, 9f))) };
        var c = new SceneNode("C") { Attributes = AttributeSet.Empty.With("Push", MemberValue.Of("text")) };
        var d = new SceneNode("D");
        var rig = new Rig(null, a, b, c, d);

        rig.Apply(AttributeEdit.SetPart("Push", MemberValue.Of(Vector3.Zero), part: 1, number: 50f)).ShouldBe(4);

        ValueOf(a, "Push").Vector.ShouldBe(new Vector3(1f, 50f, 3f));
        ValueOf(b, "Push").Vector.ShouldBe(new Vector3(7f, 50f, 9f));
        ValueOf(c, "Push").Vector.ShouldBe(new Vector3(0f, 50f, 0f), "another kind starts from the value handed in");
        ValueOf(d, "Push").Vector.ShouldBe(new Vector3(0f, 50f, 0f));
        rig.Undo.UndoCount.ShouldBe(1);

        rig.Undo.Undo();
        ValueOf(c, "Push").ShouldBe(MemberValue.Of("text"));
        d.Attributes.Count.ShouldBe(0);
    }

    [Fact]
    public void A_frames_position_and_its_rotation_in_degrees_are_edited_one_number_at_a_time()
    {
        var a = new SceneNode("A") { Attributes = AttributeSet.Empty.With("Rest", MemberValue.Of(new CFrame(1f, 2f, 3f))) };
        var rig = new Rig(null, a);
        MemberValue from = MemberValue.Of(CFrame.Identity);

        rig.Apply(AttributeEdit.SetPart("Rest", from, part: 2, number: -4f)).ShouldBe(1);
        rig.Apply(AttributeEdit.SetPart("Rest", from, part: 4, number: 90f)).ShouldBe(1);

        CFrame rest = ValueOf(a, "Rest").Frame;
        rest.Position.ShouldBe(new Vector3(1f, 2f, -4f));
        Vector3 degrees = EulerAngles.FromQuaternion(rest.Rotation).AsDegrees;
        degrees.X.ShouldBe(0f, 1e-3f);
        degrees.Y.ShouldBe(90f, 1e-3f);
        degrees.Z.ShouldBe(0f, 1e-3f);

        rig.Undo.Undo();
        ValueOf(a, "Rest").Frame.Rotation.ShouldBe(Quaternion.Identity);
    }

    [Fact]
    public void An_attribute_comes_off_every_node_that_has_it_and_undo_puts_it_back_in_its_place()
    {
        var a = new SceneNode("A")
        {
            Attributes = AttributeSet.Empty
                .With("First", MemberValue.Of(1d)).With("Damage", MemberValue.Of(10d)).With("Last", MemberValue.Of(3d)),
        };
        var b = new SceneNode("B");
        var rig = new Rig(null, a, b);

        rig.Apply(AttributeEdit.Remove("Damage")).ShouldBe(1);
        a.Attributes.Select(pair => pair.Key).ShouldBe(["First", "Last"]);
        rig.Undo.UndoName.ShouldBe("Remove Attribute");

        rig.Undo.Undo();
        a.Attributes.Select(pair => pair.Key).ShouldBe(["First", "Damage", "Last"]);
        ValueOf(a, "Damage").ShouldBe(MemberValue.Of(10d));
    }

    [Fact]
    public void A_rename_keeps_the_value_and_the_place_and_skips_a_node_that_has_the_new_name()
    {
        var a = new SceneNode("A")
        {
            Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(10d)).With("Label", MemberValue.Of("a")),
        };
        var clash = new SceneNode("Clash")
        {
            Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(1d)).With("Harm", MemberValue.Of(2d)),
        };
        var without = new SceneNode("Without");
        var rig = new Rig(null, a, clash, without);

        rig.Apply(AttributeEdit.Rename("Damage", "Harm")).ShouldBe(1);

        a.Attributes.Select(pair => pair.Key).ShouldBe(["Harm", "Label"]);
        ValueOf(a, "Harm").ShouldBe(MemberValue.Of(10d));
        clash.Attributes.Select(pair => pair.Key).ShouldBe(["Damage", "Harm"]);
        without.Attributes.Count.ShouldBe(0);

        rig.Undo.Undo();
        a.Attributes.Select(pair => pair.Key).ShouldBe(["Damage", "Label"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("RBXHidden")]
    public void A_name_the_rule_refuses_changes_nothing(string name)
    {
        var a = new SceneNode("A") { Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(10d)) };
        var rig = new Rig(null, a);

        rig.Apply(AttributeEdit.Set(name, MemberValue.Of(1d))).ShouldBe(0);
        rig.Apply(AttributeEdit.Rename("Damage", name)).ShouldBe(0);
        rig.Apply(AttributeEdit.SetPart(name, MemberValue.Of(Vector3.Zero), 0, 1f)).ShouldBe(0);

        rig.Undo.UndoCount.ShouldBe(0);
        a.Attributes.Count.ShouldBe(1);
    }

    [Fact]
    public void A_value_the_rule_refuses_changes_nothing()
    {
        var a = new SceneNode("A") { Attributes = AttributeSet.Empty.With("Push", MemberValue.Of(Vector3.One)) };
        var rig = new Rig(null, a);

        rig.Apply(AttributeEdit.Set("Damage", MemberValue.Of(double.NaN))).ShouldBe(0);
        rig.Apply(AttributeEdit.Set("Damage", MemberValue.None)).ShouldBe(0);
        rig.Apply(AttributeEdit.SetPart("Push", MemberValue.Of(Vector3.Zero), 0, float.PositiveInfinity)).ShouldBe(0);
        rig.Apply(AttributeEdit.SetPart("Push", MemberValue.Of(Vector3.Zero), 3, 1f)).ShouldBe(0);
        rig.Apply(AttributeEdit.SetPart("Push", MemberValue.Of(1d), 0, 1f)).ShouldBe(0);

        rig.Undo.UndoCount.ShouldBe(0);
        a.Attributes.Count.ShouldBe(1);
    }

    [Fact]
    public void A_node_with_no_room_keeps_what_it_has_and_still_takes_a_new_value_for_a_name_it_has()
    {
        AttributeSet.TryMake(
            [.. Enumerable.Range(0, AttributeLimits.MaxPerNode)
                .Select(i => new KeyValuePair<string, MemberValue>($"Name{i}", MemberValue.Of((double)i)))],
            out AttributeSet? set, out _).ShouldBeTrue();
        var full = new SceneNode("Full") { Attributes = set.ShouldNotBeNull() };
        var rig = new Rig(null, full);

        rig.Apply(AttributeEdit.Set("OneMore", MemberValue.Of(1d))).ShouldBe(0);
        rig.Apply(AttributeEdit.Set("Name7", MemberValue.Of(700d))).ShouldBe(1);

        full.Attributes.Count.ShouldBe(AttributeLimits.MaxPerNode);
        ValueOf(full, "Name7").ShouldBe(MemberValue.Of(700d));
    }

    [Fact]
    public void An_entitys_keyvalue_name_is_refused_on_its_node_and_taken_on_the_others()
    {
        EntitySchemaCatalog schemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
        [
            new EntitySchema("door", keyvalues:
            [
                new("speed", "", "", "100", KeyvalueType.Float, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u,
                    KeyvalueDescriptor.NoChoices),
                new("wait", "", "", "4", KeyvalueType.Float, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u,
                    KeyvalueDescriptor.NoChoices),
            ]),
        ]));
        var door = new SceneNode("Door") { Entity = new EntityData("door") };
        door.Entity.SetValue("note", "only the node has it");
        var crate = new SceneNode("Crate") { Attributes = AttributeSet.Empty.With("Old", MemberValue.Of(1d)) };
        door.Attributes = crate.Attributes;
        var rig = new Rig(schemas, door, crate);

        // The class declares one and the node holds the other.
        rig.Apply(AttributeEdit.Set("speed", MemberValue.Of(9d))).ShouldBe(1);
        rig.Apply(AttributeEdit.Set("note", MemberValue.Of("x"))).ShouldBe(1);
        rig.Apply(AttributeEdit.Rename("Old", "wait")).ShouldBe(1);

        door.Attributes.Select(pair => pair.Key).ShouldBe(["Old"]);
        crate.Attributes.Select(pair => pair.Key).ShouldBe(["wait", "speed", "note"]);

        // A name the entity does not answer to goes on both.
        rig.Apply(AttributeEdit.Set("Score", MemberValue.Of(7d))).ShouldBe(2);
        ValueOf(door, "Score").ShouldBe(MemberValue.Of(7d));
    }

    [Fact]
    public void Edits_inside_one_gesture_are_one_entry_and_a_cancel_puts_everything_back()
    {
        var a = new SceneNode("A") { Attributes = AttributeSet.Empty.With("Glow", MemberValue.Of(new Color3(1f, 1f, 1f))) };
        var rig = new Rig(null, a);

        rig.Undo.BeginTransaction("Color");
        for (int i = 1; i <= 5; i++)
            rig.Apply(AttributeEdit.Set("Glow", MemberValue.Of(new Color3(i / 10f, 0f, 0f))), inGesture: true).ShouldBe(1);
        rig.Undo.CommitTransaction();

        rig.Undo.UndoCount.ShouldBe(1);
        ValueOf(a, "Glow").Color.ShouldBe(new Color3(0.5f, 0f, 0f));
        rig.Undo.Undo();
        ValueOf(a, "Glow").Color.ShouldBe(new Color3(1f, 1f, 1f));

        rig.Undo.BeginTransaction("Color");
        rig.Apply(AttributeEdit.Set("Glow", MemberValue.Of(new Color3(0f, 1f, 0f))), inGesture: true);
        rig.Undo.CancelTransaction();
        ValueOf(a, "Glow").Color.ShouldBe(new Color3(1f, 1f, 1f));
    }

    [Fact]
    public void An_edit_to_a_node_that_left_the_scene_is_a_no_op_on_redo()
    {
        var a = new SceneNode("A");
        var rig = new Rig(null, a);
        rig.Apply(new TagEdit("Lava", IsOn: true));
        rig.Apply(AttributeEdit.Set("Damage", MemberValue.Of(1d)));
        rig.Undo.Undo();
        rig.Undo.Undo();

        rig.Scene.Root.RemoveChild(a);
        rig.Undo.Redo();
        rig.Undo.Redo();

        a.Tags.Count.ShouldBe(0);
        a.Attributes.Count.ShouldBe(0);
    }
}
