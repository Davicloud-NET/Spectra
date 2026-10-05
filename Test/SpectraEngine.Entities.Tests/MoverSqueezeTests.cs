using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// Doors and lifts against a real character, ticked the way a level ticks
/// them: entities first, then the character.
/// </summary>
// How the push and the squeeze feel needs a person.
public sealed class MoverSqueezeTests
{
    private const string OnOpen = "sink:OnOpen:";
    private const string OnClose = "sink:OnClose:";
    private const string OnFullyOpen = "sink:OnFullyOpen:";
    private const string OnFullyClosed = "sink:OnFullyClosed:";

    // A thirtieth of a unit: what a mover at 2 a second travels in a tick.
    private const float Step = 1f / 30f;

    // The furthest a mover gets into a player it squeezes: the block depth,
    // the tick that crossed it and the tick that noticed.
    private const float DeepestSqueeze = LinearMover.BlockDepth + 2f * Step;

    // The most a still part gives when the player is pressed into it.
    private const float PartGive = 0.13f;

    [Fact]
    public void A_door_closing_on_a_pinned_player_reverses()
    {
        var level = new Level();
        level.Floor();

        // Shut, the door fills the doorway from 0.7 to 1.7, where the player
        // stands. Open, it is clear above the head.
        SceneNode node = level.Mover("func_door", new Vector3(2f, 1f, 2f), new Vector3(0f, 1.2f, 0f));
        node.Entity!.SetValue("distance", "1.5");
        node.Entity.SetValue("startopen", "1");
        node.Entity.SetValue("wait", "-1");
        level.Start(feet: Vector3.Zero);
        FuncDoor door = level.Live<FuncDoor>(node);
        float head = level.Character.State.Position.Y + level.Character.Tuning.StandHeight;

        level.World.QueueInput(door, "Close");
        float lowest = float.MaxValue;
        for (int tick = 0; tick < 200; tick++)
        {
            level.Tick();
            lowest = MathF.Min(lowest, node.WorldPosition.Y - 0.5f);
            level.Character.State.Position.Y.ShouldBeGreaterThan(-PartGive, $"tick {tick}");
        }

        door.IsFullyOpen.ShouldBeTrue();
        level.Log.ShouldBe([OnClose, OnOpen, OnFullyOpen]);
        (head - lowest).ShouldBeInRange(LinearMover.BlockDepth, DeepestSqueeze + PartGive);
        level.Character.State.Grounded.ShouldBeTrue();
        level.Character.State.Position.Y.ShouldBe(level.Character.Tuning.SkinWidth, 0.02f);
    }

    [Fact]
    public void A_door_opening_into_a_pinned_player_closes_again()
    {
        var level = new Level();

        // A hatch the player stands on, rising under a ceiling half a unit
        // above the head.
        SceneNode node = level.Mover("func_door", new Vector3(3f, 0.5f, 3f), new Vector3(0f, -0.25f, 0f));
        node.Entity!.SetValue("distance", "2");
        node.Entity.SetValue("wait", "-1");
        level.Ceiling(headroom: 0.5f);
        level.Start(feet: Vector3.Zero);
        FuncDoor door = level.Live<FuncDoor>(node);

        level.World.QueueInput(door, "Open");
        int highest = 0;
        for (int tick = 0; tick < 200; tick++)
        {
            level.Tick();
            highest = Math.Max(highest, door.TicksTravelled);
        }

        door.IsFullyClosed.ShouldBeTrue();
        level.Log.ShouldBe([OnOpen, OnClose, OnFullyClosed]);
        (highest * Step).ShouldBeInRange(0.5f, 0.5f + DeepestSqueeze + PartGive);
        level.Character.State.Position.Y.ShouldBe(level.Character.Tuning.SkinWidth, 0.02f);
    }

    [Fact]
    public void A_door_blocked_before_it_leaves_its_rest_waits_there()
    {
        var level = new Level();
        level.Floor();
        SceneNode node = level.Mover("func_door", new Vector3(2f, 1f, 2f), new Vector3(0f, 1.2f, 0f));
        node.Entity!.SetValue("distance", "1.5");
        node.Entity.SetValue("startopen", "1");
        node.Entity.SetValue("wait", "-1");
        level.Start(feet: new Vector3(3f, 0f, 3f));
        FuncDoor door = level.Live<FuncDoor>(node);

        // Someone with their head 0.3 into the open door, where it comes down.
        float underside = node.WorldPosition.Y - 0.5f;
        var stuck = new PinnedPlayer(new Vector3(0f, underside + 0.3f - PinnedPlayer.Height, 0f));
        level.World.Player = stuck;

        level.World.QueueInput(door, "Close");
        level.Tick(50);

        door.IsFullyOpen.ShouldBeTrue();
        level.Log.ShouldBe([OnClose]);
        level.World.TickingEntityCount.ShouldBe(1);

        stuck.Feet = new Vector3(5f, 0f, 5f);
        level.Tick(50);

        door.IsFullyClosed.ShouldBeTrue();
        level.Log.ShouldBe([OnClose, OnFullyClosed]);
    }

    [Fact]
    public void A_door_pushes_a_player_who_has_room_and_closes()
    {
        var level = new Level();
        level.Floor();

        // Slides shut along X, from 2 units off, across where the player is.
        SceneNode node = level.Mover("func_door", new Vector3(0.5f, 2f, 2f), new Vector3(0f, 1.05f, 0f));
        node.Entity!.SetValue("movedir", "1 0 0");
        node.Entity.SetValue("distance", "2");
        node.Entity.SetValue("startopen", "1");
        node.Entity.SetValue("wait", "-1");
        level.Start(feet: new Vector3(0.5f, 0f, 0f));
        FuncDoor door = level.Live<FuncDoor>(node);

        level.World.QueueInput(door, "Close");
        level.Tick(70);

        door.IsFullyClosed.ShouldBeTrue();
        level.Log.ShouldBe([OnClose, OnFullyClosed]);

        // Against the door's leading face, which stops at -0.25.
        level.Character.State.Position.X.ShouldBe(-0.25f - level.Character.Tuning.Radius, 0.02f);
        level.Character.State.Grounded.ShouldBeTrue();
    }

    [Fact]
    public void A_blocked_linear_mover_waits_and_goes_on_once_the_player_leaves()
    {
        var level = new Level();
        level.Floor();

        // A press whose underside starts 0.44 above the head and comes down
        // 1.5.
        SceneNode node = level.Mover("func_movelinear", new Vector3(2f, 0.5f, 2f), new Vector3(0f, 2.5f, 0f));
        node.Entity!.SetValue("movedir", "0 -1 0");
        node.Entity.SetValue("distance", "1.5");
        level.Start(feet: Vector3.Zero);
        FuncMoveLinear press = level.Live<FuncMoveLinear>(node);
        float head = level.Character.State.Position.Y + level.Character.Tuning.StandHeight;

        level.World.QueueInput(press, "Open");
        level.Tick(200);

        float underside = node.WorldPosition.Y - 0.25f;
        (head - underside).ShouldBeInRange(LinearMover.BlockDepth, DeepestSqueeze + PartGive);
        press.TicksTravelled.ShouldBeLessThan(press.TravelTicks);
        level.World.TickingEntityCount.ShouldBe(1);
        level.Log.ShouldBeEmpty();

        level.Character.Teleport(new Vector3(3f, 0.05f, 3f));
        level.Tick(60);

        press.TicksTravelled.ShouldBe(press.TravelTicks);
        level.World.TickingEntityCount.ShouldBe(0);
        level.Log.ShouldBe([OnFullyOpen]);
    }

    [Fact]
    public void A_lift_waits_under_a_rider_who_meets_the_ceiling()
    {
        var level = new Level();
        SceneNode node = level.Mover("func_movelinear", new Vector3(3f, 0.5f, 3f), new Vector3(0f, -0.25f, 0f));
        node.Entity!.SetValue("distance", "2");
        float ceiling = level.Ceiling(headroom: 0.5f);
        level.Start(feet: Vector3.Zero);
        FuncMoveLinear lift = level.Live<FuncMoveLinear>(node);
        float height = level.Character.Tuning.StandHeight;

        level.World.QueueInput(lift, "Open");
        for (int tick = 0; tick < 200; tick++)
        {
            level.Tick();
            (level.Character.State.Position.Y + height - ceiling).ShouldBeLessThan(PartGive, $"tick {tick}");
        }

        (lift.TicksTravelled * Step).ShouldBeInRange(0.5f, 0.5f + DeepestSqueeze + PartGive);
        level.World.TickingEntityCount.ShouldBe(1);
        level.Log.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2", 180)]
    // 0.2 a tick, twice the block depth.
    [InlineData("12", 30)]
    public void A_lift_carries_its_rider_to_the_top_however_fast_it_goes(string speed, int travelTicks)
    {
        var level = new Level();
        SceneNode node = level.Mover("func_movelinear", new Vector3(3f, 0.5f, 3f), new Vector3(0f, -0.25f, 0f));
        node.Entity!.SetValue("distance", "6");
        node.Entity.SetValue("speed", speed);
        level.Start(feet: Vector3.Zero);
        FuncMoveLinear lift = level.Live<FuncMoveLinear>(node);
        lift.TravelTicks.ShouldBe(travelTicks);

        level.World.QueueInput(lift, "Open");
        for (int tick = 1; tick <= travelTicks; tick++)
        {
            level.Tick();
            lift.TicksTravelled.ShouldBe(tick);
            level.Character.State.Grounded.ShouldBeTrue($"tick {tick}");
        }

        level.Character.State.Position.Y.ShouldBe(6f + level.Character.Tuning.SkinWidth, 0.02f);
    }

    // Parts only, so the level needs no compile: a floor with its top at 0,
    // movers wired to a recorder, and a character.
    private sealed class Level
    {
        private readonly Scene _scene = new("Squeeze");
        private EntityWorld? _world;
        private CharacterSimulation? _character;

        public List<string> Log { get; } = [];

        public EntityWorld World => _world ?? throw new InvalidOperationException("Start the level first.");

        public CharacterSimulation Character =>
            _character ?? throw new InvalidOperationException("Start the level first.");

        public void Floor() => Movers.Part(_scene.Root, "floor", new Vector3(8f, 0.5f, 8f), new Vector3(0f, -0.25f, 0f));

        // A slab with its underside this far above the head of a character
        // standing at height 0. Returns the underside's height.
        public float Ceiling(float headroom)
        {
            var tuning = new CharacterTuning();
            float underside = tuning.SkinWidth + tuning.StandHeight + headroom;
            Movers.Part(_scene.Root, "ceiling", new Vector3(3f, 0.5f, 3f), new Vector3(0f, underside + 0.25f, 0f));
            return underside;
        }

        public SceneNode Mover(string className, Vector3 size, Vector3 position)
        {
            SceneNode node = Movers.Part(_scene.Root, className, size, position);
            node.Entity = new EntityData(className);
            Movers.WireToSink(node, "OnOpen", "OnClose", "OnFullyOpen", "OnFullyClosed");
            return node;
        }

        // Starts the entities, then puts the character down and lets it settle.
        public void Start(Vector3 feet)
        {
            EntityRuntime.Place(_scene.Root, "sink", "test_recorder");
            _world = new EntityWorld(_scene, new CapturingLogger(), EntityRuntime.Catalog(Log));
            _world.Activate();

            _character = new CharacterSimulation(_scene) { SpawnPosition = feet + new Vector3(0f, 0.05f, 0f) };
            _character.Spawn();
            _world.Player = _character;

            Tick(30);
            _character.State.Grounded.ShouldBeTrue();
        }

        public T Live<T>(SceneNode node)
            where T : Entity => EntityRuntime.Live<T>(World, node);

        public void Tick(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                World.Tick(Movers.Dt);
                Character.Tick(default, Movers.Dt);
            }
        }
    }
}
