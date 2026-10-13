using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The demo's lift with the player on it, moved by the mover in Luau. The
/// engine carries whoever stands on a part that moves, and it knows the
/// part from what the mover says the player stands on.
/// </summary>
public sealed class LuauMoverLiftTests
{
    // On the lift in the start room's corner, a little above its top.
    private static readonly Vector3 OnTheLift = new(123f, 0.35f, -3f);

    private const float Tolerance = 1e-4f;

    [Fact]
    public void The_player_rides_the_lift_up_waits_with_it_and_rides_it_down()
    {
        using MoverLevel level = new MoverLevel().WithTheShippedMover();
        SceneNode lift = Lift(level);
        level.Play().Tick(default, 5);
        level.Character.Teleport(OnTheLift);
        level.Tick(default, 20);
        float standing = level.State.Position.Y;
        level.State.GroundNodeId.ShouldBe(lift.Id, "the mover says it stands on the lift");

        // 2.2 units at 1.5 a second is 88 ticks, and it stays up for 3 seconds.
        SendUp(level, lift);
        level.Tick(default, 150);
        float top = level.State.Position.Y;
        bool stoodAtTheTop = level.State.Grounded && level.State.GroundNodeId == lift.Id;
        level.Tick(default, 250);

        level.Errors.ShouldBeEmpty();
        top.ShouldBe(standing + 2.2f, 1e-3f);
        stoodAtTheTop.ShouldBeTrue();
        level.State.Position.Y.ShouldBe(standing, 1e-3f);
        level.State.Grounded.ShouldBeTrue();
        new Vector2(level.State.Position.X, level.State.Position.Z).ShouldBe(new Vector2(OnTheLift.X, OnTheLift.Z));
    }

    [Fact]
    public void The_ride_is_the_one_the_built_in_mover_gives()
    {
        using var builtIn = new MoverLevel();
        using MoverLevel scripted = new MoverLevel().WithTheShippedMover();

        List<CharacterState> expected = Ride(builtIn);
        List<CharacterState> ridden = Ride(scripted);

        scripted.Errors.ShouldBeEmpty();
        for (int tick = 0; tick < expected.Count; tick++)
        {
            Vector3.Distance(ridden[tick].Position, expected[tick].Position).ShouldBeLessThan(Tolerance, $"after tick {tick}");
            ridden[tick].Grounded.ShouldBe(expected[tick].Grounded, $"after tick {tick}");
        }
    }

    // Walks off the lift while it is on its way up, and falls.
    private static List<CharacterState> Ride(MoverLevel level)
    {
        SceneNode lift = Lift(level);
        level.Play().Tick(default, 5);
        level.Character.Teleport(OnTheLift);
        level.Tick(default, 20);
        SendUp(level, lift);

        var states = new List<CharacterState>();
        for (int tick = 0; tick < 240; tick++)
        {
            level.Tick(tick is > 60 and < 120 ? MoverCourse.Walk(0f) : default);
            states.Add(level.State);
        }

        return states;
    }

    private static SceneNode Lift(MoverLevel level) =>
        level.Scene.Root.Children.Single(node => node.Name == DemoPlayArea.LiftName);

    private static void SendUp(MoverLevel level, SceneNode lift)
    {
        EntityWorld entities = level.Manager.EntityWorld.ShouldNotBeNull();
        entities.TryFindOwner(lift, out Entity? owner).ShouldBeTrue();
        entities.QueueInput(owner.ShouldNotBeNull(), "Open");
    }
}
