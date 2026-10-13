using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What the engine tells a panel of the selection's tags and attributes:
/// merged over the selected nodes, and the same instance while nothing
/// changed.
/// </summary>
public sealed class TagPanelInfoTests
{
    private static SceneNode Tagged(string name, params string[] tags) => new(name) { Tags = TagSet.Of(tags) };

    private static SceneNode With(string name, params (string Name, MemberValue Value)[] attributes)
    {
        AttributeSet set = AttributeSet.Empty;
        foreach ((string attribute, MemberValue value) in attributes)
            set = set.With(attribute, value);

        return new SceneNode(name) { Attributes = set };
    }

    [Fact]
    public void No_selection_has_no_info()
    {
        TagPanelInfo.Capture([]).ShouldBeNull();
        AttributePanelInfo.Capture([]).ShouldBeNull();
    }

    [Fact]
    public void Tags_are_listed_once_each_in_the_order_first_met_with_how_many_nodes_have_them()
    {
        TagPanelInfo info = TagPanelInfo.Capture(
            [Tagged("A", "Lava", "Hot"), Tagged("B"), Tagged("C", "Deep", "Lava")]).ShouldNotBeNull();

        info.SelectionCount.ShouldBe(3);
        info.Tags.ShouldBe([new TagPanelRow("Lava", 2), new TagPanelRow("Hot", 1), new TagPanelRow("Deep", 1)]);
        info.HasFullNode.ShouldBeFalse();
    }

    [Fact]
    public void The_same_info_comes_back_until_a_tag_or_the_selection_changes()
    {
        SceneNode a = Tagged("A", "Lava");
        SceneNode b = Tagged("B");
        TagPanelInfo first = TagPanelInfo.Capture([a, b]).ShouldNotBeNull();

        TagPanelInfo.Capture([a, b], first).ShouldBeSameAs(first);

        a.Tags = a.Tags.With("Hot");
        TagPanelInfo edited = TagPanelInfo.Capture([a, b], first).ShouldNotBeNull();
        edited.ShouldNotBeSameAs(first);
        edited.Tags.Count.ShouldBe(2);

        TagPanelInfo.Capture([a], edited).ShouldNotBeSameAs(edited);
    }

    [Fact]
    public void A_node_with_no_room_for_one_more_is_told()
    {
        SceneNode full = Tagged("Full", [.. Enumerable.Range(0, TagLimits.MaxPerNode).Select(i => $"T{i}")]);

        TagPanelInfo.Capture([Tagged("A"), full]).ShouldNotBeNull().HasFullNode.ShouldBeTrue();
    }

    [Fact]
    public void Attributes_are_merged_by_name_with_how_many_nodes_have_each()
    {
        AttributePanelInfo info = AttributePanelInfo.Capture(
        [
            With("A", ("Damage", MemberValue.Of(10d)), ("Label", MemberValue.Of("a"))),
            With("B", ("Label", MemberValue.Of("a")), ("Damage", MemberValue.Of(20d))),
            With("C"),
        ]).ShouldNotBeNull();

        info.SelectionCount.ShouldBe(3);
        info.Rows.Select(row => row.Name).ShouldBe(["Damage", "Label"]);
        info.Rows[0].Count.ShouldBe(2);
        info.Rows[0].Value.ShouldBe(MemberValue.Of(10d), "the first node's value stands for the row");
        info.Rows[0].IsMixed.ShouldBeTrue();
        info.Rows[1].IsMixed.ShouldBeFalse();
        info.EntityNames.ShouldBeEmpty();
    }

    [Fact]
    public void Three_numbers_and_a_frame_say_which_of_their_parts_differ()
    {
        var turned = new CFrame(new Vector3(1f, 2f, 3f), Quaternion.CreateFromYawPitchRoll(1f, 0f, 0f));
        AttributePanelInfo info = AttributePanelInfo.Capture(
        [
            With("A", ("Push", MemberValue.Of(new Vector3(1f, 2f, 3f))), ("Rest", MemberValue.Of(new CFrame(1f, 2f, 3f)))),
            With("B", ("Push", MemberValue.Of(new Vector3(1f, 5f, 3f))), ("Rest", MemberValue.Of(turned))),
            With("C", ("Push", MemberValue.Of(new Vector3(1f, 2f, 9f))), ("Rest", MemberValue.Of(new CFrame(0f, 2f, 3f)))),
        ]).ShouldNotBeNull();

        info.Rows[0].MixedParts.ShouldBe(0b110, "y and z of the three numbers");
        info.Rows[1].MixedParts.ShouldBe(0b111001, "x of the position, and the whole rotation");
    }

    [Fact]
    public void Values_of_different_kinds_under_one_name_make_a_row_of_no_kind()
    {
        AttributePanelInfo info = AttributePanelInfo.Capture(
            [With("A", ("Odd", MemberValue.Of(1d))), With("B", ("Odd", MemberValue.Of("one")))]).ShouldNotBeNull();

        info.Rows[0].Kind.ShouldBe(MemberValueKind.None);
        info.Rows[0].IsMixed.ShouldBeTrue();
        info.Rows[0].Count.ShouldBe(2);
    }

    [Fact]
    public void An_attribute_with_the_name_of_its_nodes_keyvalue_is_marked_and_the_names_are_handed_over()
    {
        EntitySchemaCatalog schemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
        [
            new EntitySchema("door", keyvalues:
            [
                new("speed", "", "", "100", KeyvalueType.Float, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u,
                    KeyvalueDescriptor.NoChoices),
            ]),
        ]));
        SceneNode door = With("Door", ("speed", MemberValue.Of(9d)), ("Score", MemberValue.Of(7d)));
        door.Entity = new EntityData("door");
        door.Entity.SetValue("note", "only the node has it");
        SceneNode crate = With("Crate", ("speed", MemberValue.Of(9d)));

        AttributePanelInfo info = AttributePanelInfo.Capture([crate, door], schemas).ShouldNotBeNull();

        info.Rows.Single(row => row.Name == "speed").IsTakenByEntity.ShouldBeTrue();
        info.Rows.Single(row => row.Name == "Score").IsTakenByEntity.ShouldBeFalse();
        info.EntityNames.ShouldBe(["speed", "note"], ignoreOrder: true);

        AttributePanelInfo.Capture([crate], schemas).ShouldNotBeNull()
            .Rows.Single().IsTakenByEntity.ShouldBeFalse("a node with no entity has no setting to clash with");
    }

    [Fact]
    public void The_same_attribute_info_comes_back_until_something_changes_and_never_for_an_entity()
    {
        SceneNode a = With("A", ("Damage", MemberValue.Of(10d)));
        AttributePanelInfo first = AttributePanelInfo.Capture([a]).ShouldNotBeNull();

        AttributePanelInfo.Capture([a], null, first).ShouldBeSameAs(first);

        a.Attributes = a.Attributes.With("Damage", MemberValue.Of(11d));
        AttributePanelInfo.Capture([a], null, first).ShouldNotBeSameAs(first);

        // An entity's keyvalues change in place, under the same set.
        a.Entity = new EntityData("thing");
        AttributePanelInfo withEntity = AttributePanelInfo.Capture([a]).ShouldNotBeNull();
        AttributePanelInfo.Capture([a], null, withEntity).ShouldNotBeSameAs(withEntity);
    }

    [Fact]
    public void A_frames_rotation_is_read_and_written_in_degrees_one_number_at_a_time()
    {
        MemberValue frame = MemberValue.Of(new CFrame(1f, 2f, 3f));

        MemberValue turned = AttributeParts.With(in frame, 4, 90f);
        MemberValue moved = AttributeParts.With(in turned, 0, -5f);

        AttributeParts.CountOf(MemberValueKind.CFrame).ShouldBe(6);
        AttributeParts.Get(in moved, 0).ShouldBe(-5f);
        AttributeParts.Get(in moved, 1).ShouldBe(2f);
        AttributeParts.Get(in moved, 4).ShouldBe(90f, 1e-3f);
        AttributeParts.Get(in moved, 3).ShouldBe(0f, 1e-3f);
        moved.Frame.Rotation.ShouldBe(turned.Frame.Rotation, "moving it leaves the rotation bit for bit");
    }

    [Fact]
    public void Every_kind_has_a_starting_value_a_map_can_hold()
    {
        foreach (MemberValueKind kind in Enum.GetValues<MemberValueKind>())
        {
            if (kind == MemberValueKind.None) continue;

            MemberValue start = AttributeParts.DefaultOf(kind);
            start.Kind.ShouldBe(kind);
            AttributeValueRule.Refusal(in start).ShouldBeNull(kind.ToString());
        }
    }

    [Fact]
    public void A_change_of_tags_is_logged_with_the_new_tags_for_a_tree_to_filter_by()
    {
        var scene = new Scene("Level");
        SceneNode brick = scene.Root.AddChild(Tagged("Brick", "Lava"));
        var log = new SceneChangeLog();
        log.Observe(scene);
        log.Drain();

        brick.Tags = brick.Tags.With("Hot");
        brick.Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(1d));

        SceneChange change = log.Drain().Changes.ShouldHaveSingleItem();
        change.Kind.ShouldBe(SceneChangeKind.TagsChanged);
        change.NodeId.ShouldBe(brick.Id);
        change.Tags.ShouldBe(["Lava", "Hot"]);

        SceneChange.Of(SceneChangeKind.Added, brick).Tags.ShouldBe(["Lava", "Hot"]);
        default(SceneChange).Tags.ShouldBeSameAs(TagSet.Empty);
        ((int)SceneChangeKind.TagsChanged).ShouldBe(5, "a kind is added at the end");
    }
}
