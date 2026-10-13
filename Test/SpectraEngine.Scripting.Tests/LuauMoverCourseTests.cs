using System.Numerics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The mover the engine ships in Luau, walked over the demo's course by the
/// commands the built-in mover is walked by. The two need not agree for
/// good. This holds the shipped one to what it was written to do.
/// </summary>
public sealed class LuauMoverCourseTests
{
    // How far apart the two may be after any tick, in units. They do the
    // same sums on the same floats, and Luau does a few of them in doubles:
    // over the course they were at most 0.000004 apart.
    private const float Tolerance = 1e-4f;

    [Fact]
    public void The_Luau_mover_walks_the_course_as_the_built_in_mover_does()
    {
        using var builtIn = new MoverLevel();
        using MoverLevel scripted = new MoverLevel().WithTheShippedMover();

        List<CharacterState> expected = MoverCourse.Run(builtIn.Play());
        List<CharacterState> walked = MoverCourse.Run(scripted.Play());

        scripted.Errors.ShouldBeEmpty();
        scripted.Printed.ShouldBe(["The player moves by a mover in Luau"]);
        scripted.Character.Mover.ShouldBeOfType<LuauCharacterMover>();
        builtIn.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);

        for (int tick = 0; tick < expected.Count; tick++)
        {
            Vector3.Distance(walked[tick].Position, expected[tick].Position).ShouldBeLessThan(Tolerance, $"where it is after tick {tick}");
            Vector3.Distance(walked[tick].Velocity, expected[tick].Velocity).ShouldBeLessThan(Tolerance * 60f, $"how it moves after tick {tick}");
            walked[tick].Grounded.ShouldBe(expected[tick].Grounded, $"whether it stands after tick {tick}");
            walked[tick].SteppedUpBy.ShouldBe(expected[tick].SteppedUpBy, Tolerance, $"the step of tick {tick}");
            walked[tick].SlideContacts.ShouldBe(Renamed(expected[tick].SlideContacts, builtIn.Scene, scripted.Scene), $"what tick {tick} slid along");
        }
    }

    // What the comparison above is worth: the commands have to take the
    // player through everything a mover does.
    [Fact]
    public void The_course_asks_a_mover_for_a_door_stairs_a_fall_a_ramp_a_part_a_jump_and_a_wall()
    {
        using MoverLevel level = new MoverLevel().WithTheShippedMover();
        SceneNode platform = level.Scene.Root.Children.Single(node => node.Name == "Play.PartPlatform");

        List<CharacterState> walked = MoverCourse.Run(level.Play());

        level.Errors.ShouldBeEmpty();
        walked.Count(state => state.SteppedUpBy > 0.2f).ShouldBeGreaterThanOrEqualTo(5, "the staircase has five risers");
        walked.ShouldContain(state => !state.Grounded && state.Velocity.Y < -4f, "it fell off the terrace");
        walked.ShouldContain(state => state.Grounded && state.GroundNormal.Y < 0.95f, "it stood on the ramp");
        walked.ShouldContain(state => state.SlideContacts.Contains(platform.Id), "it walked into the side of the part");
        walked.ShouldContain(state => state.Grounded && state.GroundNodeId == platform.Id, "it stood on the part");
        walked.Max(state => state.Position.X).ShouldBe(168.64f, 0.01f, "the east wall stopped it");
        walked.Max(state => state.Position.Z).ShouldBe(18.64f, 0.01f, "and the corner");
        walked[^1].Velocity.Length().ShouldBeLessThan(0.01f, "it stood still at the end");
    }

    [Fact]
    public void The_same_commands_played_twice_give_the_same_states_bit_for_bit()
    {
        using MoverLevel first = new MoverLevel().WithTheShippedMover();
        using MoverLevel second = new MoverLevel().WithTheShippedMover();

        List<CharacterState> once = MoverCourse.Run(first.Play());
        List<CharacterState> again = MoverCourse.Run(second.Play());

        first.Errors.ShouldBeEmpty();
        second.Errors.ShouldBeEmpty();
        for (int tick = 0; tick < once.Count; tick++)
            StateBits.Of(again[tick], second.Scene).ShouldBe(StateBits.Of(once[tick], first.Scene), $"after tick {tick}");
    }

    // A state put back and the same commands again, as a rollback does. The
    // second jump of the double jump is kept in the state, so it comes back.
    [Fact]
    public void A_state_put_back_and_played_again_ends_where_it_ended_before()
    {
        using MoverLevel level = new MoverLevel().WithScript("""
            game:GetService("Players").LocalPlayer.Character:SetMover(require("./movers/double_jump"))
            """);
        level.Play().Tick(default, 30);
        CharacterCommand ahead = MoverCourse.Walk(0f);
        CharacterCommand jump = MoverCourse.Walk(0f, CharacterButtons.Jump);

        // In the air after the first jump, with the second one still to make.
        level.Tick(in jump).Tick(in ahead, 10);
        CharacterState kept = level.State;
        string[] first = AJumpAndALanding(level, in ahead, in jump);

        level.Character.Restore(in kept);
        string[] second = AJumpAndALanding(level, in ahead, in jump);

        level.Errors.ShouldBeEmpty();
        second.ShouldBe(first);

        // One jump in the air was counted, which is 1 as a float's bits.
        kept.Memory[0].ShouldBe(0f);
        first.ShouldContain(state => state.Contains("keeps=3f800000,"));
        level.State.Grounded.ShouldBeTrue();
    }

    private static string[] AJumpAndALanding(MoverLevel level, in CharacterCommand ahead, in CharacterCommand jump)
    {
        var states = new List<string>();
        for (int tick = 0; tick < 120; tick++)
        {
            level.Tick(tick == 5 ? jump : ahead);
            states.Add(StateBits.Of(level.State, level.Scene));
        }

        return [.. states];
    }

    // The same parts by the ids they have in another level built alike.
    private static CharacterSlideContacts Renamed(CharacterSlideContacts contacts, Scene from, Scene to)
    {
        var renamed = default(CharacterSlideContacts);
        for (int i = 0; i < contacts.Count; i++)
        {
            from.TryFindById(contacts[i], out SceneNode? node).ShouldBeTrue();
            renamed.Add(to.Root.Traverse().Single(other => other.Name == node.ShouldNotBeNull().Name).Id);
        }

        return renamed;
    }
}
