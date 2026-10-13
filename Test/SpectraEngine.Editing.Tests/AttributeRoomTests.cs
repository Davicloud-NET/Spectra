using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Undo;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Where a new value goes when only some of the selected nodes have the
/// attribute. The panel's row counts the nodes it will stay off and the
/// editor leaves those out, so the two are held to one answer here.
/// </summary>
public sealed class AttributeRoomTests
{
    private const AttributeRoom Every = AttributeRoom.Root | AttributeRoom.Full | AttributeRoom.Entity;

    // One node that has 'speed', one with room for it, and one for each
    // thing that stands in the way: no room, an entity's setting, the root.
    private sealed class Mixed
    {
        public Mixed()
        {
            Scene = new Scene("marked") { EntitySchemas = Doors() };
            Has = Scene.Root.CreateChild("Has");
            Has.Attributes = AttributeSet.Empty.With("speed", MemberValue.Of(4d));
            Free = Scene.Root.CreateChild("Free");
            Full = Scene.Root.CreateChild("Full");
            Full.Attributes = FullSet();
            Door = Scene.Root.CreateChild("Door");
            Door.Entity = new EntityData("door");
            Nodes = [Has, Free, Full, Door, Scene.Root];
            Undo = new UndoStack(Scene);
        }

        public Scene Scene { get; }
        public UndoStack Undo { get; }
        public SceneNode Has { get; }
        public SceneNode Free { get; }
        public SceneNode Full { get; }
        public SceneNode Door { get; }
        public SceneNode[] Nodes { get; }

        public AttributePanelRow Row(string name) =>
            AttributePanelInfo.Capture(Nodes, Scene.EntitySchemas).ShouldNotBeNull()
                .Rows.Single(row => row.Name == name);
    }

    private static AttributeSet FullSet()
    {
        AttributeSet.TryMake(
            [.. Enumerable.Range(0, AttributeLimits.MaxPerNode)
                .Select(i => new KeyValuePair<string, MemberValue>($"Name{i}", MemberValue.Of((double)i)))],
            out AttributeSet? set, out _).ShouldBeTrue();

        return set.ShouldNotBeNull();
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
    public void A_row_counts_the_nodes_a_new_value_stays_off_and_says_what_stands_in_the_way()
    {
        var mixed = new Mixed();

        AttributePanelRow speed = mixed.Row("speed");

        speed.Count.ShouldBe(1);
        speed.LeftOutCount.ShouldBe(3);
        speed.LeftOut.ShouldBe(Every);
    }

    [Fact]
    public void The_editor_leaves_out_the_nodes_the_row_counted_and_no_others()
    {
        var mixed = new Mixed();
        int leftOut = mixed.Row("speed").LeftOutCount;

        int changed = AttributeEditor.Apply(mixed.Undo, mixed.Nodes, AttributeEdit.Set("speed", MemberValue.Of(9d)));

        changed.ShouldBe(mixed.Nodes.Length - leftOut);
        mixed.Nodes.Count(node => node.Attributes.Contains("speed")).ShouldBe(mixed.Nodes.Length - leftOut);
        mixed.Free.Attributes.Contains("speed").ShouldBeTrue();
        mixed.Full.Attributes.Contains("speed").ShouldBeFalse();
        mixed.Door.Attributes.Count.ShouldBe(0);
        mixed.Scene.Root.Attributes.Count.ShouldBe(0);

        // Nothing is left to reach, and the row still names who is out.
        AttributePanelRow after = mixed.Row("speed");
        after.Count.ShouldBe(2);
        after.LeftOutCount.ShouldBe(3);
    }

    [Fact]
    public void One_number_of_three_stays_off_the_same_nodes()
    {
        var mixed = new Mixed();
        mixed.Has.Attributes = mixed.Has.Attributes.With("Push", MemberValue.Of(new Vector3(1f, 2f, 3f)));
        AttributePanelRow push = mixed.Row("Push");
        push.LeftOut.ShouldBe(AttributeRoom.Root | AttributeRoom.Full, "no entity has a setting called Push");

        int changed = AttributeEditor.Apply(
            mixed.Undo, mixed.Nodes, AttributeEdit.SetPart("Push", MemberValue.Of(Vector3.Zero), 1, 7f));

        changed.ShouldBe(mixed.Nodes.Length - push.LeftOutCount);
        mixed.Door.Attributes.Contains("Push").ShouldBeTrue();
        mixed.Full.Attributes.Contains("Push").ShouldBeFalse();
        mixed.Scene.Root.Attributes.Count.ShouldBe(0);
    }

    [Fact]
    public void A_node_that_has_the_attribute_is_never_left_out()
    {
        var mixed = new Mixed();

        // Name7 is on the full node alone, which keeps it and takes a new value for it.
        AttributePanelRow held = mixed.Row("Name7");
        held.Count.ShouldBe(1);
        held.LeftOut.ShouldBe(AttributeRoom.Root);
        held.LeftOutCount.ShouldBe(1);

        AttributeEditor.Apply(mixed.Undo, [mixed.Full], AttributeEdit.Set("Name7", MemberValue.Of(700d))).ShouldBe(1);
    }

    [Fact]
    public void A_row_every_node_has_or_has_room_for_leaves_nobody_out()
    {
        var scene = new Scene("plain");
        SceneNode a = scene.Root.CreateChild("A");
        a.Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(1d));
        SceneNode b = scene.Root.CreateChild("B");

        AttributePanelRow damage = AttributePanelInfo.Capture([a, b]).ShouldNotBeNull().Rows.Single();

        damage.LeftOutCount.ShouldBe(0);
        damage.LeftOut.ShouldBe(AttributeRoom.Open);
    }

    [Fact]
    public void Each_thing_in_the_way_is_named_for_the_node_it_is_in_the_way_of()
    {
        var mixed = new Mixed();
        EntitySchemaCatalog? schemas = mixed.Scene.EntitySchemas;

        AttributePanelInfo.RoomFor(mixed.Free, "speed", schemas).ShouldBe(AttributeRoom.Open);
        AttributePanelInfo.RoomFor(mixed.Full, "speed", schemas).ShouldBe(AttributeRoom.Full);
        AttributePanelInfo.RoomFor(mixed.Door, "speed", schemas).ShouldBe(AttributeRoom.Entity);
        AttributePanelInfo.RoomFor(mixed.Door, "Damage", schemas).ShouldBe(AttributeRoom.Open);
        AttributePanelInfo.RoomFor(mixed.Scene.Root, "speed", schemas).ShouldBe(AttributeRoom.Root);
    }

    [Fact]
    public void The_words_for_what_stands_in_the_way_name_each_once()
    {
        PropertyLimits.LeftOut(AttributeRoom.Open, "speed").ShouldBe("");
        PropertyLimits.LeftOut(AttributeRoom.Root, "speed").ShouldBe("The level's root keeps no attributes.");
        PropertyLimits.LeftOut(AttributeRoom.Full, "speed").ShouldBe("A node holds at most 256 attributes.");
        PropertyLimits.LeftOut(Every, "speed").ShouldBe(
            "The level's root keeps no attributes. A node holds at most 256 attributes. " +
            "An entity among them has a setting called 'speed' already.");
    }
}
