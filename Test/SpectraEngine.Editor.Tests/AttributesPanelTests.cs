using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editor.Shell;
using System.Numerics;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The Attributes section of the Properties panel: a row of name, kind and
/// value for each attribute of the selection, a line that adds one, and what
/// the rules refuse shown where it was typed.
/// </summary>
public sealed class AttributesPanelTests
{
    private static SceneNode Node(string name, params (string Name, MemberValue Value)[] attributes)
    {
        AttributeSet set = AttributeSet.Empty;
        foreach ((string attribute, MemberValue value) in attributes)
            set = set.With(attribute, value);

        return new SceneNode(name) { Attributes = set };
    }

    private static MemberValue ValueOf(SceneNode node, string name) =>
        node.Attributes.TryGet(name, out MemberValue value) ? value : MemberValue.None;

    private static SceneNode OneOfEach() => Node(
        "Brick",
        ("Label", MemberValue.Of("hot")),
        ("Damage", MemberValue.Of(0.1d)),
        ("Deadly", MemberValue.Of(true)),
        ("Push", MemberValue.Of(new Vector3(0f, 12.5f, -1f))),
        ("Glow", MemberValue.Of(new Color3(1f, 0.5f, 0f))),
        ("Rest", MemberValue.Of(new CFrame(new Vector3(1f, 2f, 3f), EulerAngles.FromDegrees(new Vector3(0f, 90f, 0f)).ToQuaternion()))));

    [Fact]
    public void With_nothing_selected_there_is_no_row()
    {
        var rig = new TagPanelRig(null);

        rig.Attributes.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void One_node_shows_a_row_for_each_attribute_with_the_editor_of_its_kind()
    {
        var rig = new TagPanelRig(null, OneOfEach());

        rig.Attributes.Rows.Select(row => row.Name).ShouldBe(["Label", "Damage", "Deadly", "Push", "Glow", "Rest"]);
        rig.Attributes.Rows.Select(row => row.KindLabel).ShouldBe(
            ["Text", "Number", "True or false", "Three numbers", "Color", "Position and rotation"]);

        rig.Row("Label").IsOneCell.ShouldBeTrue();
        rig.Row("Label").Cells.Single().Text.ShouldBe("hot");
        rig.Row("Damage").Cells.Single().Text.ShouldBe("0.1");
        rig.Row("Deadly").IsBoolean.ShouldBeTrue();
        rig.Row("Deadly").Flag.ShouldBe(true);
        rig.Row("Push").HasParts.ShouldBeTrue();
        rig.Row("Push").Cells.Select(cell => cell.Text).ShouldBe(["0", "12.5", "-1"]);
        rig.Row("Push").Cells.Select(cell => cell.Label).ShouldBe(["x", "y", "z"]);
        rig.Row("Glow").IsColor.ShouldBeTrue();
        rig.Row("Glow").Cells.Single().Text.ShouldBe("#FF8000");
        rig.Row("Rest").IsFrame.ShouldBeTrue();
        rig.Row("Rest").Cells.Select(cell => cell.Text).ShouldBe(["1", "2", "3"]);
        rig.Row("Rest").TurnCells.Select(cell => cell.Text).ShouldBe(["0", "90", "0"]);
        rig.Attributes.Rows.ShouldAllBe(row => !row.IsPartial && !row.HasRejection && !row.IsTakenByEntity);
    }

    [Theory]
    [InlineData("Text", MemberValueKind.Text)]
    [InlineData("Number", MemberValueKind.Number)]
    [InlineData("True or false", MemberValueKind.Boolean)]
    [InlineData("Three numbers", MemberValueKind.Vector3)]
    [InlineData("Color", MemberValueKind.Color)]
    [InlineData("Position and rotation", MemberValueKind.CFrame)]
    public void A_new_attribute_gets_its_name_and_the_kind_picked_beside_it(string kindLabel, MemberValueKind kind)
    {
        SceneNode brick = Node("Brick");
        var rig = new TagPanelRig(null, brick);

        rig.Attributes.NewKindLabel = kindLabel;
        rig.Type(rig.Attributes.NewNameField, "Fresh");

        ValueOf(brick, "Fresh").Kind.ShouldBe(kind);
        rig.Row("Fresh").KindLabel.ShouldBe(kindLabel);
        rig.Attributes.NewNameField.Text.ShouldBe("");
        rig.Attributes.NewKindLabel.ShouldBe(kindLabel, "the kind stays for the next one");
        rig.Undo.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void The_Add_button_adds_what_the_name_field_holds()
    {
        SceneNode brick = Node("Brick");
        var rig = new TagPanelRig(null, brick);

        rig.Attributes.NewNameField.Text = "Score";
        rig.Attributes.AddTyped();

        ValueOf(brick, "Score").ShouldBe(MemberValue.Of(""));
    }

    [Theory]
    [InlineData("two words", "Not added: 'two words' is not a valid attribute name. An attribute's name is 1 to 100 letters, digits and any of _ . - /, and does not start with RBX.")]
    [InlineData("RBXHidden", "Not added: 'RBXHidden' is not a valid attribute name. An attribute's name is 1 to 100 letters, digits and any of _ . - /, and does not start with RBX.")]
    [InlineData("Damage", "Not added: There is an attribute called 'Damage' already.")]
    public void A_name_the_rules_refuse_is_shown_as_wrong_where_it_was_typed_and_is_not_added(
        string name, string reason)
    {
        SceneNode brick = Node("Brick", ("Damage", MemberValue.Of(1d)));
        var rig = new TagPanelRig(null, brick);

        rig.Type(rig.Attributes.NewNameField, name);

        rig.AttributeEdits.ShouldBeEmpty();
        brick.Attributes.Count.ShouldBe(1);
        rig.Attributes.NewNameField.Rejection.ShouldBe(reason);
        rig.Attributes.NewNameField.Text.ShouldBe(name);
    }

    // Roblox takes these in a name, so the panel does. Nothing reads a name
    // as a path: a row is found by the whole name.
    [Theory]
    [InlineData("a.b")]
    [InlineData("a-b")]
    [InlineData("a/b")]
    [InlineData("ui/hud.title-2")]
    [InlineData("9lives")]
    public void A_name_with_a_period_a_hyphen_or_a_slash_is_added_shown_edited_and_renamed_like_any_other(string name)
    {
        SceneNode brick = Node("Brick", ("Damage", MemberValue.Of(1d)));
        var rig = new TagPanelRig(null, brick);

        rig.Attributes.NewKindLabel = "Number";
        rig.Type(rig.Attributes.NewNameField, name);

        rig.Attributes.NewNameField.Rejection.ShouldBeEmpty();
        rig.Attributes.Rows.Select(row => row.Name).ShouldBe(["Damage", name]);
        rig.Row(name).NameField.Text.ShouldBe(name);

        rig.Type(rig.Row(name).Cells.Single(), "7");
        ValueOf(brick, name).ShouldBe(MemberValue.Of(7d));

        rig.Type(rig.Row("Damage").NameField, name);
        rig.Row("Damage").NameField.Rejection.ShouldBe($"Not renamed: There is an attribute called '{name}' already.");

        rig.Type(rig.Row(name).NameField, name + "/x.y-z");
        brick.Attributes.Select(pair => pair.Key).ShouldBe(["Damage", name + "/x.y-z"]);
        ValueOf(brick, name + "/x.y-z").ShouldBe(MemberValue.Of(7d));

        rig.Attributes.Remove(rig.Row(name + "/x.y-z"));
        rig.Publish();
        brick.Attributes.Select(pair => pair.Key).ShouldBe(["Damage"]);
    }

    [Fact]
    public void A_node_with_no_room_refuses_one_more_attribute_with_the_reason()
    {
        AttributeSet.TryMake(
            [.. Enumerable.Range(0, AttributeLimits.MaxPerNode)
                .Select(i => new KeyValuePair<string, MemberValue>($"Name{i}", MemberValue.Of((double)i)))],
            out AttributeSet? set, out _).ShouldBeTrue();
        var rig = new TagPanelRig(null, new SceneNode("Full") { Attributes = set.ShouldNotBeNull() });

        rig.Type(rig.Attributes.NewNameField, "OneMore");

        rig.Attributes.NewNameField.Rejection.ShouldBe("Not added: A node holds at most 256 attributes.");
        rig.AttributeEdits.ShouldBeEmpty();
    }

    [Fact]
    public void A_number_is_written_as_typed_and_text_that_is_no_number_is_refused_in_place()
    {
        SceneNode brick = Node("Brick", ("Damage", MemberValue.Of(25d)));
        var rig = new TagPanelRig(null, brick);
        PropertyFieldModel cell = rig.Row("Damage").Cells.Single();

        rig.Type(cell, "a lot");
        cell.Rejection.ShouldBe("Not applied: expected a number.");
        rig.Row("Damage").HasRejection.ShouldBeTrue();
        cell.Text.ShouldBe("25", "the cell shows what the node still holds");
        rig.AttributeEdits.ShouldBeEmpty();

        // A double, digit for digit: a script reads what the cell shows.
        rig.Type(cell, "0.123456789012345");
        ValueOf(brick, "Damage").Number.ShouldBe(0.123456789012345d);
        rig.Row("Damage").Cells.Single().Text.ShouldBe("0.123456789012345");
        rig.Row("Damage").HasRejection.ShouldBeFalse();
    }

    [Fact]
    public void Text_a_checkbox_and_a_colour_are_each_written_whole()
    {
        SceneNode brick = OneOfEach();
        var rig = new TagPanelRig(null, brick);

        rig.Type(rig.Row("Label").Cells.Single(), "cold");
        ValueOf(brick, "Label").ShouldBe(MemberValue.Of("cold"));

        rig.Row("Deadly").Flag = false;
        rig.Publish();
        ValueOf(brick, "Deadly").ShouldBe(MemberValue.Of(false));

        rig.Type(rig.Row("Glow").Cells.Single(), "#0080ff");
        ValueOf(brick, "Glow").Color.ShouldBe(Color3.FromRgb(0f, 128f, 255f));
        rig.Row("Glow").Cells.Single().Text.ShouldBe("#0080FF");

        rig.Type(rig.Row("Glow").Cells.Single(), "blue");
        rig.Row("Glow").Rejection.ShouldBe("Not applied: expected #RRGGBB.");
        ValueOf(brick, "Glow").Color.ShouldBe(Color3.FromRgb(0f, 128f, 255f));
    }

    [Fact]
    public void Text_may_be_emptied()
    {
        SceneNode brick = Node("Brick", ("Label", MemberValue.Of("hot")));
        var rig = new TagPanelRig(null, brick);

        rig.Type(rig.Row("Label").Cells.Single(), "");

        ValueOf(brick, "Label").ShouldBe(MemberValue.Of(""));
    }

    // A node keeps text as UTF-8, and the cell shows it as the text it is.
    [Fact]
    public void Text_outside_ASCII_is_shown_as_it_is_and_written_as_typed()
    {
        const string Built = "hei\u00df \u20ac\U0001F600";
        const string Typed = "\u65e5\u672c\u8a9e, more than fits in a value \U0001F600";
        SceneNode brick = Node("Brick", ("Label", MemberValue.Of(Built)));
        var rig = new TagPanelRig(null, brick);

        rig.Row("Label").Cells.Single().Text.ShouldBe(Built);

        rig.Type(rig.Row("Label").Cells.Single(), Typed);

        ValueOf(brick, "Label").ShouldBe(MemberValue.Of(Typed));
        ValueOf(brick, "Label").IsUtf8.ShouldBeTrue();
        rig.Row("Label").Cells.Single().Text.ShouldBe(Typed);
    }

    [Fact]
    public void One_cell_of_three_numbers_and_of_a_frame_writes_that_number_alone()
    {
        SceneNode brick = OneOfEach();
        var rig = new TagPanelRig(null, brick);

        rig.Type(rig.Row("Push").Cells[2], "7.5");
        ValueOf(brick, "Push").Vector.ShouldBe(new Vector3(0f, 12.5f, 7.5f));

        rig.Type(rig.Row("Rest").Cells[0], "-4");
        rig.Type(rig.Row("Rest").TurnCells[1], "45");
        CFrame rest = ValueOf(brick, "Rest").Frame;
        rest.Position.ShouldBe(new Vector3(-4f, 2f, 3f));
        EulerAngles.FromQuaternion(rest.Rotation).AsDegrees.Y.ShouldBe(45f, 1e-3f);
        rig.Row("Rest").TurnCells.Select(cell => cell.Text).ShouldBe(["0", "45", "0"]);

        rig.Type(rig.Row("Push").Cells[0], "far");
        rig.Row("Push").Rejection.ShouldBe("Not applied: expected a number for x.");
        ValueOf(brick, "Push").Vector.X.ShouldBe(0f);
    }

    [Fact]
    public void Picking_another_kind_gives_the_attribute_that_kinds_starting_value()
    {
        SceneNode brick = Node("Brick", ("Damage", MemberValue.Of(25d)));
        var rig = new TagPanelRig(null, brick);
        AttributeRowModel before = rig.Row("Damage");

        before.KindLabel = "True or false";
        rig.Publish();

        ValueOf(brick, "Damage").ShouldBe(MemberValue.Of(false));
        rig.Row("Damage").ShouldNotBeSameAs(before);
        rig.Row("Damage").IsBoolean.ShouldBeTrue();
        rig.Undo.UndoCount.ShouldBe(1);

        rig.Undo.Undo();
        rig.Publish();
        rig.Row("Damage").Cells.Single().Text.ShouldBe("25");
    }

    [Fact]
    public void A_rename_keeps_the_value_and_a_name_the_rules_refuse_puts_the_old_one_back()
    {
        SceneNode brick = Node("Brick", ("Damage", MemberValue.Of(25d)), ("Label", MemberValue.Of("hot")));
        var rig = new TagPanelRig(null, brick);

        rig.Type(rig.Row("Damage").NameField, "Label");
        rig.Row("Damage").NameField.Rejection.ShouldBe("Not renamed: There is an attribute called 'Label' already.");
        rig.Row("Damage").NameField.Text.ShouldBe("Damage");

        rig.Type(rig.Row("Damage").NameField, "not a name");
        rig.Row("Damage").HasRejection.ShouldBeTrue();
        rig.AttributeEdits.ShouldBeEmpty();

        rig.Type(rig.Row("Damage").NameField, "Harm");
        brick.Attributes.Select(pair => pair.Key).ShouldBe(["Harm", "Label"]);
        ValueOf(brick, "Harm").ShouldBe(MemberValue.Of(25d));
        rig.Attributes.Rows.Select(row => row.Name).ShouldBe(["Harm", "Label"]);
    }

    [Fact]
    public void Removing_a_row_takes_the_attribute_off_every_selected_node_as_one_undo_entry()
    {
        SceneNode a = Node("A", ("Damage", MemberValue.Of(1d)), ("Label", MemberValue.Of("a")));
        SceneNode b = Node("B", ("Damage", MemberValue.Of(2d)));
        var rig = new TagPanelRig(null, a, b);

        rig.Attributes.Remove(rig.Row("Damage"));
        rig.Publish();

        a.Attributes.Select(pair => pair.Key).ShouldBe(["Label"]);
        b.Attributes.Count.ShouldBe(0);
        rig.Attributes.Rows.Select(row => row.Name).ShouldBe(["Label"]);
        rig.Undo.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void Several_nodes_show_what_only_some_have_as_partial_and_what_differs_as_mixed()
    {
        var rig = new TagPanelRig(
            null,
            Node("A", ("Damage", MemberValue.Of(10d)), ("Push", MemberValue.Of(new Vector3(1f, 2f, 3f))), ("Odd", MemberValue.Of(1d))),
            Node("B", ("Damage", MemberValue.Of(10d)), ("Push", MemberValue.Of(new Vector3(1f, 5f, 3f))), ("Odd", MemberValue.Of("one"))),
            Node("C", ("Damage", MemberValue.Of(10d))));

        AttributeRowModel damage = rig.Row("Damage");
        damage.IsPartial.ShouldBeFalse();
        damage.Cells.Single().Text.ShouldBe("10");

        AttributeRowModel push = rig.Row("Push");
        push.IsPartial.ShouldBeTrue();
        push.PartialNote.ShouldBe("On 2 of 3. A new value goes on all 3.");
        push.Cells.Select(cell => cell.Text).ShouldBe(["1", "", "3"]);
        push.Cells.Select(cell => cell.Placeholder).ShouldBe(["", "mixed", ""]);

        AttributeRowModel odd = rig.Row("Odd");
        odd.IsMixedKind.ShouldBeTrue();
        odd.KindLabel.ShouldBe("");
        odd.Cells.ShouldBeEmpty();
    }

    [Fact]
    public void A_value_typed_over_several_nodes_goes_on_all_of_them_as_one_undo_entry()
    {
        SceneNode a = Node("A", ("Damage", MemberValue.Of(10d)), ("Push", MemberValue.Of(new Vector3(1f, 2f, 3f))));
        SceneNode b = Node("B", ("Push", MemberValue.Of(new Vector3(7f, 8f, 9f))));
        var rig = new TagPanelRig(null, a, b);

        rig.Type(rig.Row("Damage").Cells.Single(), "25");
        ValueOf(a, "Damage").ShouldBe(MemberValue.Of(25d));
        ValueOf(b, "Damage").ShouldBe(MemberValue.Of(25d));
        rig.Row("Damage").IsPartial.ShouldBeFalse();
        rig.Undo.UndoCount.ShouldBe(1);

        // One number of three: each node keeps its other two.
        rig.Type(rig.Row("Push").Cells[1], "0");
        ValueOf(a, "Push").Vector.ShouldBe(new Vector3(1f, 0f, 3f));
        ValueOf(b, "Push").Vector.ShouldBe(new Vector3(7f, 0f, 9f));
        rig.Undo.UndoCount.ShouldBe(2);

        rig.Undo.Undo();
        rig.Undo.Undo();
        b.Attributes.Select(pair => pair.Key).ShouldBe(["Push"]);
        ValueOf(b, "Push").Vector.ShouldBe(new Vector3(7f, 8f, 9f));
    }

    [Fact]
    public void Picking_a_kind_for_values_of_different_kinds_gives_them_all_one()
    {
        SceneNode a = Node("A", ("Odd", MemberValue.Of(1d)));
        SceneNode b = Node("B", ("Odd", MemberValue.Of("one")));
        var rig = new TagPanelRig(null, a, b);

        rig.Row("Odd").KindLabel = "Number";
        rig.Publish();

        ValueOf(a, "Odd").ShouldBe(MemberValue.Of(0d));
        ValueOf(b, "Odd").ShouldBe(MemberValue.Of(0d));
        rig.Row("Odd").IsMixedKind.ShouldBeFalse();
    }

    [Fact]
    public void A_name_the_entity_on_the_node_answers_to_is_refused_and_one_already_there_is_marked()
    {
        EntitySchemaCatalog schemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
        [
            new EntitySchema("door", keyvalues:
            [
                new("speed", "", "", "100", KeyvalueType.Float, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u,
                    KeyvalueDescriptor.NoChoices),
            ]),
        ]));
        SceneNode door = Node("Door", ("speed", MemberValue.Of(9d)), ("Score", MemberValue.Of(7d)));
        door.Entity = new EntityData("door");
        door.Entity.SetValue("note", "only the node has it");
        var rig = new TagPanelRig(schemas, door);

        // In a hand-written map, or from before the node was a door.
        rig.Row("speed").IsTakenByEntity.ShouldBeTrue();
        rig.Row("speed").TakenNote.ShouldBe(
            "The entity has a setting called 'speed', and scripts read that. Give this attribute another name.");
        rig.Row("Score").IsTakenByEntity.ShouldBeFalse();

        rig.Type(rig.Attributes.NewNameField, "note");
        rig.Attributes.NewNameField.Rejection.ShouldBe(
            "Not added: 'note' is a setting of the entity already, and scripts read it as an attribute. " +
            "Pick another name.");

        rig.Type(rig.Row("Score").NameField, "speed");
        rig.Row("Score").HasRejection.ShouldBeTrue();
        rig.AttributeEdits.ShouldBeEmpty();

        // The way out the note names: another name.
        rig.Type(rig.Row("speed").NameField, "Pace");
        door.Attributes.Select(pair => pair.Key).ShouldBe(["Pace", "Score"]);
        rig.Row("Pace").IsTakenByEntity.ShouldBeFalse();
    }

    [Fact]
    public void A_row_is_kept_while_its_name_and_kind_stay_so_a_cell_being_typed_in_is_not_replaced()
    {
        SceneNode brick = Node("Brick", ("Damage", MemberValue.Of(25d)), ("Label", MemberValue.Of("hot")));
        var rig = new TagPanelRig(null, brick);
        AttributeRowModel label = rig.Row("Label");
        PropertyFieldModel cell = label.Cells.Single();

        // Somebody is typing in Label while Damage changes under them.
        cell.BeginEdit();
        cell.Text = "half typ";
        brick.Attributes = brick.Attributes.With("Damage", MemberValue.Of(26d));
        rig.Publish();

        rig.Row("Label").ShouldBeSameAs(label);
        cell.Text.ShouldBe("half typ");
        rig.Row("Damage").Cells.Single().Text.ShouldBe("26");
    }

    [Fact]
    public void A_colour_dragged_in_the_picker_shows_at_once_and_posts_each_step()
    {
        SceneNode brick = Node("Brick", ("Glow", MemberValue.Of(new Color3(1f, 1f, 1f))));
        var rig = new TagPanelRig(null, brick);
        AttributeRowModel glow = rig.Row("Glow");

        glow.ScrubColor(new Color3(1f, 0f, 0f).ToLinear());

        glow.Cells.Single().Text.ShouldBe("#FF0000");
        ValueOf(brick, "Glow").Color.ShouldBe(new Color3(1f, 0f, 0f));
        rig.AttributeEdits.Count.ShouldBe(1);
    }
}
