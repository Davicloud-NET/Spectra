using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Tests;

public sealed class LogicAutoTests
{
    private const float Tick = 1f / 60f;

    [Fact]
    public void OnMapSpawn_is_queued_at_activation_and_arrives_once_on_the_first_tick()
    {
        var log = new List<string>();
        Scene scene = SceneWithAuto();

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        log.ShouldBeEmpty("an output is queued, never delivered inside Activate");

        world.Tick(Tick);
        log.ShouldBe(["sink:Begin:"]);

        world.Tick(Tick);
        world.Tick(Tick);
        log.Count.ShouldBe(1);
    }

    [Fact]
    public void OnMapSpawn_arrives_after_anything_an_entity_fired_while_spawning()
    {
        // The auto comes first in traversal order. Firing from OnSpawn would
        // put its output ahead of the spawner's.
        var log = new List<string>();
        Scene scene = SceneWithAuto();
        SceneNode spawner = EntityRuntime.Place(scene.Root, "spawner", "test_spawn_firer");
        EntityRuntime.Wire(spawner, SpawnFiringEntity.OnSpawned, "sink", "Spawned");

        EntityCatalog catalog = EntityRuntime.Catalog(log);
        catalog.Add(new EntitySchema("test_spawn_firer"), static () => new SpawnFiringEntity());

        var world = new EntityWorld(scene, new CapturingLogger(), catalog);
        world.Activate();
        world.Tick(Tick);

        log.ShouldBe(["sink:Spawned:", "sink:Begin:"]);
    }

    [Fact]
    public void A_delayed_wire_on_OnMapSpawn_counts_from_the_moment_the_map_begins()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode auto = EntityRuntime.Place(scene.Root, "auto", "logic_auto");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(auto, LogicAuto.OnMapSpawn, "sink", "Begin", delay: 0.5f);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        world.Tick(0.25f);
        log.ShouldBeEmpty();

        world.Tick(0.25f);
        log.ShouldBe(["sink:Begin:"]);
    }

    [Fact]
    public void Every_play_session_begins_the_map_again()
    {
        var log = new List<string>();
        Scene scene = SceneWithAuto();

        for (int session = 0; session < 2; session++)
        {
            var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
            world.Activate();
            world.Tick(Tick);
            world.Deactivate();
        }

        log.ShouldBe(["sink:Begin:", "sink:Begin:"]);
    }

    [Fact]
    public void A_logic_auto_spawned_into_a_running_world_fires_when_it_joins()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        world.Tick(Tick);

        SceneNode auto = EntityRuntime.Place(scene.Root, "auto", "logic_auto");
        EntityRuntime.Wire(auto, LogicAuto.OnMapSpawn, "sink", "Begin");
        world.QueueSpawn(auto);

        // Spawned at the end of this tick, delivered on the next.
        world.Tick(Tick);
        log.ShouldBeEmpty();

        world.Tick(Tick);
        log.ShouldBe(["sink:Begin:"]);
    }

    [Fact]
    public void A_logic_auto_accepts_no_input()
    {
        Scene scene = SceneWithAuto();

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();

        var auto = EntityRuntime.Live<LogicAuto>(world, scene.Root.Children[0]);
        EntityRuntime.Send(auto, "Trigger").ShouldBeFalse();
    }

    // An auto wired to a recorder, placed first so it leads the traversal.
    private static Scene SceneWithAuto()
    {
        var scene = new Scene("Entities");
        SceneNode auto = EntityRuntime.Place(scene.Root, "auto", "logic_auto");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(auto, LogicAuto.OnMapSpawn, "sink", "Begin");
        return scene;
    }
}
