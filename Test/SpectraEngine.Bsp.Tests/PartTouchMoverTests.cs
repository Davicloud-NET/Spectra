using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What the touch pass takes from the player's mover: the parts its slide
/// stopped at, how far off a solid part it stops, and which parts it
/// collides with at all. A player a test places stands in for the mover.
/// </summary>
public sealed class PartTouchMoverTests
{
    private const float Dt = Touching.Dt;

    // A 2 by 2 by 2 block on the ground at the origin. A capsule coming in
    // along +X first touches it with its feet at Edge.
    private const float Edge = 1f + FakePlayerPresence.Radius;

    private static readonly Vector3 BlockCenter = new(0f, 1f, 0f);
    private static readonly Vector3 BlockHalf = new(1f, 1f, 1f);

    private readonly List<string> _log = [];
    private readonly FakePlayerPresence _player = new() { Feet = new Vector3(4f, 0f, 0f) };

    private string[] Heard => [.. _log.Where(line => line.StartsWith("hear", StringComparison.Ordinal))];

    [Theory]
    [InlineData(0.04f)]
    [InlineData(0.5f)]
    [InlineData(3f)]
    public void A_part_the_movers_slide_stopped_at_is_touched_wherever_the_tick_left_the_player(float gap)
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block");
        EntityWorld world = Play(scene);
        Listen(world, block);

        Stand(Edge + gap);
        world.Tick(Dt);
        Heard.ShouldBeEmpty();

        _player.RanInto = block.Id;
        world.Tick(Dt);
        Heard.ShouldBe(["hear touch 1 by 0"]);

        // Held for as long as the mover names it, and told once.
        world.Tick(Dt);
        Heard.Length.ShouldBe(1);
        world.Touches.PartTouchCount.ShouldBe(1);
    }

    [Fact]
    public void A_touch_the_slide_began_ends_on_the_tick_the_mover_names_the_part_no_more_and_the_player_is_clear()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block");
        EntityWorld world = Play(scene);
        Listen(world, block);
        Stand(Edge + 0.5f);
        _player.RanInto = block.Id;
        world.Tick(Dt);

        _player.RanInto = default;
        world.Tick(Dt);

        Heard.ShouldBe(["hear touch 1 by 0", "hear touch 2 by 0"]);
        world.Touches.PartTouchCount.ShouldBe(0);
    }

    [Fact]
    public void A_touch_the_slide_began_goes_on_by_distance_while_the_player_stays_inside_the_exit_margin()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block");
        EntityWorld world = Play(scene);
        Listen(world, block);

        // Past where a touch starts, and not past where one ends.
        Stand(Edge + 0.04f);
        _player.RanInto = block.Id;
        world.Tick(Dt);

        _player.RanInto = default;
        for (int i = 0; i < 5; i++)
            world.Tick(Dt);

        Heard.ShouldBe(["hear touch 1 by 0"]);
    }

    [Fact]
    public void A_part_the_mover_names_twice_as_ground_and_as_what_it_ran_into_is_touched_once()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block");
        EntityWorld world = Play(scene);
        Listen(world, block);

        Stand(Edge + 0.5f);
        _player.RanInto = block.Id;
        _player.StandsOn = block.Id;
        world.Tick(Dt);
        world.Tick(Dt);

        Heard.ShouldBe(["hear touch 1 by 0"]);
    }

    [Fact]
    public void A_part_with_CanTouch_off_is_not_touched_though_the_mover_ran_into_it()
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block");
        block.CanTouch = false;
        EntityWorld world = Play(scene);
        Listen(world, block);

        Stand(Edge + 0.01f);
        _player.RanInto = block.Id;
        world.Tick(Dt);

        Heard.ShouldBeEmpty();
    }

    [Fact]
    public void A_world_brush_and_a_node_that_is_gone_are_not_touched_though_the_mover_names_them()
    {
        var scene = new Scene("Touch");
        SceneNode wall = SpatialTestHelpers.CreateBrushNode(scene.Root, "Wall", BlockCenter, half: 1f);
        EntityWorld world = Play(scene);
        Listen(world, wall);

        Stand(Edge + 0.01f);
        _player.RanInto = wall.Id;
        world.Tick(Dt);
        _player.RanInto = Guid.NewGuid();
        world.Tick(Dt);

        Heard.ShouldBeEmpty();
    }

    // A subtractive part does nothing. The mover walks through it, and the
    // pass's search leaves a subtractive brush out.
    [Theory]
    [InlineData(0.015f)]
    [InlineData(-0.01f)]
    [InlineData(-1f)]
    public void A_subtractive_part_is_touched_neither_near_nor_inside_nor_when_the_mover_names_it(float gap)
    {
        var scene = new Scene("Touch");
        SceneNode hole = scene.Root.CreateChild("hole");
        hole.LocalPosition = BlockCenter;
        hole.BrushKind = BrushKind.Part;
        hole.Brush = Brush.CreateBox(-BlockHalf, BlockHalf).WithOperation(BrushOperation.Subtractive);
        hole.CanCollide.ShouldBeTrue();
        EntityWorld world = Play(scene);
        Listen(world, hole);

        Stand(Edge + gap);
        world.Tick(Dt);
        _player.RanInto = hole.Id;
        world.Tick(Dt);

        Heard.ShouldBeEmpty();
    }

    // A mover with a wider skin rests further off a wall than the engine's
    // own, and the wall it rests against is still touched.
    [Theory]
    [InlineData(0.010f, 0.045f, false)]
    [InlineData(0.025f, 0.045f, true)]
    [InlineData(0.025f, 0.055f, false)]
    public void A_solid_part_is_touched_from_twice_the_skin_the_players_mover_keeps(
        float skin, float gap, bool isTouched)
    {
        var scene = new Scene("Touch");
        SceneNode block = Block(scene, "block");
        EntityWorld world = Play(scene);
        Listen(world, block);
        _player.SkinWidth = skin;

        Stand(Edge + gap);
        world.Tick(Dt);

        Heard.ShouldBe(isTouched ? ["hear touch 1 by 0"] : []);
    }

    [Fact]
    public void A_player_that_says_nothing_of_its_skin_has_the_one_the_engines_mover_starts_with()
    {
        IPlayerPresence untuned = new UntunedPlayerPresence();
        IPlayerPresence walker = new CharacterSimulation(new Scene("Touch"));

        untuned.SkinWidth.ShouldBe(new CharacterTuning().SkinWidth);
        walker.SkinWidth.ShouldBe(new CharacterTuning().SkinWidth);
    }

    private EntityWorld Play(Scene scene)
    {
        EntityWorld world = Touching.Play(scene, _log, _player);
        world.Scripts = new FakeScriptRuntime(_log);
        return world;
    }

    private void Stand(float x) => _player.Feet = new Vector3(x, 0f, 0f);

    // A level's listeners are gone when it starts, so this comes after Play.
    private static void Listen(EntityWorld world, SceneNode part)
    {
        world.Listeners.Add(part, TouchSignal.Touched, 1);
        world.Listeners.Add(part, TouchSignal.TouchEnded, 2);
    }

    private static SceneNode Block(Scene scene, string name)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = BlockCenter;

        // Kind before brush, or the node is briefly a world brush.
        node.BrushKind = BrushKind.Part;
        node.Brush = Brush.CreateBox(-BlockHalf, BlockHalf);
        return node;
    }
}
