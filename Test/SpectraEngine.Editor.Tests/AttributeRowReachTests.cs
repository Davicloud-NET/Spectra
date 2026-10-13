using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// What a row only some of the selected nodes have says of a new value: where
/// it goes, and why it stays off a node that cannot take it. The editor
/// leaves such a node as it is, so the row has to say so first.
/// </summary>
public sealed class AttributeRowReachTests
{
    private static SceneNode Node(string name, params (string Name, MemberValue Value)[] attributes)
    {
        AttributeSet set = AttributeSet.Empty;
        foreach ((string attribute, MemberValue value) in attributes)
            set = set.With(attribute, value);

        return new SceneNode(name) { Attributes = set };
    }

    private static SceneNode Full(string name)
    {
        AttributeSet.TryMake(
            [.. Enumerable.Range(0, AttributeLimits.MaxPerNode)
                .Select(i => new KeyValuePair<string, MemberValue>($"Name{i}", MemberValue.Of((double)i)))],
            out AttributeSet? set, out _).ShouldBeTrue();

        return new SceneNode(name) { Attributes = set.ShouldNotBeNull() };
    }

    private static EntitySchemaCatalog Doors() => EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        new EntitySchema("door", keyvalues:
        [
            new("speed", "", "", "100", KeyvalueType.Float, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u,
                KeyvalueDescriptor.NoChoices),
        ]),
    ]));

    [Fact]
    public void A_row_every_other_node_has_room_for_says_a_new_value_goes_on_all()
    {
        var rig = new TagPanelRig(null, Node("A", ("Damage", MemberValue.Of(10d))), Node("B"), Node("C"));

        rig.Row("Damage").PartialNote.ShouldBe("On 1 of 3. A new value goes on all 3.");
    }

    [Fact]
    public void With_the_levels_root_selected_a_row_says_a_new_value_stays_off_it_and_it_does()
    {
        SceneNode a = Node("A", ("Damage", MemberValue.Of(10d)));
        SceneNode b = Node("B", ("Damage", MemberValue.Of(10d)));
        var rig = new TagPanelRig(null, a, b);
        rig.Selected.Add(rig.Scene.Root);
        rig.Publish();
        const string note = "On 2 of 3. A new value stays on those 2. The level's root keeps no attributes.";

        rig.Row("Damage").IsPartial.ShouldBeTrue();
        rig.Row("Damage").PartialNote.ShouldBe(note);

        rig.Type(rig.Row("Damage").Cells.Single(), "25");

        a.Attributes.TryGet("Damage", out MemberValue onA).ShouldBeTrue();
        onA.ShouldBe(MemberValue.Of(25d));
        rig.Scene.Root.Attributes.Count.ShouldBe(0);
        rig.Row("Damage").PartialNote.ShouldBe(note, "what the row said is what happened");
    }

    [Fact]
    public void A_node_with_no_room_is_counted_out_and_the_others_still_get_the_value()
    {
        SceneNode has = Node("Has", ("Damage", MemberValue.Of(10d)));
        SceneNode free = Node("Free");
        SceneNode full = Full("Full");
        var rig = new TagPanelRig(null, has, free, full);

        rig.Row("Damage").PartialNote.ShouldBe(
            "On 1 of 3. A new value goes on 2 of them. A node holds at most 256 attributes.");

        rig.Type(rig.Row("Damage").Cells.Single(), "25");

        free.Attributes.Contains("Damage").ShouldBeTrue();
        full.Attributes.Contains("Damage").ShouldBeFalse();
        rig.Row("Damage").PartialNote.ShouldBe(
            "On 2 of 3. A new value stays on those 2. A node holds at most 256 attributes.");
    }

    [Fact]
    public void A_node_whose_entity_has_a_setting_of_the_name_is_counted_out()
    {
        SceneNode brick = Node("Brick", ("speed", MemberValue.Of(4d)));
        SceneNode door = Node("Door");
        door.Entity = new EntityData("door");
        var rig = new TagPanelRig(Doors(), brick, door);

        rig.Row("speed").PartialNote.ShouldBe(
            "On 1 of 2. A new value stays on that one. An entity among them has a setting called 'speed' already.");

        rig.Type(rig.Row("speed").Cells.Single(), "9");

        door.Attributes.Count.ShouldBe(0);
        brick.Attributes.TryGet("speed", out MemberValue speed).ShouldBeTrue();
        speed.ShouldBe(MemberValue.Of(9d));
    }

    [Fact]
    public void Every_reason_a_node_is_left_out_for_is_said_once()
    {
        SceneNode has = Node("Has", ("speed", MemberValue.Of(4d)));
        SceneNode door = Node("Door");
        door.Entity = new EntityData("door");
        var rig = new TagPanelRig(Doors(), has, door, Full("Full"), Full("Fuller"));
        rig.Selected.Add(rig.Scene.Root);
        rig.Publish();

        rig.Row("speed").PartialNote.ShouldBe(
            "On 1 of 5. A new value stays on that one. The level's root keeps no attributes. " +
            "A node holds at most 256 attributes. An entity among them has a setting called 'speed' already.");
    }

    [Fact]
    public void A_row_all_selected_nodes_have_says_nothing()
    {
        var rig = new TagPanelRig(
            null, Node("A", ("Damage", MemberValue.Of(1d))), Node("B", ("Damage", MemberValue.Of(2d))));

        rig.Row("Damage").IsPartial.ShouldBeFalse();
        rig.Row("Damage").PartialNote.ShouldBe("");
    }
}
