using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The touch pass for the parts scripts listen to: what counts as a touch,
/// and when each listener is called for one that starts and one that ends.
/// A script runtime that runs nothing stands in for the scripts.
/// </summary>
public sealed class PartTouchTests
{
    private const float Dt = Touching.Dt;

    // A 2 by 2 by 2 block standing on the ground at the origin. A capsule
    // coming in along +X first touches it with its feet at Edge.
    private const float Edge = 1f + FakePlayerPresence.Radius;

    // The mover counts as contact what is within two of its skins.
    private const float SolidReach = 0.02f;

    private static readonly Vector3 BlockCenter = new(0f, 1f, 0f);
    private static readonly Vector3 BlockHalf = new(1f, 1f, 1f);

    private readonly List<string> _log = [];
    private readonly FakePlayerPresence _player = new() { Feet = new Vector3(4f, 0f, 0f) };

    private string[] Heard => [.. _log.Where(line => line.StartsWith("hear", StringComparison.Ordinal))];

    [Fact]
    public void A_part_the_player_walks_through_is_touched_once_and_left_once()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block", isSolid: false);
        EntityWorld world = Play(scene);
        Listen(world, block);

        // Through the block and out the far side, a tenth of a unit a tick.
        for (int i = 0; i <= 80; i++)
        {
            Stand(4f - (i * 0.1f));
            world.Tick(Dt);
        }

        Heard.ShouldBe(["hear touch 1 by 0", "hear touch 2 by 0"]);
        world.Touches.PartTouchCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(true, 0.015f, true)]
    [InlineData(true, 0.03f, false)]
    [InlineData(false, 0.015f, false)]
    [InlineData(false, -0.01f, true)]
    public void A_solid_part_is_touched_from_where_the_mover_stops_and_any_other_by_overlap(
        bool isSolid, float gap, bool isTouched)
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block", isSolid);
        EntityWorld world = Play(scene);
        Listen(world, block);

        Stand(Edge + gap);
        world.Tick(Dt);

        Heard.ShouldBe(isTouched ? ["hear touch 1 by 0"] : []);
    }

    [Fact]
    public void A_touch_of_a_solid_part_ends_once_the_player_is_clear_by_the_exit_margin()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block", isSolid: true);
        EntityWorld world = Play(scene);
        Listen(world, block);
        Stand(Edge + 0.01f);
        world.Tick(Dt);

        Stand(Edge + SolidReach + TouchTracker.ExitMargin - 0.01f);
        world.Tick(Dt);
        Heard.ShouldBe(["hear touch 1 by 0"]);

        Stand(Edge + SolidReach + TouchTracker.ExitMargin + 0.01f);
        world.Tick(Dt);
        Heard.ShouldBe(["hear touch 1 by 0", "hear touch 2 by 0"]);

        // Inside the margin again is not a touch: one starts at contact.
        Stand(Edge + SolidReach + TouchTracker.ExitMargin - 0.01f);
        world.Tick(Dt);
        Heard.Length.ShouldBe(2);
    }

    [Fact]
    public void The_part_the_mover_says_the_player_stands_on_is_touched_whatever_the_gap()
    {
        var scene = new Scene("Touch");
        SceneNode floor = Part(scene, "floor", new Vector3(0f, -0.5f, 0f), new Vector3(2f, 0.5f, 2f), isSolid: true);
        EntityWorld world = Play(scene);
        Listen(world, floor);

        // Four centimetres up: past where a solid part is touched from.
        _player.Feet = new Vector3(0f, 0.04f, 0f);
        world.Tick(Dt);
        Heard.ShouldBeEmpty();

        _player.StandsOn = floor.Id;
        world.Tick(Dt);
        Heard.ShouldBe(["hear touch 1 by 0"]);
    }

    [Fact]
    public void Parts_touched_in_one_tick_are_told_in_the_order_they_were_first_listened_to()
    {
        var scene = new Scene("Touch");
        SceneNode first = Block(scene, "first", isSolid: false);
        SceneNode second = Block(scene, "second", isSolid: false);
        EntityWorld world = Play(scene);

        // The other way round than the scene has them.
        world.Listeners.Add(second, TouchSignal.Touched, 5);
        world.Listeners.Add(first, TouchSignal.Touched, 6);
        world.Listeners.Add(second, TouchSignal.Touched, 7);

        Stand(0f);
        world.Tick(Dt);

        Heard.ShouldBe(["hear touch 5 by 0", "hear touch 7 by 0", "hear touch 6 by 0"]);
    }

    [Fact]
    public void A_touch_that_ends_is_told_before_one_that_starts_in_the_same_tick()
    {
        var scene = new Scene("Touch");
        SceneNode left = Part(scene, "left", new Vector3(-3f, 1f, 0f), BlockHalf, isSolid: false);
        SceneNode right = Part(scene, "right", new Vector3(3f, 1f, 0f), BlockHalf, isSolid: false);
        EntityWorld world = Play(scene);
        Listen(world, right, started: 1, ended: 2);
        Listen(world, left, started: 3, ended: 4);
        Stand(-3f);
        world.Tick(Dt);

        Stand(3f);
        world.Tick(Dt);

        Heard.ShouldBe(["hear touch 3 by 0", "hear touch 4 by 0", "hear touch 1 by 0"]);
    }

    // The trigger's callback fires its output as the pass finds the touch.
    // The wire's input and the output's own listener are queued then, and
    // the listener of the touch after both.
    [Fact]
    public void A_triggers_own_output_goes_ahead_of_what_a_script_hears_of_the_same_touch()
    {
        var scene = new Scene("Touch");
        SceneNode zone = Touching.Volume(scene.Root, "zone", BlockCenter, BlockHalf);
        EntityRuntime.Place(scene.Root, "door", "recorder");
        EntityRuntime.Wire(zone, "OnStartTouch", "door", "Open");
        EntityWorld world = Play(scene);
        world.Listeners.Add(zone, TouchSignal.Touched, 8);
        world.Listeners.Add(zone, "OnStartTouch", 7);

        Stand(0f);
        world.Tick(Dt);

        _log.ShouldBe(["start scripts", "start:zone", "door:Open::zone:zone", "hear 7", "hear touch 8 by 0"]);
    }

    [Fact]
    public void Switching_CanTouch_off_ends_the_touch_and_on_starts_another()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block", isSolid: false);
        EntityWorld world = Play(scene);
        Listen(world, block);
        Stand(0f);
        world.Tick(Dt);

        block.CanTouch = false;
        world.Tick(Dt);
        Heard.ShouldBe(["hear touch 1 by 0", "hear touch 2 by 0"]);

        // Still inside, and not noticed.
        for (int i = 0; i < 5; i++)
            world.Tick(Dt);
        Heard.Length.ShouldBe(2);

        block.CanTouch = true;
        world.Tick(Dt);
        Heard.ShouldBe(["hear touch 1 by 0", "hear touch 2 by 0", "hear touch 1 by 0"]);
    }

    [Fact]
    public void A_part_that_leaves_the_scene_while_touched_ends_its_touch()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block", isSolid: false);
        EntityWorld world = Play(scene);
        Listen(world, block);
        Stand(0f);
        world.Tick(Dt);

        scene.Root.RemoveChild(block);
        world.Tick(Dt);
        world.Tick(Dt);

        Heard.ShouldBe(["hear touch 1 by 0", "hear touch 2 by 0"]);
        world.Touches.PartTouchCount.ShouldBe(0);
    }

    // A thread that waits for Touched in a loop is one listener that leaves
    // and one that comes, every time round.
    [Fact]
    public void A_listener_that_comes_while_the_touch_goes_on_hears_no_second_start()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block", isSolid: false);
        EntityWorld world = Play(scene);
        world.Listeners.Add(block, TouchSignal.Touched, 1);
        Stand(0f);
        world.Tick(Dt);

        world.Listeners.Remove(block, TouchSignal.Touched, 1);
        world.Tick(Dt);
        world.Listeners.Add(block, TouchSignal.Touched, 3);
        for (int i = 0; i < 5; i++)
            world.Tick(Dt);

        Heard.ShouldBe(["hear touch 1 by 0"]);
        world.Touches.PartTouchCount.ShouldBe(1);

        // Away and back is a touch of its own.
        Stand(4f);
        world.Tick(Dt);
        Stand(0f);
        world.Tick(Dt);
        Heard.ShouldBe(["hear touch 1 by 0", "hear touch 3 by 0"]);
    }

    [Fact]
    public void A_part_first_listened_to_with_the_player_on_it_is_touched_on_the_next_tick()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block", isSolid: false);
        EntityWorld world = Play(scene);
        Stand(0f);
        for (int i = 0; i < 5; i++)
            world.Tick(Dt);

        // Nobody listened, so nothing was looked at.
        world.Touches.PartTouchCount.ShouldBe(0);

        Listen(world, block);
        world.Tick(Dt);

        Heard.ShouldBe(["hear touch 1 by 0"]);
    }

    [Fact]
    public void A_world_brush_is_never_touched_and_says_why()
    {
        var scene = new Scene("Touch");
        SceneNode wall = SpatialTestHelpers.CreateBrushNode(scene.Root, "Wall", BlockCenter, half: 1f);
        EntityWorld world = Play(scene);
        Listen(world, wall);

        Stand(Edge + 0.01f);
        world.Tick(Dt);
        Stand(0f);
        world.Tick(Dt);

        Heard.ShouldBeEmpty();
        TouchSignal.WhyNone(wall).ShouldBe(
            "Wall cannot tell what touches it: it is a world brush, fused into the level, and only parts " +
            "notice a touch while a level plays. Convert the brush to a part.");
    }

    [Fact]
    public void A_brush_a_bake_fused_into_the_level_says_the_same_and_a_part_and_a_mesh_say_nothing()
    {
        var scene = new Scene("Touch");
        var baked = new SceneNode("Floor") { BakedKind = SceneNodeKind.BrushWorld };
        var cut = new SceneNode("Doorway") { BakedKind = SceneNodeKind.BrushSubtractive };

        TouchSignal.WhyNone(baked).ShouldNotBeNull().ShouldStartWith("Floor cannot tell what touches it: it is a world brush");
        TouchSignal.WhyNone(cut).ShouldNotBeNull().ShouldStartWith("Doorway cannot tell what touches it");
        TouchSignal.WhyNone(Block(scene, "block", isSolid: true)).ShouldBeNull();
        TouchSignal.WhyNone(SpatialTestHelpers.CreateMeshNode(scene.Root, "coin", Vector3.Zero)).ShouldBeNull();
        TouchSignal.WhyNone(new SceneNode("Prop") { BakedKind = SceneNodeKind.Mesh }).ShouldBeNull();
    }

    // A mesh has no shape of its own yet. Its box turns and grows with it.
    [Theory]
    [InlineData(1f, 0f, 0.84f, true)]
    [InlineData(1f, 0f, 0.86f, false)]
    [InlineData(2f, 0f, 1.34f, true)]
    [InlineData(2f, 0f, 1.36f, false)]
    [InlineData(1f, MathF.PI / 4f, 1.04f, true)]
    [InlineData(1f, MathF.PI / 4f, 1.07f, false)]
    public void A_mesh_is_touched_where_the_player_overlaps_the_box_around_it(
        float scale, float turn, float feetX, bool isTouched)
    {
        var scene = new Scene("Touch");
        SceneNode coin = SpatialTestHelpers.CreateMeshNode(scene.Root, "coin", new Vector3(0f, 1f, 0f));
        coin.LocalScale = new Vector3(scale);
        coin.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn);
        EntityWorld world = Play(scene);
        Listen(world, coin);

        Stand(feetX);
        world.Tick(Dt);

        Heard.ShouldBe(isTouched ? ["hear touch 1 by 0"] : []);
    }

    [Fact]
    public void Stopping_the_level_tells_no_listener_and_the_next_play_starts_clean()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block", isSolid: false);
        EntityWorld world = Play(scene);
        Listen(world, block);
        Stand(0f);
        world.Tick(Dt);

        world.Deactivate();
        Heard.ShouldBe(["hear touch 1 by 0"]);
        world.Touches.PartTouchCount.ShouldBe(0);
        world.Listeners.TouchListenerCount.ShouldBe(0);

        world.Activate();
        world.Player = _player;
        world.Tick(Dt);
        Heard.Length.ShouldBe(1);

        // The player never left, and to the new level the touch is new.
        Listen(world, block);
        world.Tick(Dt);
        Heard.ShouldBe(["hear touch 1 by 0", "hear touch 1 by 0"]);
    }

    [Fact]
    public void A_player_that_leaves_the_level_ends_its_touches_and_none_starts_with_nobody_walking()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block", isSolid: false);
        EntityWorld world = Play(scene);
        Listen(world, block);
        Stand(0f);
        world.Tick(Dt);

        _player.IsPresent = false;
        world.Tick(Dt);
        Heard.ShouldBe(["hear touch 1 by 0", "hear touch 2 by 0"]);

        world.Player = null;
        for (int i = 0; i < 5; i++)
            world.Tick(Dt);
        Heard.Length.ShouldBe(2);
    }

    [Fact]
    public void Only_a_listener_under_one_of_the_two_names_counts_as_one_for_a_touch()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block", isSolid: false);
        EntityWorld world = Play(scene);

        world.Listeners.Add(block, "OnPing", 1);
        world.Listeners.TouchListenerCount.ShouldBe(0);

        Listen(world, block, started: 2, ended: 3);
        world.Listeners.TouchListenerCount.ShouldBe(2);
        world.Listeners.Count.ShouldBe(3);

        world.Listeners.Remove(block, TouchSignal.TouchEnded, 3).ShouldBeTrue();
        world.Listeners.Remove(block, TouchSignal.TouchEnded, 3).ShouldBeFalse();
        world.Listeners.TouchListenerCount.ShouldBe(1);
    }

    // What a touch queues waits with what scripts made, and takes no room
    // from what entities send.
    [Fact]
    public void A_listeners_call_for_a_touch_takes_the_scripts_room_in_the_queue_and_none_of_the_entities()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block", isSolid: false);
        EntityRuntime.Place(scene.Root, "door", "recorder");
        var logger = new CapturingLogger();
        EntityWorld world = Touching.Play(scene, _log, _player, logger);
        world.Scripts = new FakeScriptRuntime(_log);
        world.MaxPendingEvents = 2;
        for (int listener = 1; listener <= 3; listener++)
            world.Listeners.Add(block, TouchSignal.Touched, listener);

        Stand(0f);
        world.QueueInput("door", "First");
        world.QueueInput("door", "Second");
        world.Tick(Dt);

        _log.Skip(1).ShouldBe(["door:First::-:-", "door:Second::-:-", "hear touch 1 by 0", "hear touch 2 by 0"]);
        world.DiscardedEventCount.ShouldBe(1);
        logger.MessagesAt(Microsoft.Extensions.Logging.LogLevel.Error).ShouldHaveSingleItem()
            .ShouldContain("a listener's call for 'Touched' at 'block' was dropped");
    }

    [Theory]
    [InlineData(Edge + 0.03f)]
    [InlineData(0f)]
    public void A_tick_near_a_listened_part_and_one_on_it_allocate_nothing(float feetX)
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block", isSolid: true);
        SceneNode coin = SpatialTestHelpers.CreateMeshNode(scene.Root, "coin", new Vector3(feetX + 0.9f, 1f, 0f));
        Part(scene, "unheard", new Vector3(feetX, 1f, 1f), BlockHalf, isSolid: false);
        EntityWorld world = Play(scene);
        Listen(world, block);
        Listen(world, coin, started: 3, ended: 4);
        Stand(feetX);

        for (int i = 0; i < 60; i++)
            world.Tick(Dt);

        int heard = Heard.Length;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++)
            world.Tick(Dt);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0L);
        Heard.Length.ShouldBe(heard);
    }

    private EntityWorld Play(Scene scene)
    {
        EntityWorld world = Touching.Play(scene, _log, _player);
        world.Scripts = new FakeScriptRuntime(_log);
        return world;
    }

    private void Stand(float x) => _player.Feet = new Vector3(x, 0f, 0f);

    // A level's listeners are gone when it starts, so this comes after Play.
    private static void Listen(EntityWorld world, SceneNode part, long started = 1, long ended = 2)
    {
        world.Listeners.Add(part, TouchSignal.Touched, started);
        world.Listeners.Add(part, TouchSignal.TouchEnded, ended);
    }

    private static SceneNode Block(Scene scene, string name, bool isSolid) =>
        Part(scene, name, BlockCenter, BlockHalf, isSolid);

    private static SceneNode Part(Scene scene, string name, Vector3 center, Vector3 half, bool isSolid)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = center;

        // Kind before brush, or the node is briefly a world brush.
        node.BrushKind = BrushKind.Part;
        node.Brush = Brush.CreateBox(-half, half);
        node.CanCollide = isSolid;
        return node;
    }
}
