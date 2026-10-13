using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The site's page on moving a part, held to the code: its scripts are
/// samples luau-lsp checks, each does in a level what the page says it does,
/// and the page quotes what the engine says.
/// </summary>
public sealed class MovePartGuideTests
{
    private const string Folder = "Guides/move_a_part/";

    // The file Insert Script makes for a part that is called Part.
    private const string PartScript = "Scripts/Part.luau";

    private static readonly SitePage Page = SitePage.At("guides/move-a-part-with-a-script.mdx");

    private static readonly Vector3 Home = new(0f, 2f, 0f);

    // Where the middle of a new part is: it is 2 units a side and lands on the ground plate.
    private static readonly Vector3 OnThePlate = new(0f, 1f, 0f);

    private static string Code(string name) => ScriptSamples.At(Folder + name).Code;

    [Fact]
    public void The_page_shows_its_samples_in_the_order_a_person_writes_them()
    {
        Page.Blocks("luau").ShouldBe(
        [
            Code("slide.luau"), Code("slide_and_turn.luau"), Code("where.luau"), Code("lift.luau"), Code("endless.luau"),
        ]);
    }

    [Fact]
    public void The_slide_is_a_unit_each_way_by_the_seconds_the_ticks_add_up_to_and_stop_puts_the_part_back()
    {
        using var level = new ScriptLevel();
        SceneNode part = level.Part("Part", Home);
        level.ScriptAt(PartScript, Code("slide.luau"), under: part);

        level.Play().Tick(60);

        // A second of ticks, each a sixtieth.
        level.Errors.ShouldBeEmpty();
        part.WorldPosition.X.ShouldBe(0f);
        part.WorldPosition.Y.ShouldBe(Home.Y + MathF.Sin(1f), 1e-4f);

        level.Stop();

        part.LocalPosition.ShouldBe(Home);
    }

    [Fact]
    public void The_frame_slides_the_part_and_turns_it_a_radian_a_second()
    {
        using var level = new ScriptLevel();
        SceneNode part = level.Part("Part", Home);
        level.ScriptAt(PartScript, Code("slide_and_turn.luau"), under: part);

        level.Play().Tick(60);

        level.Errors.ShouldBeEmpty();
        CFrame expected = new CFrame(Home + new Vector3(0f, MathF.Sin(1f), 0f)) * CFrame.Angles(0f, 1f, 0f);
        part.CFrame.FuzzyEq(expected, 1e-4f).ShouldBeTrue($"the part is at {part.CFrame}");
    }

    [Fact]
    public void A_part_under_the_slide_moves_in_the_world_and_stays_where_it_is_under_its_parent()
    {
        using var level = new ScriptLevel();
        SceneNode part = level.Part("Part", OnThePlate);
        level.ScriptAt(PartScript, Code("slide_and_turn.luau"), under: part);

        // On top of the first, as the page has the reader put it.
        SceneNode rider = level.Part("Rider", new Vector3(0f, 2f, 0f), parent: part);
        level.ScriptAt("Scripts/Rider.luau", Code("where.luau"), under: rider);

        level.Play().Tick(61);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "in the world 3.00, under its parent 2.00",
            "in the world 3.84, under its parent 2.00",
        ]);
        foreach (string line in level.Printed)
            Page.Text.ShouldContain(line);

        Page.Text.ShouldContain("A new part is 2 units on every side");
    }

    [Fact]
    public void The_lift_takes_two_seconds_each_way_and_rests_a_second_at_each_end()
    {
        using var level = new ScriptLevel();
        SceneNode part = level.Part("Part", Home);
        level.ScriptAt(PartScript, Code("lift.luau"), under: part);

        level.Play().Tick(60);
        part.WorldPosition.Y.ShouldBe(Home.Y + 1.5f, 1e-4f);

        level.Tick(60);
        part.WorldPosition.Y.ShouldBe(Home.Y + 3f, 1e-4f);

        // At rest for a second, then down.
        level.Tick(60);
        part.WorldPosition.Y.ShouldBe(Home.Y + 3f, 1e-4f);
        level.Tick(120);

        level.Errors.ShouldBeEmpty();
        part.WorldPosition.Y.ShouldBe(Home.Y, 1e-4f);
    }

    [Fact]
    public void A_wait_is_a_whole_number_of_ticks_as_the_page_counts_them()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local RunService = game:GetService("RunService")
            local ticks = 0
            RunService.Heartbeat:Connect(function() ticks += 1 end)
            for _, seconds in { 0, 0.04, 1 } do
                local before = ticks
                local passed = task.wait(seconds)
                print(seconds, ticks - before, string.format("%.2f", passed))
            end
            """);

        level.Play().Tick(80);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["0 1 0.02", "0.04 3 0.05", "1 60 1.00"]);
        Page.Text.ShouldContain("`task.wait(0.04)` waits 3 ticks, which is 0.05 seconds");
    }

    [Fact]
    public void The_loop_with_no_wait_is_stopped_with_the_words_the_page_quotes_and_the_level_plays_on()
    {
        using var level = new ScriptLevel();
        SceneNode part = level.Part("Part", Home);
        level.ScriptAt(PartScript, Code("endless.luau"), under: part);
        SceneNode other = level.Part("Other", new Vector3(4f, 2f, 0f));
        level.ScriptAt("Scripts/Other.luau", Code("slide.luau"), under: other);

        level.Play();
        level.StopTheNextHangSoon();
        level.Tick(60);

        ScriptMessage failure = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        failure.Script.ShouldBe(PartScript);
        failure.Text.ShouldBe("the script ran for more than 0.1 s without a pause and was switched off");
        other.WorldPosition.Y.ShouldBeGreaterThan(2.5f, "the other script of the level ran on");

        // The page speaks of the limit a level has when nobody shortens it.
        double limit = LuauHost.DefaultCallLimit.TotalSeconds;
        Page.Text.ShouldContain($"the script ran for more than {limit:0} s without a pause and was switched off");
        Page.Text.ShouldContain($"`Part.luau:{failure.Line}`");
    }

    [Fact]
    public void A_script_that_moves_a_block_fails_with_the_sentence_the_page_quotes()
    {
        using var level = new ScriptLevel();
        level.ScriptAt("Scripts/Baseplate.luau", Code("slide.luau"), under: level.Baseplate);

        level.Play().Tick(3);

        // Once a tick: the connection stays, and each call of it fails.
        const string why = "cannot be moved: a world brush sits at or below it, and only parts can move while a " +
            "level plays. Convert the brush to a part.";
        level.Sink.Messages.Count.ShouldBe(3);
        level.Errors.ShouldAllBe(text => text == $"Baseplate {why}");

        // The page's block is one the reader inserted, which the editor names Brush.
        Page.Text.ShouldContain($"`Brush {why}`");
        Page.Text.ShouldContain("The line starts with the block's name");
        Page.Text.ShouldContain("once for every tick");
    }
}
