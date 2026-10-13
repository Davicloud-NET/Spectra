using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The site's page on the player's mover, held to the code: it shows the
/// movers the engine ships and one more, the one more jumps as high as the
/// page says, and a slip in it ends with the line the page quotes.
/// </summary>
public sealed class MoverGuideTests
{
    private const string Moon = "Scripts/movers/moon.luau";
    private const string Typo = ScriptSamples.MistakesFolder + "/moon_typo.luau";

    private static readonly SitePage Page = SitePage.At("guides/change-how-the-player-moves.mdx");

    private static readonly CharacterCommand Stand = default;
    private static readonly CharacterCommand Jump = new() { Buttons = CharacterButtons.Jump };

    // The installer the engine ships, with the path the page tells a person to type.
    private static string InstallerFor(string mover) =>
        ScriptSamples.At(MoverLevel.Installer).Code.Replace("./movers/walk", mover, StringComparison.Ordinal);

    [Fact]
    public void The_page_shows_the_installer_the_double_jump_and_the_moon_walk()
    {
        Page.Blocks("luau").ShouldBe(
        [
            ScriptSamples.At(MoverLevel.Installer).Code, ScriptSamples.At(MoverLevel.DoubleJump).Code,
            ScriptSamples.At(Moon).Code,
        ]);
    }

    [Fact]
    public void The_page_names_the_shipped_files_and_the_line_the_installer_prints()
    {
        Page.Text.ShouldContain($"`Assets/{MoverLevel.Walk}`");
        Page.Text.ShouldContain($"`Assets/{MoverLevel.DoubleJump}`");
        Page.Text.ShouldContain("`./movers/double_jump`");
        Page.Text.ShouldContain("`./movers/moon`");

        using MoverLevel level = new MoverLevel().WithTheShippedMover();
        level.Play().Tick(in Stand, 2);

        level.Errors.ShouldBeEmpty();
        Page.Text.ShouldContain($"`{level.Printed.ShouldHaveSingleItem()}`");
    }

    [Fact]
    public void Under_a_third_of_the_gravity_a_jump_is_about_three_times_as_high()
    {
        using MoverLevel walk = new MoverLevel().WithTheShippedMover();
        using MoverLevel moon = new MoverLevel().WithScript(InstallerFor("./movers/moon"));
        moon.Files.Add(Moon, ScriptSamples.At(Moon).Code);

        float once = HighestOf(walk);
        float high = HighestOf(moon);

        walk.Errors.ShouldBeEmpty();
        moon.Errors.ShouldBeEmpty();
        high.ShouldBeGreaterThan(once * 2.6f);
        high.ShouldBeLessThan(once * 3.2f);
        moon.State.Grounded.ShouldBeTrue("the player came down again");
    }

    [Fact]
    public void The_slip_ends_the_mover_in_the_air_with_the_line_the_page_quotes_and_the_player_lands()
    {
        using MoverLevel level = new MoverLevel().WithScript(InstallerFor("./movers/moon"));
        level.Files.Add(Moon, ScriptSamples.At(Moon).Code.Replace("tuning.Gravity", "tuning.Gravty", StringComparison.Ordinal));

        HighestOf(level);

        ScriptMessage failure = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        failure.Text.ShouldBe(
            "The mover failed: Gravty is not a valid member of CharacterTuning \"CharacterTuning\". " +
            LuauCharacterMover.TookOver);
        failure.Script.ShouldBe(Moon);
        level.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
        level.State.Grounded.ShouldBeTrue("the built-in mover brought the player down");

        Page.Text.ShouldContain($"`{failure.Text}`");
        Page.Text.ShouldContain($"`moon.luau:{failure.Line}`");
        ScriptSamples.At(Typo).MarkedLines.ShouldBe([failure.Line], "the checker and the level name the same line");
    }

    // The page tells a person to type the slip, and says the checker marks
    // it. The sample the checker is held to is the page's mover with that slip.
    [Fact]
    public void The_mistake_the_checker_is_held_to_is_the_moon_walk_with_the_slip_the_page_names()
    {
        string slipped = ScriptSamples.At(Moon).Code
            .Replace("require(\"./walk\")", "require(\"../Scripts/movers/walk\")", StringComparison.Ordinal)
            .Replace("tuning.Gravity", "tuning.Gravty", StringComparison.Ordinal);

        ScriptSamples.At(Typo).Code.ShouldBe(slipped);
        Page.Text.ShouldContain("`tuning.Gravty`");
    }

    [Fact]
    public void The_page_quotes_what_a_mover_that_waits_and_a_second_mover_after_a_failure_are_told()
    {
        Page.Text.ShouldContain($"`{MoverCall.Waited}. {LuauCharacterMover.TookOver}`");
        Page.Text.ShouldContain($"`{MoverSlot.FailedBefore}`");
    }

    // Jumps from standing and comes down. How high the feet got above where they stood.
    private static float HighestOf(MoverLevel level)
    {
        level.Play().Tick(in Stand, 30);
        float ground = level.State.Position.Y;
        float highest = ground;
        level.Tick(in Jump);
        for (int tick = 1; tick < 600; tick++)
        {
            level.Tick(in Stand);
            highest = MathF.Max(highest, level.State.Position.Y);
        }

        return highest - ground;
    }
}
