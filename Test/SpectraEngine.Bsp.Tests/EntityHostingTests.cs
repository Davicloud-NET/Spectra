using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The entity world as <see cref="SceneManager"/> hosts it: started at play
/// entry, stopped at play exit and on a scene replace.
/// </summary>
public sealed class EntityHostingTests
{
    private const float Tick = 1f / 60f;

    [Fact]
    public void Every_entity_spawns_before_any_of_them_activates()
    {
        // Two walks: with one, the first node activates before the last
        // has spawned.
        var log = new List<string>();
        var scene = new Scene("Entities");
        EntityRuntime.Place(scene.Root, "a", "lifecycle");
        EntityRuntime.Place(scene.Root, "b", "lifecycle");
        EntityRuntime.Place(scene.Root, "c", "lifecycle");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        log.ShouldBe(new[]
        {
            "spawn:a", "spawn:b", "spawn:c",
            "activate:a", "activate:b", "activate:c",
        });

        log.Clear();
        world.Deactivate();
        log.ShouldBe(new[] { "remove:a", "remove:b", "remove:c" });
    }

    [Fact]
    public void A_tick_advances_world_time_by_exactly_the_step_it_is_given()
    {
        // Ticked with the fixed delta, not the frame delta: the heap is keyed
        // on absolute fire times.
        var scene = new Scene("Entities");
        var world = new EntityWorld(scene, new CapturingLogger(), new EntityCatalog());
        world.Activate();

        for (int i = 0; i < 60; i++)
            world.Tick(Tick);

        world.Time.ShouldBe(60f * Tick, 1e-4f);
    }

    [Fact]
    public void The_play_mode_boundary_builds_the_runtime_and_takes_it_away_again()
    {
        var log = new List<string>();
        SceneManager manager = Hosted(log);
        Scene scene = manager.ActiveScene.ShouldNotBeNull();
        EntityRuntime.Place(scene.Root, "door", "lifecycle");

        manager.EntityWorld.ShouldBeNull();

        manager.StartEntityWorld();

        EntityWorld world = manager.EntityWorld.ShouldNotBeNull();
        world.IsActive.ShouldBeTrue();
        world.Entities.ShouldHaveSingleItem().TargetName.ShouldBe("door");
        log.ShouldBe(new[] { "spawn:door", "activate:door" });

        manager.StopEntityWorld();

        manager.EntityWorld.ShouldBeNull();
        world.IsActive.ShouldBeFalse();
        log[^1].ShouldBe("remove:door");
    }

    [Fact]
    public void Opening_a_map_while_play_mode_is_running_tears_the_entity_world_down()
    {
        // A map open replaces the graph in place. A world left running holds
        // entities bound to nodes that are in no scene.
        var log = new List<string>();
        SceneManager manager = Hosted(log);
        Scene scene = manager.ActiveScene.ShouldNotBeNull();
        SceneNode authored = EntityRuntime.Place(scene.Root, "door", "lifecycle");

        manager.StartEntityWorld();
        EntityWorld world = manager.EntityWorld.ShouldNotBeNull();

        // Same order as EditorSession.OpenMap: stop the runtime, then replace.
        manager.OnSceneReplaced();
        MapSceneBinder.ApplyTo(MapSceneBinder.FromScene(scene), scene);

        manager.EntityWorld.ShouldBeNull();
        world.IsActive.ShouldBeFalse();
        world.Entities.ShouldBeEmpty();
        world.Index.ShouldBeNull();
        log[^1].ShouldBe("remove:door");

        // The reloaded node has the same id and is a different object.
        SceneNode reloaded = scene.Root.Children[^1];
        reloaded.Id.ShouldBe(authored.Id);
        ReferenceEquals(reloaded, authored).ShouldBeFalse();
    }

    [Fact]
    public void A_world_kept_across_a_map_load_is_rebound_onto_the_new_nodes()
    {
        // Why the teardown matters: node ids survive a map load, so the
        // target-name index repoints every stale entity at the fresh node and
        // last map's state runs over this map's scene.
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode authored = EntityRuntime.Place(scene.Root, "door", "lifecycle");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        Entity entity = world.Entities.ShouldHaveSingleItem();

        MapSceneBinder.ApplyTo(MapSceneBinder.FromScene(scene), scene);

        SceneNode reloaded = scene.Root.Children.ShouldHaveSingleItem();
        ReferenceEquals(reloaded, authored).ShouldBeFalse();
        ReferenceEquals(entity.Node, reloaded).ShouldBeTrue();

        // It never spawned into the new scene, so nothing reports it.
        log.ShouldBe(new[] { "spawn:door", "activate:door" });
    }

    // Own catalogue: EntityCatalog.Shared freezes on first read, which would
    // make the tests order-dependent.
    private static SceneManager Hosted(List<string> log)
    {
        var manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = EntityRuntime.Catalog(log),
        };

        manager.LoadStartupScene(new FakeRenderer(), new AssetManager(NullLogger<AssetManager>.Instance));
        return manager;
    }
}
