using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A running entity moves a node only through the world, and stopping puts
/// the level back as it was authored.
/// </summary>
public sealed class EntityWorldMotionTests
{
    private const float Dt = EntityMotion.Dt;

    [Fact]
    public void A_ticking_entity_moving_a_part_never_dirties_the_static_world()
    {
        var scene = new Scene("Motion");
        EntityMotion.BrushNode(scene.Root, "floor", BrushKind.World, new Vector3(0f, -1f, 0f));
        EntityMotion.BrushNode(scene.Root, "wall", BrushKind.World, new Vector3(3f, 0f, 0f));

        SceneNode door = EntityMotion.Slider(
            EntityMotion.BrushNode(scene.Root, "door", BrushKind.Part, new Vector3(0f, 0.5f, 0f)),
            new Vector3(0f, 2f, 0f), ticks: 30);

        // A group that moves the parts below it.
        SceneNode lift = EntityMotion.Slider(scene.Root.CreateChild("lift"), new Vector3(4f, 0f, 0f), ticks: 30);
        EntityMotion.BrushNode(lift, "deck", BrushKind.Part, new Vector3(0f, 0f, 5f));
        EntityMotion.BrushNode(lift, "rail", BrushKind.Part, new Vector3(0f, 1f, 5f));

        var renderer = new FakeRenderer();
        scene.RebuildStaticWorld(renderer);
        int compiles = scene.StaticWorldCompileCount;
        Vector3 doorAuthored = door.LocalPosition;

        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();

        for (int i = 0; i < 200; i++)
        {
            world.Tick(Dt);

            scene.StaticWorldDirty.ShouldBeFalse($"tick {i + 1}");
            scene.StaticWorldCompileCount.ShouldBe(compiles, $"tick {i + 1}");
        }

        door.LocalPosition.ShouldNotBe(doorAuthored);
        world.MovedNodeCount.ShouldBe(2);
        world.RefusedMoveCount.ShouldBe(0);

        scene.RebuildStaticWorldIfDirty(renderer);
        scene.StaticWorldCompileCount.ShouldBe(compiles);
    }

    [Fact]
    public void Stopping_restores_every_moved_transform_bit_for_bit()
    {
        var scene = new Scene("Motion");

        SceneNode part = EntityMotion.Slider(
            EntityMotion.BrushNode(scene.Root, "part", BrushKind.Part, new Vector3(0.1f, 1f / 3f, -7.3f)),
            new Vector3(1.7f, 2.9f, -0.3f), ticks: 7);
        part.LocalRotation = Quaternion.CreateFromYawPitchRoll(0.3f, 0.2f, 0.1f);
        part.Entity!.SetValue("turn", "1.1");

        // No brush at or below it, so its scale may change too.
        SceneNode marker = EntityMotion.Slider(scene.Root.CreateChild("marker"), new Vector3(-5f, 0.7f, 11f), ticks: 13);
        marker.LocalScale = new Vector3(1.5f, 0.75f, 2f);
        marker.Entity!.SetValue("grow", "1.3");

        SceneNode still = EntityMotion.BrushNode(scene.Root, "still", BrushKind.Part, new Vector3(9f, 0f, 0f));

        SceneNode[] nodes = [part, marker, still];
        byte[][] authored = [.. nodes.Select(n => EntityMotion.Bits(n.LocalTransform))];

        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();
        for (int i = 0; i < 100; i++)
            world.Tick(Dt);

        EntityMotion.Bits(part.LocalTransform).ShouldNotBe(authored[0]);
        EntityMotion.Bits(marker.LocalTransform).ShouldNotBe(authored[1]);
        world.RefusedMoveCount.ShouldBe(0);

        world.Deactivate();

        for (int i = 0; i < nodes.Length; i++)
            EntityMotion.Bits(nodes[i].LocalTransform).ShouldBe(authored[i], nodes[i].Name);
        world.MovedNodeCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(40)]
    public void Stopping_mid_move_restores_the_authored_transform(int ticksPlayed)
    {
        var scene = new Scene("Motion");
        SceneNode door = EntityMotion.Slider(
            EntityMotion.BrushNode(scene.Root, "door", BrushKind.Part, new Vector3(2.2f, 0.45f, -1.1f)),
            new Vector3(0f, 1.9f, 0f), ticks: 20);
        door.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
        door.Entity!.SetValue("turn", "0.4");
        byte[] authored = EntityMotion.Bits(door.LocalTransform);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();
        for (int i = 0; i < ticksPlayed; i++)
            world.Tick(Dt);

        world.Deactivate();

        EntityMotion.Bits(door.LocalTransform).ShouldBe(authored);
    }

    [Fact]
    public void A_moved_parent_and_child_both_go_back()
    {
        var scene = new Scene("Motion");
        SceneNode train = EntityMotion.Slider(scene.Root.CreateChild("train"), new Vector3(6f, 0f, 0f), ticks: 11);
        train.LocalPosition = new Vector3(-3.3f, 0.2f, 1.9f);
        train.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.25f);

        SceneNode door = EntityMotion.Slider(
            EntityMotion.BrushNode(train, "door", BrushKind.Part, new Vector3(0.6f, 1.1f, 0f)),
            new Vector3(0f, 0f, 1.3f), ticks: 5);

        byte[] trainAuthored = EntityMotion.Bits(train.LocalTransform);
        byte[] doorAuthored = EntityMotion.Bits(door.LocalTransform);
        Matrix4x4 doorWorld = door.WorldMatrix;

        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();
        for (int i = 0; i < 33; i++)
            world.Tick(Dt);

        door.WorldMatrix.ShouldNotBe(doorWorld);
        world.MovedNodeCount.ShouldBe(2);

        world.Deactivate();

        EntityMotion.Bits(train.LocalTransform).ShouldBe(trainAuthored);
        EntityMotion.Bits(door.LocalTransform).ShouldBe(doorAuthored);
        door.WorldMatrix.ShouldBe(doorWorld);
    }

    [Fact]
    public void A_level_saves_the_same_bytes_after_play()
    {
        var manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = EntityMotion.Catalog([]),
        };
        manager.LoadStartupScene(new FakeRenderer(), new AssetManager(NullLogger<AssetManager>.Instance));
        Scene scene = manager.ActiveScene.ShouldNotBeNull();

        EntityMotion.Slider(
            EntityMotion.BrushNode(scene.Root, "door", BrushKind.Part, new Vector3(4f, 1f, 0f)),
            new Vector3(0f, 2f, 0f), ticks: 25);
        SceneNode lift = EntityMotion.Slider(scene.Root.CreateChild("lift"), new Vector3(0f, 3f, 0f), ticks: 40);
        EntityMotion.BrushNode(lift, "deck", BrushKind.Part, new Vector3(-6f, 0.25f, 2f));

        byte[] before = MapWriter.Write(MapSceneBinder.FromScene(scene));

        var session = new PlaySession(manager, new CharacterSimulation(scene)
        {
            SpawnPosition = manager.PlayerSpawn,
            FallOutHeight = manager.PlayerFallOutHeight,
        });
        session.Enter();
        for (int i = 0; i < 90; i++)
            session.Tick(Dt, default);

        // Saved now, the map would hold the door half open.
        MapWriter.Write(MapSceneBinder.FromScene(scene)).ShouldNotBe(before);

        session.Exit();

        MapWriter.Write(MapSceneBinder.FromScene(scene)).ShouldBe(before);
    }

    [Fact]
    public void A_move_above_a_world_brush_is_refused_and_named()
    {
        var logger = new CapturingLogger();
        var scene = new Scene("Motion");
        SceneNode lift = EntityMotion.Slider(scene.Root.CreateChild("lift"), new Vector3(0f, 3f, 0f));
        lift.LocalPosition = new Vector3(1f, 2f, 3f);
        EntityMotion.BrushNode(lift, "slab", BrushKind.World, Vector3.Zero);
        EntityMotion.BrushNode(lift, "rail", BrushKind.Part, new Vector3(0f, 1f, 0f));

        var renderer = new FakeRenderer();
        scene.RebuildStaticWorld(renderer);
        int compiles = scene.StaticWorldCompileCount;
        byte[] authored = EntityMotion.Bits(lift.LocalTransform);

        var world = new EntityWorld(scene, logger, EntityMotion.Catalog([]));
        world.Activate();
        for (int i = 0; i < 5; i++)
            world.Tick(Dt);

        EntityMotion.Bits(lift.LocalTransform).ShouldBe(authored);
        scene.StaticWorldDirty.ShouldBeFalse();
        scene.StaticWorldCompileCount.ShouldBe(compiles);

        ((SliderEntity)EntityRuntime.Live(world, lift)).LastMoveAccepted.ShouldBeFalse();
        world.RefusedMoveCount.ShouldBe(5);
        world.MovedNodeCount.ShouldBe(0);

        // Once per node, not once per tick.
        string error = logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem();
        error.ShouldContain("'lift'");
        error.ShouldContain("world brush");
    }

    [Fact]
    public void A_move_of_a_world_brush_itself_is_refused()
    {
        var logger = new CapturingLogger();
        var scene = new Scene("Motion");
        SceneNode pillar = EntityMotion.Slider(
            EntityMotion.BrushNode(scene.Root, "pillar", BrushKind.World, new Vector3(0f, 1f, 0f)),
            new Vector3(0f, 3f, 0f));
        scene.RebuildStaticWorld(new FakeRenderer());

        var world = new EntityWorld(scene, logger, EntityMotion.Catalog([]));
        world.Activate();
        world.Tick(Dt);

        pillar.LocalPosition.ShouldBe(new Vector3(0f, 1f, 0f));
        scene.StaticWorldDirty.ShouldBeFalse();
        logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem().ShouldContain("'pillar'");
    }

    [Fact]
    public void A_scale_change_above_a_brush_is_refused_and_named()
    {
        var logger = new CapturingLogger();
        var scene = new Scene("Motion");
        SceneNode group = EntityMotion.Slider(scene.Root.CreateChild("swelling"), new Vector3(0f, 1f, 0f));
        group.Entity!.SetValue("grow", "2");
        EntityMotion.BrushNode(group, "part", BrushKind.Part, Vector3.Zero);
        byte[] authored = EntityMotion.Bits(group.LocalTransform);

        var world = new EntityWorld(scene, logger, EntityMotion.Catalog([]));
        world.Activate();
        for (int i = 0; i < 3; i++)
            world.Tick(Dt);

        EntityMotion.Bits(group.LocalTransform).ShouldBe(authored);
        world.RefusedMoveCount.ShouldBe(3);

        string error = logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem();
        error.ShouldContain("'swelling'");
        error.ShouldContain("scale");
    }

    [Fact]
    public void An_entity_being_removed_still_sees_the_pose_it_moved_to()
    {
        var scene = new Scene("Motion");
        SceneNode door = EntityMotion.Slider(
            EntityMotion.BrushNode(scene.Root, "door", BrushKind.Part, new Vector3(0f, 0.5f, 0f)),
            new Vector3(0f, 2f, 0f), ticks: 20);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();
        var slider = (SliderEntity)EntityRuntime.Live(world, door);
        for (int i = 0; i < 20; i++)
            world.Tick(Dt);

        world.Deactivate();

        slider.SeenOnRemove.ShouldNotBeNull().Position.ShouldBe(new Vector3(0f, 2.5f, 0f));
        door.LocalPosition.ShouldBe(new Vector3(0f, 0.5f, 0f));
    }

    [Fact]
    public void A_move_made_while_being_removed_is_put_back_too()
    {
        var scene = new Scene("Motion");
        SceneNode door = EntityMotion.Slider(
            EntityMotion.BrushNode(scene.Root, "door", BrushKind.Part, new Vector3(0f, 0.5f, 0f)),
            new Vector3(0f, 2f, 0f));
        byte[] authored = EntityMotion.Bits(door.LocalTransform);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();

        // No tick has run, so OnRemove makes the first move of this node.
        ((SliderEntity)EntityRuntime.Live(world, door)).MoveOnRemove = new Vector3(50f, 50f, 50f);

        world.Deactivate();

        EntityMotion.Bits(door.LocalTransform).ShouldBe(authored);
    }

    [Fact]
    public void A_node_cannot_be_moved_through_a_world_that_is_not_running()
    {
        var scene = new Scene("Motion");
        SceneNode part = EntityMotion.BrushNode(scene.Root, "part", BrushKind.Part, Vector3.Zero);
        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog([]));
        var elsewhere = new Transform { Position = new Vector3(1f, 0f, 0f) };

        Should.Throw<InvalidOperationException>(() => world.SetLocalTransform(part, in elsewhere));

        world.Activate();
        world.SetLocalTransform(part, in elsewhere).ShouldBeTrue();
        world.Deactivate();

        Should.Throw<InvalidOperationException>(() => world.SetLocalTransform(part, in elsewhere));
        part.LocalPosition.ShouldBe(Vector3.Zero);
    }

    [Fact]
    public void Activating_twice_gives_the_same_tick_by_tick_transforms()
    {
        var scene = new Scene("Motion");
        SceneNode train = EntityMotion.Slider(scene.Root.CreateChild("train"), new Vector3(6f, 0f, 0f), ticks: 11);
        SceneNode door = EntityMotion.Slider(
            EntityMotion.BrushNode(train, "door", BrushKind.Part, new Vector3(0.6f, 1.1f, 0f)),
            new Vector3(0f, 1.3f, 0f), ticks: 7);
        door.Entity!.SetValue("turn", "0.9");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog([]));

        List<byte[]> first = Play(world, train, door, ticks: 60);
        List<byte[]> second = Play(world, train, door, ticks: 60);

        second.Count.ShouldBe(first.Count);
        for (int i = 0; i < first.Count; i++)
            second[i].ShouldBe(first[i], $"sample {i}");
    }

    private static List<byte[]> Play(EntityWorld world, SceneNode a, SceneNode b, int ticks)
    {
        var samples = new List<byte[]>();
        world.Activate();
        for (int i = 0; i < ticks; i++)
        {
            world.Tick(Dt);
            samples.Add(EntityMotion.Bits(a.LocalTransform));
            samples.Add(EntityMotion.Bits(b.LocalTransform));
        }

        world.Deactivate();
        return samples;
    }
}
