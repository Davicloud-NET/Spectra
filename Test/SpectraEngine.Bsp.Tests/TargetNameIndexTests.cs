using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// How a wire's target name resolves: runtime forms, trailing star, duplicates,
/// and staying current as the scene changes.
/// </summary>
public sealed class TargetNameIndexTests
{
    private const float Tick = 1f / 60f;

    [Fact]
    public void The_runtime_forms_resolve_to_self_the_activator_and_the_caller()
    {
        // On a wire !self and !caller are the same entity: the one firing it.
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        SceneNode player = EntityRuntime.Place(scene.Root, "player", "recorder");
        EntityRuntime.Wire(source, "OnGo", "!self", "Ping");
        EntityRuntime.Wire(source, "OnGo", "!caller", "Ping");
        EntityRuntime.Wire(source, "OnGo", "!activator", "Ping");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        EntityRuntime.Live(world, source).FireOutput("OnGo", EntityRuntime.Live(world, player));
        world.Tick(Tick);

        log.Count.ShouldBe(3);
        log[0].ShouldStartWith("source:Ping");
        log[1].ShouldStartWith("source:Ping");
        log[2].ShouldStartWith("player:Ping");
        log.ShouldAllBe(entry => entry.EndsWith(":player:source", StringComparison.Ordinal));
    }

    [Fact]
    public void A_trailing_star_matches_by_prefix_and_leaves_everything_else_alone()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        EntityRuntime.Place(scene.Root, "door_north", "recorder");
        EntityRuntime.Place(scene.Root, "door_south", "recorder");
        EntityRuntime.Place(scene.Root, "hall", "recorder");
        EntityRuntime.Wire(source, "OnGo", "door*", "Ping");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        EntityRuntime.Live(world, source).FireOutput("OnGo");
        world.Tick(Tick);

        log.Count.ShouldBe(2);
        log[0].ShouldStartWith("door_north:Ping");
        log[1].ShouldStartWith("door_south:Ping");
    }

    [Fact]
    public void A_prefix_match_comes_back_in_traversal_order_whatever_order_the_names_appeared_in()
    {
        // Renaming the first door creates its bucket last, so bucket order and
        // traversal order disagree.
        var scene = new Scene("Entities");
        SceneNode first = EntityRuntime.Place(scene.Root, "door_north", "recorder");
        EntityRuntime.Place(scene.Root, "door_south", "recorder");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();

        first.Name = "door_west";

        var results = new List<Entity>();
        world.Index!.Resolve("door*", null, null, null, results);

        results.Count.ShouldBe(2);
        results[0].TargetName.ShouldBe("door_west");
        results[1].TargetName.ShouldBe("door_south");
    }

    [Fact]
    public void Duplicate_names_are_legal_and_firing_at_one_fires_every_match_in_traversal_order()
    {
        // The nested lamp is created first but comes second in traversal order.
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        SceneNode group = scene.Root.CreateChild("GroupA");
        SceneNode nested = EntityRuntime.Place(group, "lamp", "recorder");
        nested.Entity!.SetValue("tag", "nested");
        SceneNode top = EntityRuntime.Place(scene.Root, "lamp", "recorder");
        top.Entity!.SetValue("tag", "top");
        EntityRuntime.Wire(source, "OnGo", "lamp", "Ping");

        // Root children become [source, lamp(top), GroupA].
        scene.Root.InsertChild(1, top);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        EntityRuntime.Live(world, source).FireOutput("OnGo");
        world.Tick(Tick);

        log.Count.ShouldBe(2);
        log[0].ShouldStartWith("top:Ping");
        log[1].ShouldStartWith("nested:Ping");
    }

    [Fact]
    public void A_rename_retargets_the_entity_through_the_scenes_own_event()
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "door", "recorder");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();

        Resolve(world, "door").Count.ShouldBe(1);

        node.Name = "gate";

        Resolve(world, "door").ShouldBeEmpty();
        Resolve(world, "gate").ShouldHaveSingleItem().Node.ShouldBeSameAs(node);
    }

    [Fact]
    public void A_node_removed_mid_session_leaves_the_index()
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "door", "recorder");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();
        Resolve(world, "door").Count.ShouldBe(1);

        scene.Root.RemoveChild(node);

        Resolve(world, "door").ShouldBeEmpty();
        // The id mapping stays, for an undo of the delete.
        world.Index!.EntityCount.ShouldBe(1);
    }

    [Fact]
    public void A_node_re_added_under_the_same_id_rejoins_the_index()
    {
        // Undo of a delete rebuilds the node as a new object with the old id.
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        SceneNode door = EntityRuntime.Place(scene.Root, "door", "recorder");
        EntityData authored = door.Entity!;
        EntityRuntime.Wire(source, "OnGo", "door", "Ping");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        Entity live = EntityRuntime.Live(world, door);

        scene.Root.RemoveChild(door);
        Resolve(world, "door").ShouldBeEmpty();

        var restored = new SceneNode("door", door.Id) { Entity = authored };
        scene.Root.AddChild(restored);

        Resolve(world, "door").ShouldHaveSingleItem().ShouldBeSameAs(live);
        live.Node.ShouldBeSameAs(restored);

        EntityRuntime.Live(world, source).FireOutput("OnGo");
        world.Tick(Tick);
        log.ShouldHaveSingleItem().ShouldStartWith("door:Ping");
    }

    [Fact]
    public void An_entity_re_added_under_a_different_name_is_listed_under_that_name_only()
    {
        var scene = new Scene("Entities");
        SceneNode door = EntityRuntime.Place(scene.Root, "door", "recorder");
        EntityData authored = door.Entity!;

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();

        scene.Root.RemoveChild(door);
        scene.Root.AddChild(new SceneNode("gate", door.Id) { Entity = authored });

        Resolve(world, "door").ShouldBeEmpty();
        Resolve(world, "gate").Count.ShouldBe(1);
    }

    [Fact]
    public void An_unknown_target_resolves_to_nothing_rather_than_to_everything()
    {
        var scene = new Scene("Entities");
        EntityRuntime.Place(scene.Root, "door", "recorder");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();

        Resolve(world, "hall").ShouldBeEmpty();
        Resolve(world, "").ShouldBeEmpty();
        Resolve(world, "!nonsense").ShouldBeEmpty();
        // A bare star is a zero-length prefix.
        Resolve(world, "*").Count.ShouldBe(1);
    }

    private static List<Entity> Resolve(EntityWorld world, string target)
    {
        var results = new List<Entity>();
        world.Index!.Resolve(target, null, null, null, results);
        return results;
    }
}
