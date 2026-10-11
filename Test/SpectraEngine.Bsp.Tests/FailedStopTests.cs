using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A level whose stop throws, because an entity's removal does. What the
/// level changed still goes back, and the next save holds the authored level.
/// </summary>
public sealed class FailedStopTests
{
    private static readonly Vector3 Authored = new(2.2f, 0.45f, -1.1f);

    [Fact]
    public void A_removal_that_throws_still_puts_back_what_the_level_changed()
    {
        var scene = new Scene("Stop");
        SceneNode door = BrokenLevel(scene.Root);
        byte[] authored = EntityMotion.Bits(door.LocalTransform);
        EntityWorld world = Played(scene);
        new ScriptWrites(world).TryWrite(door, SettableMemberId.Name, MemberValue.Of("gate"), out _).ShouldBeTrue();
        EntityMotion.Bits(door.LocalTransform).ShouldNotBe(authored);

        Should.Throw<InvalidOperationException>(world.Deactivate);

        EntityMotion.Bits(door.LocalTransform).ShouldBe(authored);
        door.Name.ShouldBe("door");
        world.Journal.Count.ShouldBe(0);
    }

    [Fact]
    public void A_world_whose_removal_threw_has_stopped_and_let_go_of_the_scene()
    {
        var scene = new Scene("Stop");
        BrokenLevel(scene.Root);
        EntityWorld world = Played(scene);

        Should.Throw<InvalidOperationException>(world.Deactivate);

        world.IsActive.ShouldBeFalse();
        world.Index.ShouldBeNull();
        world.Entities.ShouldBeEmpty();
        Should.Throw<InvalidOperationException>(() => world.QueueInput("door", "Open"));
    }

    [Fact]
    public void A_stop_that_throws_leaves_the_manager_with_no_entity_world()
    {
        SceneManager manager = Hosted();
        BrokenLevel(manager.ActiveScene.ShouldNotBeNull().Root);
        manager.StartEntityWorld();

        Should.Throw<InvalidOperationException>(manager.StopEntityWorld);

        manager.EntityWorld.ShouldBeNull();
    }

    [Fact]
    public void A_play_session_whose_stop_threw_is_over()
    {
        SceneManager manager = Hosted();
        Scene scene = manager.ActiveScene.ShouldNotBeNull();
        BrokenLevel(scene.Root);
        var session = new PlaySession(manager, new CharacterSimulation(scene));
        session.Enter();

        Should.Throw<InvalidOperationException>(session.Exit);

        session.IsActive.ShouldBeFalse();
        Should.NotThrow(() => session.Tick(EntityMotion.Dt, default));
    }

    [Fact]
    public void A_save_after_a_stop_that_threw_holds_the_authored_level()
    {
        SceneManager manager = Hosted();
        BrokenLevel(manager.ActiveScene.ShouldNotBeNull().Root);
        manager.StartEntityWorld();
        EntityWorld world = manager.EntityWorld.ShouldNotBeNull();
        for (int i = 0; i < 7; i++)
            world.Tick(EntityMotion.Dt);

        Should.Throw<InvalidOperationException>(() => manager.TakeAuthoredMap());
        MapDocument map = manager.TakeAuthoredMap().ShouldNotBeNull();

        map.Nodes.Single(node => node.Name == "door").Transform.Position.ShouldBe(Authored);
    }

    // The entity that throws comes first in the tree, so it is told to go
    // before the door is. Hands back the door, which slides.
    private static SceneNode BrokenLevel(SceneNode root)
    {
        EntityRuntime.Place(root, "broken", FailingRemovalEntity.Class);
        return EntityMotion.Slider(
            EntityMotion.BrushNode(root, "door", BrushKind.Part, Authored), new Vector3(0f, 1.9f, 0f), ticks: 20);
    }

    private static EntityWorld Played(Scene scene)
    {
        var world = new EntityWorld(
            scene, new CapturingLogger(), FailingRemovalEntity.AddTo(EntityMotion.Catalog([])));
        world.Activate();
        for (int i = 0; i < 7; i++)
            world.Tick(EntityMotion.Dt);

        return world;
    }

    private static SceneManager Hosted()
    {
        var manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = FailingRemovalEntity.AddTo(EntityMotion.Catalog([])),
        };

        manager.LoadStartupScene(new FakeRenderer(), new AssetManager(NullLogger<AssetManager>.Instance));
        return manager;
    }
}
