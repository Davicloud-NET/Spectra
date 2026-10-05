using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Parts in the span query: a part that collides is a solid where it stands
/// now, and the filter chooses among them.
/// </summary>
// The wall fills z from -4.5 to -4 with a doorway at x from -1 to 1. The door
// is a part in the doorway, a little thinner than the wall.
public sealed class SolidSpanPartTests
{
    private const float Exact = 1e-4f;

    private static readonly Vector3 Near = new(0f, 1f, 0f);
    private static readonly Vector3 Far = new(0f, 1f, -8f);
    private static readonly Vector3 DoorCenter = new(0f, 1.2f, -4.25f);

    [Fact]
    public void A_closed_door_blocks_the_doorway()
    {
        SpanLevel level = Room(out _);

        SolidSpan span = level.Trace(Near, Far).ShouldHaveSingleItem();

        span.Start.ShouldBe(4.05f, Exact);
        span.End.ShouldBe(4.45f, Exact);
        span.Material.ShouldBe(SpanLevel.Wood);
    }

    [Fact]
    public void A_door_that_has_slid_out_of_the_doorway_no_longer_blocks_it()
    {
        SpanLevel level = Room(out SceneNode door);
        level.Trace(Near, Far).Length.ShouldBe(1);

        // Into the wall beside the doorway. Nothing is compiled again: a part
        // is read where its node is.
        door.LocalPosition += new Vector3(2.2f, 0f, 0f);

        level.Trace(Near, Far).ShouldBeEmpty();
    }

    [Fact]
    public void A_door_hidden_inside_the_wall_adds_nothing_to_the_wall()
    {
        SpanLevel level = Room(out SceneNode door);
        door.LocalPosition += new Vector3(2.2f, 0f, 0f);
        var aside = new Vector3(2f, 0f, 0f);

        SolidSpan span = level.Trace(Near + aside, Far + aside).ShouldHaveSingleItem();

        span.Start.ShouldBe(4f, Exact);
        span.End.ShouldBe(4.5f, Exact);
        span.Material.ShouldBe(SpanLevel.Plaster);
    }

    [Fact]
    public void A_door_swung_open_is_met_edge_on()
    {
        SpanLevel level = Room(out SceneNode door);
        door.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);

        // Turned about its middle, it is 0.4 wide and 2 deep along the line.
        SolidSpan span = level.Trace(Near, Far).ShouldHaveSingleItem();
        span.Start.ShouldBe(3.25f, Exact);
        span.End.ShouldBe(5.25f, Exact);

        var aside = new Vector3(0.5f, 0f, 0f);
        level.Trace(Near + aside, Far + aside).ShouldBeEmpty();
    }

    [Fact]
    public void A_part_under_a_moved_parent_is_met_where_the_parent_put_it()
    {
        var level = new SpanLevel();
        SceneNode group = level.Scene.Root.CreateChild("Group");
        SceneNode crate = level.Part("Crate", new Vector3(0f, 1f, -2f), new Vector3(0.5f), SpanLevel.Wood);
        group.AddChild(crate);

        level.Trace(Near, Far).ShouldHaveSingleItem().Start.ShouldBe(1.5f, Exact);

        group.LocalPosition = new Vector3(0f, 0f, -3f);

        level.Trace(Near, Far).ShouldHaveSingleItem().Start.ShouldBe(4.5f, Exact);
    }

    [Fact]
    public void A_part_on_a_scaled_node_is_met_at_the_size_it_is_drawn()
    {
        var level = new SpanLevel();
        SceneNode crate = level.Part("Crate", new Vector3(0f, 1f, -2f), new Vector3(0.5f), SpanLevel.Wood);
        crate.LocalScale = new Vector3(1f, 1f, 3f);

        SolidSpan span = level.Trace(Near, Far).ShouldHaveSingleItem();

        // Three times as deep, and still measured in world units.
        span.Start.ShouldBe(0.5f, Exact);
        span.End.ShouldBe(3.5f, Exact);
    }

    [Fact]
    public void A_trigger_never_blocks()
    {
        SpanLevel level = Room(out SceneNode door);
        door.LocalPosition += new Vector3(0f, 10f, 0f);

        SceneNode trigger = level.Part("Trigger", new Vector3(0f, 1.2f, -3f), new Vector3(1f, 1.2f, 1f));
        trigger.CanCollide = false;
        trigger.CanQuery = false;
        trigger.IsRendered = false;

        level.Trace(Near, Far).ShouldBeEmpty();
        level.Trace(Near, Far, new SceneQueryFilter { IgnoreQueryFlags = true }).ShouldBeEmpty();
    }

    [Fact]
    public void A_part_that_can_be_queried_but_does_not_collide_does_not_block()
    {
        var level = new SpanLevel();
        SceneNode ghost = level.Part("Ghost", new Vector3(0f, 1f, -2f), new Vector3(0.5f));
        ghost.CanCollide = false;

        level.Trace(Near, Far).ShouldBeEmpty();
    }

    [Fact]
    public void A_part_hidden_from_queries_blocks_only_when_the_filter_ignores_the_query_flag()
    {
        var level = new SpanLevel();
        SceneNode clip = level.Part("Clip", new Vector3(0f, 1f, -2f), new Vector3(0.5f));
        clip.CanQuery = false;

        level.Trace(Near, Far).ShouldBeEmpty();
        level.Trace(Near, Far, new SceneQueryFilter { IgnoreQueryFlags = true }).Length.ShouldBe(1);
    }

    [Fact]
    public void A_part_the_filter_ignores_does_not_block()
    {
        SpanLevel level = Room(out SceneNode door);

        level.Trace(Near, Far, new SceneQueryFilter { Ignore = [door] }).ShouldBeEmpty();
    }

    [Fact]
    public void A_subtractive_part_changes_nothing()
    {
        SpanLevel level = Room(out SceneNode door);
        door.LocalPosition += new Vector3(0f, 10f, 0f);
        var aside = new Vector3(3f, 0f, 0f);

        // One across the wall and one in open air.
        level.SubtractivePart("HoleInWall", new Vector3(3f, 1f, -4.25f), new Vector3(0.5f, 0.5f, 1f));
        level.SubtractivePart("HoleInAir", new Vector3(0f, 1f, -2f), new Vector3(0.5f));

        var everything = new SceneQueryFilter { IgnoreQueryFlags = true, IncludeSubtractiveBrushes = true };

        level.Trace(Near, Far).ShouldBeEmpty();
        level.Trace(Near, Far, in everything).ShouldBeEmpty();

        SolidSpan wall = level.Trace(Near + aside, Far + aside, in everything).ShouldHaveSingleItem();
        wall.Start.ShouldBe(4f, Exact);
        wall.End.ShouldBe(4.5f, Exact);
    }

    [Fact]
    public void A_part_standing_half_in_a_wall_adds_only_what_sticks_out()
    {
        var level = new SpanLevel();
        level.Wall();
        level.Compile();
        level.Part("Shelf", new Vector3(0f, 1f, -3.9f), new Vector3(0.5f, 0.5f, 0.3f), SpanLevel.Wood);

        SolidSpan[] spans = level.Trace(Near, Far);

        spans.Length.ShouldBe(2);
        spans[0].Start.ShouldBe(3.6f, Exact);
        spans[0].End.ShouldBe(4f, Exact);
        spans[0].Material.ShouldBe(SpanLevel.Wood);
        spans[1].Start.ShouldBe(spans[0].End);
        spans[1].End.ShouldBe(4.5f, Exact);
        spans[1].Material.ShouldBe(SpanLevel.Plaster);
    }

    [Fact]
    public void A_filter_that_leaves_the_static_world_out_sees_only_parts()
    {
        SpanLevel level = Room(out _);
        var aside = new Vector3(3f, 0f, 0f);
        var partsOnly = new SceneQueryFilter { ExcludeStaticWorldBrushes = true };

        level.Trace(Near + aside, Far + aside, in partsOnly).ShouldBeEmpty();
        level.Trace(Near, Far, in partsOnly).ShouldHaveSingleItem().Material.ShouldBe(SpanLevel.Wood);
    }

    [Fact]
    public void A_mesh_does_not_block()
    {
        var level = new SpanLevel();
        SpatialTestHelpers.CreateMeshNode(level.Scene.Root, "Statue", new Vector3(0f, 1f, -2f));

        level.Scene.Raycast(new Ray3(Near, -Vector3.UnitZ), out _).ShouldBeTrue("the mesh is on the line");
        level.Trace(Near, Far).ShouldBeEmpty();
    }

    [Fact]
    public void A_filter_naming_a_group_that_does_not_exist_is_refused()
    {
        var level = new SpanLevel();
        var filter = new SceneQueryFilter { Groups = level.Scene.CollisionGroups, CollisionGroup = 40 };

        Should.Throw<ArgumentOutOfRangeException>(() => level.Trace(Near, Far, in filter));
    }

    private static SpanLevel Room(out SceneNode door)
    {
        var level = new SpanLevel();
        level.Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(6f, 0.5f, 6f), SpanLevel.Tile);
        level.Wall();
        level.Doorway();
        level.Compile();

        door = level.Part("Door", DoorCenter, new Vector3(1f, 1.2f, 0.2f), SpanLevel.Wood);
        return level;
    }
}
