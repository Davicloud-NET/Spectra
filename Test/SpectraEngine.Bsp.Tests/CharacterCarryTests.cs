using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A character standing on a part that moves travels with it. The part is
/// posed by the test each tick, the way an entity poses it before the
/// character ticks.
/// </summary>
// Whether the ride feels smooth needs a person.
public sealed class CharacterCarryTests
{
    private const float Dt = PhysicsDefaults.FixedDeltaTime;

    // A mover gives up before it is this far into the player, so the squeeze
    // tests stop here too. Further in, the way out of a thin part is its back.
    private const float Squeeze = 0.15f;

    [Fact]
    public void A_character_on_a_sliding_part_travels_with_it()
    {
        Rig rig = Rig.OnAPlatform();
        Vector3 feet = rig.Character.State.Position;
        var step = new Vector3(0.05f, 0f, 0f);

        for (int tick = 1; tick <= 120; tick++)
        {
            rig.Pose(rig.PlatformStart + step * tick);
            rig.Character.Tick(default, Dt);
            rig.Character.State.Grounded.ShouldBeTrue($"tick {tick}");
        }

        Vector3 travelled = rig.Character.State.Position - feet;
        Vector3.Distance(travelled, step * 120).ShouldBeLessThan(rig.Character.Tuning.SkinWidth);
        rig.Character.State.GroundNodeId.ShouldBe(rig.Platform.Id);
    }

    [Fact]
    public void Standing_on_a_still_part_does_not_drift()
    {
        Rig rig = Rig.OnAPlatform();
        CharacterState rest = rig.Character.State;

        for (int tick = 0; tick < 10_000; tick++)
            rig.Character.Tick(default, Dt);

        Bits(rig.Character.State.Position).ShouldBe(Bits(rest.Position));
        rig.Character.State.Velocity.ShouldBe(Vector3.Zero);
        rig.Character.State.Grounded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0.05f)]
    // Further a tick than the ground snap reaches.
    [InlineData(0.4f)]
    public void A_lift_going_up_and_down_keeps_its_rider_grounded(float step)
    {
        Rig rig = Rig.OnAPlatform();
        float height = rig.Character.State.Position.Y - rig.PlatformStart.Y;

        // 40 ticks up, then 40 back down.
        for (int tick = 1; tick <= 80; tick++)
        {
            int risen = tick <= 40 ? tick : 80 - tick;
            rig.Pose(rig.PlatformStart + new Vector3(0f, step * risen, 0f));
            rig.Character.Tick(default, Dt);

            rig.Character.State.Grounded.ShouldBeTrue($"tick {tick}");
            float above = rig.Character.State.Position.Y - rig.Platform.WorldPosition.Y;
            above.ShouldBe(height, 1e-3f, $"tick {tick}");
        }
    }

    [Fact]
    public void A_rider_keeps_the_walking_it_does_on_top_of_the_carry()
    {
        Rig still = Rig.OnAPlatform();
        Rig moving = Rig.OnAPlatform();
        var step = new Vector3(0f, 0f, 0.05f);
        var walk = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f), Yaw = 0f };

        for (int tick = 1; tick <= 20; tick++)
        {
            still.Character.Tick(in walk, Dt);
            moving.Pose(moving.PlatformStart + step * tick);
            moving.Character.Tick(in walk, Dt);
        }

        Vector3 apart = moving.Character.State.Position - still.Character.State.Position;
        Vector3.Distance(apart, step * 20).ShouldBeLessThan(1e-3f);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void A_rider_on_the_rim_is_carried_as_far_as_the_part_goes(float way)
    {
        // Over the edge by less than a radius: the capsule rests on the
        // platform's edge, not on its top.
        Rig rig = Rig.OnAPlatform(new Vector3(2.2f, 0f, 0f));
        Vector3 feet = rig.Character.State.Position;
        var step = new Vector3(0.05f * way, 0f, 0f);

        for (int tick = 1; tick <= 60; tick++)
        {
            rig.Pose(rig.PlatformStart + step * tick);
            rig.Character.Tick(default, Dt);
        }

        Vector3.Distance(rig.Character.State.Position, feet + step * 60).ShouldBeLessThan(1e-4f);
    }

    [Fact]
    public void Walking_off_a_moving_part_ends_the_carry()
    {
        Rig rig = Rig.OnAPlatform();
        var step = new Vector3(0f, 0f, 0.05f);
        var walk = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f), Yaw = 0f };

        // The platform is 4 wide: 60 ticks of walking leave it for the floor.
        int tick = 1;
        for (; tick <= 90; tick++)
        {
            rig.Pose(rig.PlatformStart + step * tick);
            rig.Character.Tick(in walk, Dt);
        }

        rig.Character.State.Grounded.ShouldBeTrue();
        rig.Character.State.GroundNodeId.ShouldBe(Guid.Empty);
        rig.Character.State.GroundOrigin.ShouldBe(Vector3.Zero);
        float z = rig.Character.State.Position.Z;

        for (; tick <= 120; tick++)
        {
            rig.Pose(rig.PlatformStart + step * tick);
            rig.Character.Tick(default, Dt);
        }

        rig.Character.State.Position.Z.ShouldBe(z, 1e-4f);
    }

    [Fact]
    public void A_part_that_turns_in_place_moves_nobody()
    {
        Rig rig = Rig.OnAPlatform();
        Vector3 feet = rig.Character.State.Position;

        for (int tick = 1; tick <= 60; tick++)
        {
            rig.Platform.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.02f * tick);
            rig.Character.Tick(default, Dt);
        }

        Vector3.Distance(rig.Character.State.Position, feet).ShouldBeLessThan(1e-4f);
    }

    [Fact]
    public void A_replaced_mover_is_carried_too()
    {
        Rig rig = Rig.OnAPlatform();
        rig.Character.Mover = new StandingMover();
        Vector3 feet = rig.Character.State.Position;
        var step = new Vector3(0.05f, 0.02f, -0.03f);

        for (int tick = 1; tick <= 50; tick++)
        {
            rig.Pose(rig.PlatformStart + step * tick);
            rig.Character.Tick(default, Dt);
        }

        Vector3.Distance(rig.Character.State.Position, feet + step * 50).ShouldBeLessThan(1e-4f);
    }

    [Fact]
    public void The_same_commands_and_platform_poses_replay_to_the_same_state()
    {
        Rig rig = Rig.OnAPlatform();
        var states = new CharacterState[121];
        states[0] = rig.Character.State;

        for (int tick = 1; tick <= 120; tick++)
        {
            rig.Pose(PoseAt(rig, tick));
            rig.Character.Tick(CommandAt(tick), Dt);
            states[tick] = rig.Character.State;
        }

        // From the middle of the ride, the way a correction restores it.
        rig.Character.Restore(in states[45]);
        for (int tick = 46; tick <= 120; tick++)
        {
            rig.Pose(PoseAt(rig, tick));
            rig.Character.Tick(CommandAt(tick), Dt);
            ShouldBeTheSame(rig.Character.State, states[tick], tick);
        }
    }

    [Fact]
    public void A_part_closing_on_the_capsule_pushes_it_out()
    {
        Rig rig = Rig.OnTheFloor();
        CharacterTuning tuning = rig.Character.Tuning;

        // A wall that starts clear of the character and slides 2 units into
        // where it stands.
        SceneNode wall = rig.AddPart("wall", new Vector3(1f, 2f, 4f), new Vector3(1.5f, 1f, 0f));
        Vector3 start = wall.LocalPosition;

        for (int tick = 1; tick <= 40; tick++)
        {
            wall.LocalPosition = start - new Vector3(0.05f * tick, 0f, 0f);
            rig.Character.Tick(default, Dt);
        }

        float wallFace = wall.WorldPosition.X - 0.5f;
        rig.Character.State.Position.X.ShouldBe(wallFace - tuning.Radius, 0.02f);
        rig.Character.State.Grounded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, 1f / 30f)]
    [InlineData(false, 0.2f)]
    [InlineData(true, 1f / 30f)]
    [InlineData(true, 0.2f)]
    public void A_lift_does_not_push_its_rider_through_a_ceiling(bool ceilingIsAPart, float step)
    {
        foreach (float gap in Gaps())
        {
            Rig rig = Rig.OnAPlatform();
            CharacterTuning tuning = rig.Character.Tuning;
            float ceiling = rig.Character.State.Position.Y + tuning.StandHeight + gap;
            var size = new Vector3(6f, 0.5f, 6f);
            Vector3 centre = rig.PlatformStart with { Y = ceiling + 0.25f };
            if (ceilingIsAPart)
                rig.AddPart("ceiling", size, centre);
            else
                rig.AddWorld("ceiling", size, centre);

            // A still part gives as much as a tick's solve pushes. The world
            // gives nothing.
            float give = ceilingIsAPart ? 0.13f : 0.01f;
            Slide(rig, rig.Platform, Vector3.UnitY, gap + Squeeze, step, () =>
                (rig.Character.State.Position.Y + tuning.StandHeight - ceiling).ShouldBeLessThan(
                    give, $"gap {gap}"));

            float sunk = rig.Platform.WorldPosition.Y + 0.25f - rig.Character.State.Position.Y;
            sunk.ShouldBeGreaterThan(Squeeze - give - 0.02f, $"gap {gap}");
        }
    }

    [Theory]
    [InlineData(1f / 30f)]
    [InlineData(0.2f)]
    public void A_part_closing_on_a_capsule_against_a_wall_does_not_push_it_through(float step)
    {
        foreach (float gap in Gaps())
        {
            Rig rig = Rig.OnTheFloor();
            float radius = rig.Character.Tuning.Radius;

            // A wall a hair behind the character and a slab sliding in at its
            // front.
            rig.AddWorld("wall", new Vector3(1f, 3f, 6f), new Vector3(-0.52f - radius, 1.5f, 0f));
            SceneNode slab = rig.AddPart(
                "slab", new Vector3(1f, 2f, 2f), new Vector3(radius + gap + 0.5f, 1.05f, 0f));

            Slide(rig, slab, -Vector3.UnitX, gap + Squeeze, step, () =>
                rig.Character.State.Position.X.ShouldBeGreaterThan(-0.03f, $"gap {gap}"));

            float sunk = rig.Character.State.Position.X + radius - (slab.WorldPosition.X - 0.5f);
            sunk.ShouldBeGreaterThan(Squeeze - 0.05f, $"gap {gap}");
        }
    }

    // On a grid and off it: which side gives way must not depend on where
    // the two happen to meet.
    private static IEnumerable<float> Gaps()
    {
        for (int i = 0; i < 20; i++)
        {
            yield return 0.30f + 0.01f * i;
            yield return 0.30f + 0.0037f * i;
        }
    }

    // Slides a part its travel a step a tick, ticking the character after each.
    private static void Slide(
        Rig rig, SceneNode part, Vector3 direction, float travel, float step, Action afterEachTick)
    {
        Vector3 start = part.LocalPosition;
        int ticks = (int)MathF.Ceiling(travel / step);
        for (int tick = 1; tick <= ticks; tick++)
        {
            part.LocalPosition = start + direction * MathF.Min(tick * step, travel);
            rig.Character.Tick(default, Dt);
            afterEachTick();
        }
    }

    // Across, then up, so the ride has both kinds of travel in it.
    private static Vector3 PoseAt(Rig rig, int tick) =>
        rig.PlatformStart + new Vector3(0.04f * Math.Min(tick, 60), 0.03f * Math.Max(tick - 60, 0), 0f);

    // Walks about the platform and jumps once.
    private static CharacterCommand CommandAt(int tick) => new()
    {
        MoveForward = CharacterCommand.Axis(tick % 40 < 20 ? 0.5f : -0.5f),
        Yaw = 0.7f,
        Buttons = tick == 70 ? CharacterButtons.Jump : CharacterButtons.None,
    };

    private static void ShouldBeTheSame(in CharacterState actual, in CharacterState expected, int tick)
    {
        Bits(actual.Position).ShouldBe(Bits(expected.Position), $"position, tick {tick}");
        Bits(actual.Velocity).ShouldBe(Bits(expected.Velocity), $"velocity, tick {tick}");
        Bits(actual.GroundNormal).ShouldBe(Bits(expected.GroundNormal), $"ground normal, tick {tick}");
        Bits(actual.GroundOrigin).ShouldBe(Bits(expected.GroundOrigin), $"ground origin, tick {tick}");
        actual.Grounded.ShouldBe(expected.Grounded, $"tick {tick}");
        actual.GroundNodeId.ShouldBe(expected.GroundNodeId, $"tick {tick}");
        actual.PrevButtons.ShouldBe(expected.PrevButtons, $"tick {tick}");
        actual.AirTicks.ShouldBe(expected.AirTicks, $"tick {tick}");
        actual.JumpBufferTicks.ShouldBe(expected.JumpBufferTicks, $"tick {tick}");
        actual.GroundSuppressTicks.ShouldBe(expected.GroundSuppressTicks, $"tick {tick}");
        actual.SteppedUpBy.ShouldBe(expected.SteppedUpBy, $"tick {tick}");
    }

    // Every bit, so -0 and 0 cannot pass as equal.
    private static (int X, int Y, int Z) Bits(Vector3 value) => (
        BitConverter.SingleToInt32Bits(value.X),
        BitConverter.SingleToInt32Bits(value.Y),
        BitConverter.SingleToInt32Bits(value.Z));

    // Leaves the state as it finds it: standing on the platform.
    private sealed class StandingMover : ICharacterMover
    {
        public void Tick(
            ref CharacterState state,
            in CharacterCommand command,
            ICharacterCollisionSource source,
            CharacterTuning tuning,
            float deltaTime)
        {
        }
    }

    // A world floor with its top at 0 and a part platform, 4 by 0.5 by 4,
    // with its top at 1 and well away from the origin.
    private sealed class Rig
    {
        private Rig()
        {
            SceneNode floor = Scene.Root.CreateChild("floor");
            floor.LocalPosition = new Vector3(0f, -0.5f, 0f);
            floor.Brush = Brush.CreateBox(new Vector3(-32f, -0.5f, -32f), new Vector3(32f, 0.5f, 32f));
            Scene.RebuildStaticWorld(new FakeRenderer());

            Platform = AddPart("platform", new Vector3(4f, 0.5f, 4f), new Vector3(8f, 0.75f, -6f));
            PlatformStart = Platform.LocalPosition;
            Character = new CharacterSimulation(Scene);
        }

        public Scene Scene { get; } = new("Carry");

        public SceneNode Platform { get; }

        public Vector3 PlatformStart { get; }

        public CharacterSimulation Character { get; }

        public static Rig OnAPlatform(Vector3 fromItsCentre = default)
        {
            var rig = new Rig();
            rig.Settle(rig.PlatformStart + fromItsCentre + new Vector3(0f, 0.3f, 0f));
            rig.Character.State.GroundNodeId.ShouldBe(rig.Platform.Id);
            return rig;
        }

        public static Rig OnTheFloor()
        {
            var rig = new Rig();
            rig.Settle(new Vector3(0f, 0.05f, 0f));
            rig.Character.State.GroundNodeId.ShouldBe(Guid.Empty);
            return rig;
        }

        public SceneNode AddPart(string name, Vector3 size, Vector3 position)
        {
            SceneNode node = Scene.Root.CreateChild(name);
            node.LocalPosition = position;

            // Kind before brush, or the node is briefly a world brush.
            node.BrushKind = BrushKind.Part;
            node.Brush = Brush.CreateBox(size * -0.5f, size * 0.5f);
            return node;
        }

        // Compiled at once, so the character collides with it from the next tick.
        public SceneNode AddWorld(string name, Vector3 size, Vector3 position)
        {
            SceneNode node = Scene.Root.CreateChild(name);
            node.LocalPosition = position;
            node.Brush = Brush.CreateBox(size * -0.5f, size * 0.5f);
            Scene.RebuildStaticWorld(new FakeRenderer());
            return node;
        }

        public void Pose(Vector3 position) => Platform.LocalPosition = position;

        private void Settle(Vector3 feet)
        {
            Character.SpawnPosition = feet;
            Character.Spawn();
            for (int tick = 0; tick < 60; tick++)
                Character.Tick(default, Dt);

            Character.State.Grounded.ShouldBeTrue();
        }
    }
}
