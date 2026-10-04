using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Per-node <see cref="PhysicsFlags"/>, collision groups, and the box and sphere overlap queries.
/// </summary>
public sealed class SceneQueryFlagTests
{
    [Fact]
    public void A_node_starts_solid_queryable_touchable_and_anchored()
    {
        var node = new SceneNode("n");

        node.CanCollide.ShouldBeTrue();
        node.CanQuery.ShouldBeTrue();
        node.CanTouch.ShouldBeTrue();
        node.Anchored.ShouldBeTrue("nothing simulates until asked — deliberately unlike a Roblox Part");
        node.PhysicsFlags.HasFlag(PhysicsFlags.HasBody).ShouldBeFalse();
    }

    [Fact]
    public void CanQuery_is_independent_of_CanCollide()
    {
        // Unlike Roblox, where CanQuery only takes effect with CanCollide off.
        var node = new SceneNode("n") { CanQuery = false };

        node.CanCollide.ShouldBeTrue();
        node.CanQuery.ShouldBeFalse();
    }

    [Fact]
    public void Flag_writes_do_not_dirty_the_static_world()
    {
        var scene = new Scene("Test");
        SceneNode brush = scene.Root.CreateChild("wall");
        brush.Brush = UnitBrush();
        scene.RebuildStaticWorld(new FakeRenderer());
        scene.StaticWorldDirty.ShouldBeFalse();

        brush.CanCollide = false;
        brush.CanQuery = false;
        brush.CanTouch = false;
        brush.Anchored = false;
        brush.CollisionGroup = 3;

        scene.StaticWorldDirty.ShouldBeFalse();
    }

    [Fact]
    public void A_raycast_skips_a_node_that_cannot_be_queried()
    {
        var (scene, near, far) = TwoBrushesInALine();

        near.CanQuery = false;

        scene.Raycast(RayAlongX(), out SceneRaycastHit hit).ShouldBeTrue();
        hit.Node.ShouldBeSameAs(far, "the near brush opted out of queries");
    }

    [Fact]
    public void CanQuery_is_honoured_on_a_static_WORLD_brush()
    {
        // Scene.Raycast walks the spatial index per node, not the compiled
        // BSP, so the flag works on world brushes too.
        var (scene, near, far) = TwoBrushesInALine();
        near.BrushKind.ShouldBe(BrushKind.World);

        near.CanQuery = false;

        scene.Raycast(RayAlongX(), out SceneRaycastHit hit).ShouldBeTrue();
        hit.Node.ShouldBeSameAs(far);
    }

    [Fact]
    public void Editor_picking_disregards_the_query_flags()
    {
        // Otherwise clearing CanQuery would make the node unselectable.
        var (scene, near, _) = TwoBrushesInALine();
        near.CanQuery = false;

        scene.Raycast(RayAlongX(), out SceneRaycastHit hit, SceneQueryFilter.EditorPicking).ShouldBeTrue();

        hit.Node.ShouldBeSameAs(near);
    }

    [Fact]
    public void RespectCanCollide_reproduces_the_Roblox_coupling_on_request()
    {
        var (scene, near, far) = TwoBrushesInALine();
        near.CanCollide = false;

        scene.Raycast(RayAlongX(), out SceneRaycastHit unfiltered).ShouldBeTrue();
        unfiltered.Node.ShouldBeSameAs(near, "the two flags are independent by default");

        var roblox = new SceneQueryFilter { RespectCanCollide = true };
        scene.Raycast(RayAlongX(), out SceneRaycastHit filtered, roblox).ShouldBeTrue();
        filtered.Node.ShouldBeSameAs(far);
    }

    [Fact]
    public void An_ignore_list_excludes_the_caster()
    {
        var (scene, near, far) = TwoBrushesInALine();

        var filter = new SceneQueryFilter { Ignore = new[] { near } };
        scene.Raycast(RayAlongX(), out SceneRaycastHit hit, filter).ShouldBeTrue();

        hit.Node.ShouldBeSameAs(far);
    }

    [Fact]
    public void Everything_collides_until_a_pair_is_disabled()
    {
        var groups = new CollisionGroups();
        int players = groups.Register("Players");
        int scenery = groups.Register("Scenery");

        groups.AreCollidable(players, scenery).ShouldBeTrue();
        groups.AreCollidable(CollisionGroups.DefaultGroup, players).ShouldBeTrue();
    }

    [Fact]
    public void Disabling_a_pair_is_symmetric()
    {
        var groups = new CollisionGroups();
        int a = groups.Register("A");
        int b = groups.Register("B");

        groups.SetCollidable(a, b, false);

        groups.AreCollidable(a, b).ShouldBeFalse();
        groups.AreCollidable(b, a).ShouldBeFalse();
        (groups.GetMask(a) & (1UL << b)).ShouldBe(0UL);
        (groups.GetMask(b) & (1UL << a)).ShouldBe(0UL);
    }

    [Fact]
    public void Registering_the_same_name_twice_returns_the_same_id()
    {
        var groups = new CollisionGroups();

        groups.Register("Players").ShouldBe(groups.Register("Players"));
        groups.Count.ShouldBe(2, "Default plus Players");
    }

    [Fact]
    public void The_sixty_fifth_group_is_a_named_error()
    {
        var groups = new CollisionGroups();
        for (int i = 1; i < CollisionGroups.MaxGroups; i++)
            groups.Register($"G{i}");
        groups.Count.ShouldBe(CollisionGroups.MaxGroups);

        Should.Throw<InvalidOperationException>(() => groups.Register("OneTooMany"))
            .Message.ShouldContain("OneTooMany");
    }

    [Fact]
    public void A_query_can_filter_by_collision_group()
    {
        var (scene, near, far) = TwoBrushesInALine();
        int rays = scene.CollisionGroups.Register("Rays");
        int transparent = scene.CollisionGroups.Register("Transparent");
        scene.CollisionGroups.SetCollidable(rays, transparent, false);
        near.CollisionGroup = transparent;

        var filter = new SceneQueryFilter { Groups = scene.CollisionGroups, CollisionGroup = rays };
        scene.Raycast(RayAlongX(), out SceneRaycastHit hit, filter).ShouldBeTrue();

        hit.Node.ShouldBeSameAs(far);
    }

    [Fact]
    public void An_unregistered_node_group_is_answered_not_thrown()
    {
        // A node may get a group id before the scene has registered a name for
        // it (set while detached, or restored by a loader). The filter must not
        // throw on that from inside the BVH walk.
        var (scene, near, _) = TwoBrushesInALine();
        int rays = scene.CollisionGroups.Register("Rays");
        near.CollisionGroup = 40;   // legal on the node, never registered

        var filter = new SceneQueryFilter { Groups = scene.CollisionGroups, CollisionGroup = rays };

        scene.Raycast(RayAlongX(), out SceneRaycastHit hit, filter).ShouldBeTrue();
        hit.Node.ShouldBeSameAs(near, "an unnamed group interacts, per its all-ones mask");

        var results = new List<SceneNode>();
        scene.GetPartBoundsInRadius(Vector3.Zero, 3f, results, filter);
        results.ShouldContain(near);
    }

    [Fact]
    public void An_unregistered_QUERY_group_is_reported_at_the_call_site()
    {
        // A caller naming an unregistered group is a mistake, reported before
        // any traversal.
        var (scene, _, _) = TwoBrushesInALine();
        var filter = new SceneQueryFilter { Groups = scene.CollisionGroups, CollisionGroup = 40 };
        var results = new List<SceneNode>();

        Should.Throw<ArgumentOutOfRangeException>(
            () => scene.Raycast(RayAlongX(), out _, filter));
        Should.Throw<ArgumentOutOfRangeException>(
            () => scene.GetPartBoundsInRadius(Vector3.Zero, 3f, results, filter));

        results.ShouldBeEmpty("nothing was traversed, so nothing was appended");
    }

    [Fact]
    public void A_subtractive_brush_is_not_hit_by_a_query()
    {
        var scene = new Scene("Test");
        SceneNode hole = scene.Root.CreateChild("hole");
        hole.Brush = UnitBrush().WithOperation(BrushOperation.Subtractive);

        scene.Raycast(RayAlongX(), out _).ShouldBeFalse();

        var results = new List<SceneNode>();
        scene.GetPartBoundsInRadius(Vector3.Zero, 3f, results);
        results.ShouldNotContain(hole);
    }

    [Fact]
    public void Editor_picking_can_still_select_a_subtractive_brush()
    {
        var scene = new Scene("Test");
        SceneNode hole = scene.Root.CreateChild("hole");
        hole.Brush = UnitBrush().WithOperation(BrushOperation.Subtractive);

        scene.Raycast(RayAlongX(), out SceneRaycastHit hit, SceneQueryFilter.EditorPicking)
            .ShouldBeTrue();

        hit.Node.ShouldBeSameAs(hole);
    }

    [Fact]
    public void A_box_query_finds_the_nodes_whose_bounds_it_overlaps()
    {
        var (scene, near, far) = TwoBrushesInALine();
        var results = new List<SceneNode>();

        scene.GetPartBoundsInBox(new Aabb(new Vector3(-2f, -2f, -2f), new Vector3(2f, 2f, 2f)), results);

        results.ShouldContain(near);
        results.ShouldNotContain(far);
    }

    [Fact]
    public void A_box_query_that_overlaps_nothing_reports_nothing()
    {
        var (scene, _, _) = TwoBrushesInALine();
        var results = new List<SceneNode>();

        scene.GetPartBoundsInBox(
            new Aabb(new Vector3(100f, 100f, 100f), new Vector3(101f, 101f, 101f)), results);

        results.ShouldBeEmpty();
    }

    [Fact]
    public void A_sphere_query_finds_the_nodes_it_reaches_and_no_more()
    {
        var (scene, near, far) = TwoBrushesInALine();
        var results = new List<SceneNode>();

        scene.GetPartBoundsInRadius(Vector3.Zero, 3f, results);

        results.ShouldContain(near);
        results.ShouldNotContain(far);

        results.Clear();
        scene.GetPartBoundsInRadius(Vector3.Zero, 20f, results);
        results.ShouldContain(far);
    }

    [Fact]
    public void A_sphere_query_measures_from_the_box_not_from_its_centre()
    {
        var scene = new Scene("Test");
        SceneNode wide = scene.Root.CreateChild("wide");
        wide.Brush = Brush.CreateBox(new Vector3(-10f, -1f, -1f), new Vector3(10f, 1f, 1f));
        wide.LocalPosition = new Vector3(20f, 0f, 0f);   // spans x in [10, 30]
        var results = new List<SceneNode>();

        scene.GetPartBoundsInRadius(Vector3.Zero, 11f, results);

        results.ShouldContain(wide, "the sphere reaches the box's near face at x=10");
    }

    [Fact]
    public void Overlap_queries_honour_the_filter()
    {
        var (scene, near, _) = TwoBrushesInALine();
        near.CanQuery = false;
        var results = new List<SceneNode>();

        scene.GetPartBoundsInBox(
            new Aabb(new Vector3(-2f, -2f, -2f), new Vector3(2f, 2f, 2f)), results);
        results.ShouldNotContain(near);

        results.Clear();
        scene.GetPartBoundsInBox(
            new Aabb(new Vector3(-2f, -2f, -2f), new Vector3(2f, 2f, 2f)),
            results, SceneQueryFilter.EditorPicking);
        results.ShouldContain(near);
    }

    [Fact]
    public void An_overlap_query_finds_part_brushes_as_readily_as_world_ones()
    {
        var scene = new Scene("Test");
        SceneNode part = scene.Root.CreateChild("part");
        part.BrushKind = BrushKind.Part;
        part.Brush = UnitBrush();
        var results = new List<SceneNode>();

        scene.GetPartBoundsInRadius(Vector3.Zero, 2f, results);

        results.ShouldContain(part);
    }

    [Fact]
    public void An_overlap_query_follows_a_node_that_moves()
    {
        var scene = new Scene("Test");
        SceneNode node = scene.Root.CreateChild("mover");
        node.Brush = UnitBrush();
        var results = new List<SceneNode>();

        node.LocalPosition = new Vector3(50f, 0f, 0f);
        scene.GetPartBoundsInRadius(Vector3.Zero, 2f, results);
        results.ShouldBeEmpty("the node moved away");

        node.LocalPosition = Vector3.Zero;
        scene.GetPartBoundsInRadius(Vector3.Zero, 2f, results);
        results.ShouldContain(node);
    }

    private static Ray3 RayAlongX() => new(new Vector3(-20f, 0f, 0f), Vector3.UnitX);

    private static Brush UnitBrush() =>
        Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));

    // Unit brushes at x = 0 and x = 10. A ray from -x reaches the far one only
    // when the near one is filtered out.
    private static (Scene Scene, SceneNode Near, SceneNode Far) TwoBrushesInALine()
    {
        var scene = new Scene("Test");
        SceneNode near = scene.Root.CreateChild("near");
        near.Brush = UnitBrush();
        SceneNode far = scene.Root.CreateChild("far");
        far.Brush = UnitBrush();
        far.LocalPosition = new Vector3(10f, 0f, 0f);
        return (scene, near, far);
    }
}
