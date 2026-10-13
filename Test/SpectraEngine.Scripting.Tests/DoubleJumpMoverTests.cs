using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The second mover the engine ships: the walk with a second jump in the
/// air. It is the walk's file asked for by another, and a few lines more.
/// </summary>
public sealed class DoubleJumpMoverTests
{
    private const string Installs = """
        game:GetService("Players").LocalPlayer.Character:SetMover(require("./movers/double_jump"))
        """;

    private static readonly CharacterCommand Stand = default;
    private static readonly CharacterCommand Jump = new() { Buttons = CharacterButtons.Jump };

    [Fact]
    public void A_second_jump_in_the_air_lifts_the_player_about_twice_as_high()
    {
        using MoverLevel walk = new MoverLevel().WithTheShippedMover();
        using MoverLevel doubled = new MoverLevel().WithScript(Installs);

        float once = HighestOf(walk, pressesInTheAir: 1);
        float twice = HighestOf(doubled, pressesInTheAir: 1);

        walk.Errors.ShouldBeEmpty();
        doubled.Errors.ShouldBeEmpty();

        // A tick is a sixtieth of a second, which costs a jump a little of its height.
        once.ShouldBe(walk.Character.Tuning.JumpHeight, 0.1f, "the walk jumps once, however often jump is pressed");
        twice.ShouldBeGreaterThan(once * 1.8f);
        twice.ShouldBeLessThan(once * 2.1f);
    }

    [Fact]
    public void A_third_press_in_the_air_does_nothing_and_the_ground_gives_the_second_jump_back()
    {
        using MoverLevel level = new MoverLevel().WithScript(Installs);

        float first = HighestOf(level, pressesInTheAir: 3);
        level.State.Grounded.ShouldBeTrue();
        level.State.Memory[0].ShouldBe(0f, "standing, no jump in the air is counted");
        float second = HighestOf(level, pressesInTheAir: 1, isStarted: true);

        level.Errors.ShouldBeEmpty();
        float height = level.Character.Tuning.JumpHeight;
        first.ShouldBeLessThan(height * 2.1f);
        second.ShouldBe(first, 0.05f);
    }

    // A jump kept for the landing would be a third jump nobody asked for.
    [Fact]
    public void The_press_of_the_second_jump_is_not_kept_for_the_landing()
    {
        using MoverLevel level = new MoverLevel().WithScript(Installs);
        level.Play().Tick(in Stand, 30);

        level.Tick(in Jump).Tick(in Stand, 28).Tick(in Jump);
        int jumps = 2;
        bool wasGrounded = false;
        for (int tick = 0; tick < 240; tick++)
        {
            level.Tick(in Stand);
            if (wasGrounded && !level.State.Grounded)
                jumps++;
            wasGrounded = level.State.Grounded;
        }

        level.Errors.ShouldBeEmpty();
        jumps.ShouldBe(2);
        level.State.Grounded.ShouldBeTrue();
    }

    // Jumps from standing, presses jump again near the top so many times,
    // and comes down. How high the feet got above where they stood.
    private static float HighestOf(MoverLevel level, int pressesInTheAir, bool isStarted = false)
    {
        if (!isStarted)
            level.Play();

        level.Tick(in Stand, 30);
        float ground = level.State.Position.Y;
        float highest = ground;
        level.Tick(in Jump);
        for (int tick = 1; tick < 240; tick++)
        {
            // Near the top of the first jump, and every tenth tick after.
            bool presses = tick >= 28 && (tick - 28) % 10 == 0 && (tick - 28) / 10 < pressesInTheAir;
            level.Tick(presses ? Jump : Stand);
            highest = MathF.Max(highest, level.State.Position.Y);
        }

        return highest - ground;
    }
}
