using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editor.Shell;
using System.Numerics;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// A cell of the Attributes section that is entered and left again. Focus
/// begins an edit and losing it commits, so a click in and out, or a Tab
/// through the cell, runs a commit with nothing typed.
/// </summary>
public sealed class AttributeCellLeavingTests
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

    // Focus, then focus lost.
    private static void EnterAndLeave(TagPanelRig rig, PropertyFieldModel cell)
    {
        cell.BeginEdit();
        cell.Commit();
        rig.Publish();
    }

    [Fact]
    public void Leaving_a_mixed_text_cell_with_nothing_typed_changes_no_node()
    {
        SceneNode a = Node("A", ("Label", MemberValue.Of("hot")));
        SceneNode b = Node("B", ("Label", MemberValue.Of("cold")));
        SceneNode c = Node("C");
        var rig = new TagPanelRig(null, a, b, c);
        PropertyFieldModel cell = rig.Row("Label").Cells.Single();
        cell.Text.ShouldBe("");
        cell.Placeholder.ShouldBe("mixed");

        EnterAndLeave(rig, cell);

        rig.AttributeEdits.ShouldBeEmpty();
        ValueOf(a, "Label").ShouldBe(MemberValue.Of("hot"));
        ValueOf(b, "Label").ShouldBe(MemberValue.Of("cold"));
        c.Attributes.Count.ShouldBe(0, "a node without the attribute did not get it");
        rig.Undo.UndoCount.ShouldBe(0);
        cell.HasRejection.ShouldBeFalse();
    }

    [Fact]
    public void Text_typed_into_a_mixed_cell_and_taken_out_again_changes_no_node()
    {
        SceneNode a = Node("A", ("Label", MemberValue.Of("hot")));
        SceneNode b = Node("B", ("Label", MemberValue.Of("cold")));
        var rig = new TagPanelRig(null, a, b);
        PropertyFieldModel cell = rig.Row("Label").Cells.Single();

        cell.BeginEdit();
        cell.Text = "war";
        cell.Text = "";
        cell.Commit();
        rig.Publish();

        rig.AttributeEdits.ShouldBeEmpty();
        ValueOf(a, "Label").ShouldBe(MemberValue.Of("hot"));
        ValueOf(b, "Label").ShouldBe(MemberValue.Of("cold"));
    }

    [Fact]
    public void A_mixed_text_cell_still_takes_what_is_typed_onto_every_node()
    {
        SceneNode a = Node("A", ("Label", MemberValue.Of("hot")));
        SceneNode b = Node("B", ("Label", MemberValue.Of("cold")));
        var rig = new TagPanelRig(null, a, b);

        rig.Type(rig.Row("Label").Cells.Single(), "warm");

        ValueOf(a, "Label").ShouldBe(MemberValue.Of("warm"));
        ValueOf(b, "Label").ShouldBe(MemberValue.Of("warm"));
        rig.Undo.UndoCount.ShouldBe(1);

        // Now that they agree, the text can be emptied on both.
        rig.Type(rig.Row("Label").Cells.Single(), "");
        ValueOf(a, "Label").ShouldBe(MemberValue.Of(""));
        ValueOf(b, "Label").ShouldBe(MemberValue.Of(""));
    }

    [Fact]
    public void Leaving_text_with_spaces_at_its_ends_keeps_the_spaces()
    {
        // A map written by hand can hold them. The cell trims what is typed.
        SceneNode brick = Node("Brick", ("Label", MemberValue.Of("  hot ")));
        var rig = new TagPanelRig(null, brick);
        PropertyFieldModel cell = rig.Row("Label").Cells.Single();
        cell.Text.ShouldBe("  hot ");

        EnterAndLeave(rig, cell);

        rig.AttributeEdits.ShouldBeEmpty();
        ValueOf(brick, "Label").ShouldBe(MemberValue.Of("  hot "));
        rig.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Text_with_spaces_at_its_ends_is_trimmed_once_somebody_types_in_it()
    {
        SceneNode brick = Node("Brick", ("Label", MemberValue.Of("  hot ")));
        var rig = new TagPanelRig(null, brick);

        rig.Type(rig.Row("Label").Cells.Single(), "  hot");

        ValueOf(brick, "Label").ShouldBe(MemberValue.Of("hot"));
    }

    [Theory]
    [InlineData("Damage")]
    [InlineData("Push")]
    [InlineData("Glow")]
    [InlineData("Rest")]
    public void Leaving_any_mixed_cell_with_nothing_typed_changes_no_node(string attribute)
    {
        SceneNode a = Node(
            "A",
            ("Damage", MemberValue.Of(1d)),
            ("Push", MemberValue.Of(new Vector3(1f, 2f, 3f))),
            ("Glow", MemberValue.Of(new Color3(1f, 0f, 0f))),
            ("Rest", MemberValue.Of(new CFrame(1f, 2f, 3f))));
        SceneNode b = Node(
            "B",
            ("Damage", MemberValue.Of(2d)),
            ("Push", MemberValue.Of(new Vector3(4f, 5f, 6f))),
            ("Glow", MemberValue.Of(new Color3(0f, 0f, 1f))),
            ("Rest", MemberValue.Of(new CFrame(4f, 5f, 6f))));
        var rig = new TagPanelRig(null, a, b, Node("C"));
        AttributeSet before = a.Attributes;

        foreach (PropertyFieldModel cell in rig.Row(attribute).Cells.Concat(rig.Row(attribute).TurnCells))
            EnterAndLeave(rig, cell);

        rig.AttributeEdits.ShouldBeEmpty();
        a.Attributes.ShouldBeSameAs(before);
        rig.Undo.UndoCount.ShouldBe(0);
    }
}
