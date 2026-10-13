using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The Tags section of the Properties panel: chips for the tags of the
/// selection, a field that adds one to every selected node, and what the
/// rules refuse shown where it was typed.
/// </summary>
public sealed class TagsPanelTests
{
    private static SceneNode Node(string name, params string[] tags) => new(name) { Tags = TagSet.Of(tags) };

    [Fact]
    public void With_nothing_selected_there_is_no_chip()
    {
        var rig = new TagPanelRig(null);

        rig.Tags.Chips.ShouldBeEmpty();
        rig.Tags.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void One_node_shows_its_tags_in_the_order_it_got_them()
    {
        var rig = new TagPanelRig(null, Node("Brick", "Lava", "Hot stuff"));

        rig.Tags.Chips.Select(chip => chip.Tag).ShouldBe(["Lava", "Hot stuff"]);
        rig.Tags.Chips.ShouldAllBe(chip => !chip.IsPartial && chip.PartialLabel == "");
        rig.Tags.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Enter_in_the_field_adds_the_tag_and_empties_the_field()
    {
        SceneNode brick = Node("Brick", "Lava");
        var rig = new TagPanelRig(null, brick);

        rig.Type(rig.Tags.AddField, "  Hot stuff  ");

        rig.TagEdits.ShouldBe([new TagEdit("Hot stuff", IsOn: true)]);
        brick.Tags.ShouldBe(["Lava", "Hot stuff"]);
        rig.Tags.Chips.Select(chip => chip.Tag).ShouldBe(["Lava", "Hot stuff"]);
        rig.Tags.AddField.Text.ShouldBe("");
        rig.Tags.AddField.HasRejection.ShouldBeFalse();
    }

    [Fact]
    public void A_chip_shows_nothing_the_engine_has_not_said()
    {
        var rig = new TagPanelRig(null, Node("Brick"));
        PropertyFieldModel field = rig.Tags.AddField;

        // Posted, and the engine has not answered yet.
        field.BeginEdit();
        field.Text = "Lava";
        field.Commit();

        rig.Tags.Chips.ShouldBeEmpty();
        rig.Publish();
        rig.Tags.Chips.Select(chip => chip.Tag).ShouldBe(["Lava"]);
    }

    [Fact]
    public void Several_nodes_show_a_tag_all_have_as_whole_and_one_some_have_as_partial()
    {
        var rig = new TagPanelRig(null, Node("A", "Lava", "Hot"), Node("B", "Lava"), Node("C", "Lava", "Hot"));

        TagChipModel lava = rig.Tags.Chips.Single(chip => chip.Tag == "Lava");
        TagChipModel hot = rig.Tags.Chips.Single(chip => chip.Tag == "Hot");

        lava.IsPartial.ShouldBeFalse();
        lava.Help.ShouldBe("On all 3 selected.");
        hot.IsPartial.ShouldBeTrue();
        hot.PartialLabel.ShouldBe("2 of 3");
        hot.Help.ShouldBe("On 2 of the 3 selected. Add it again to put it on all of them.");
    }

    [Fact]
    public void Adding_a_tag_puts_it_on_every_selected_node_as_one_undo_entry()
    {
        SceneNode a = Node("A", "Hot");
        SceneNode b = Node("B");
        var rig = new TagPanelRig(null, a, b);

        // One some have already: it goes on the rest.
        rig.Type(rig.Tags.AddField, "Hot");
        rig.Type(rig.Tags.AddField, "Lava");

        a.Tags.ShouldBe(["Hot", "Lava"]);
        b.Tags.ShouldBe(["Hot", "Lava"]);
        rig.Tags.Chips.ShouldAllBe(chip => !chip.IsPartial);
        rig.Undo.UndoCount.ShouldBe(2);

        rig.Undo.Undo();
        a.Tags.ShouldBe(["Hot"]);
        b.Tags.ShouldBe(["Hot"]);
    }

    [Fact]
    public void Removing_a_chip_takes_the_tag_off_every_selected_node_as_one_undo_entry()
    {
        SceneNode a = Node("A", "Lava", "Hot");
        SceneNode b = Node("B", "Hot");
        SceneNode c = Node("C");
        var rig = new TagPanelRig(null, a, b, c);

        rig.Tags.Remove(rig.Tags.Chips.Single(chip => chip.Tag == "Hot"));
        rig.Publish();

        rig.TagEdits.ShouldBe([new TagEdit("Hot", IsOn: false)]);
        a.Tags.ShouldBe(["Lava"]);
        b.Tags.Count.ShouldBe(0);
        rig.Tags.Chips.Select(chip => chip.Tag).ShouldBe(["Lava"]);
        rig.Undo.UndoCount.ShouldBe(1);

        rig.Undo.Undo();
        rig.Publish();
        rig.Tags.Chips.Select(chip => chip.Tag).ShouldBe(["Lava", "Hot"]);
    }

    [Fact]
    public void A_tag_too_long_is_shown_as_wrong_where_it_was_typed_and_is_not_added()
    {
        SceneNode brick = Node("Brick");
        var rig = new TagPanelRig(null, brick);
        string tooLong = new('a', TagLimits.MaxBytes + 1);

        rig.Type(rig.Tags.AddField, tooLong);

        rig.TagEdits.ShouldBeEmpty();
        brick.Tags.Count.ShouldBe(0);
        rig.Tags.AddField.Text.ShouldBe(tooLong, "what was typed stays, to be put right");
        rig.Tags.AddField.Rejection.ShouldBe(
            "Not added: A tag of 257 bytes is too long. A tag is 1 to 256 bytes of text.");

        // Typing again takes the reason away, and a tag that fits goes on.
        rig.Type(rig.Tags.AddField, "Lava");
        rig.Tags.AddField.HasRejection.ShouldBeFalse();
        brick.Tags.ShouldBe(["Lava"]);
    }

    [Fact]
    public void A_tag_every_selected_node_has_is_refused_with_the_reason()
    {
        var one = new TagPanelRig(null, Node("Brick", "Lava"));
        one.Type(one.Tags.AddField, "Lava");
        one.Tags.AddField.Rejection.ShouldBe("Not added: It has the tag 'Lava' already.");
        one.TagEdits.ShouldBeEmpty();

        var two = new TagPanelRig(null, Node("A", "Lava"), Node("B", "Lava"));
        two.Type(two.Tags.AddField, "Lava");
        two.Tags.AddField.Rejection.ShouldBe("Not added: Every selected node has the tag 'Lava' already.");

        // A tag is case sensitive: this is another one.
        two.Type(two.Tags.AddField, "lava");
        two.Tags.AddField.HasRejection.ShouldBeFalse();
        two.TagEdits.ShouldBe([new TagEdit("lava", IsOn: true)]);
    }

    [Fact]
    public void A_node_with_no_room_refuses_one_more_tag_with_the_reason()
    {
        SceneNode full = Node("Full", [.. Enumerable.Range(0, TagLimits.MaxPerNode).Select(i => $"Tag{i}")]);
        var rig = new TagPanelRig(null, full);

        rig.Type(rig.Tags.AddField, "OneMore");

        rig.Tags.AddField.Rejection.ShouldBe("Not added: A node has at most 256 tags.");
        rig.TagEdits.ShouldBeEmpty();
        full.Tags.Count.ShouldBe(TagLimits.MaxPerNode);
    }

    [Fact]
    public void The_levels_root_takes_no_tag_and_no_attribute_and_the_panel_says_why()
    {
        var rig = new TagPanelRig(null, Node("Brick"));
        rig.Selected.Clear();
        rig.Selected.Add(rig.Scene.Root);
        rig.Publish();

        rig.Type(rig.Tags.AddField, "Level");
        rig.Type(rig.Attributes.NewNameField, "Score");

        const string Why =
            "Not added: The level's root is not saved with the level, so it keeps no tags and no attributes. " +
            "Mark the nodes under it.";
        rig.Tags.AddField.Rejection.ShouldBe(Why);
        rig.Attributes.NewNameField.Rejection.ShouldBe(Why);
        rig.Scene.Root.Tags.Count.ShouldBe(0);
        rig.Scene.Root.Attributes.Count.ShouldBe(0);
        rig.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void An_empty_field_adds_nothing_and_says_nothing()
    {
        var rig = new TagPanelRig(null, Node("Brick"));

        rig.Type(rig.Tags.AddField, "   ");

        rig.TagEdits.ShouldBeEmpty();
        rig.Tags.AddField.HasRejection.ShouldBeFalse();
    }

    [Fact]
    public void The_chips_follow_the_selection()
    {
        SceneNode a = Node("A", "Lava");
        SceneNode b = Node("B", "Cold");
        var rig = new TagPanelRig(null, a);
        rig.Scene.Root.AddChild(b);

        rig.Selected.Clear();
        rig.Selected.Add(b);
        rig.Publish();
        rig.Tags.Chips.Select(chip => chip.Tag).ShouldBe(["Cold"]);

        rig.Selected.Clear();
        rig.Publish();
        rig.Tags.Chips.ShouldBeEmpty();
    }
}
