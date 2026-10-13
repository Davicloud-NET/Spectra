using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Entities;
using static SpectraEngine.Scripting.Tests.DoorLevel;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The site's page on hearing a level, held to the code: a script hears the
/// built-in door and counter and switches a light, and what the page quotes
/// of Output is what they print.
/// </summary>
public sealed class HearLevelGuideTests
{
    private const string Folder = "Guides/hear_the_level/";

    private static readonly SitePage Page = SitePage.At("guides/hear-what-the-level-does.mdx");

    private static string Code(string name) => ScriptSamples.At(Folder + name).Code;

    [Fact]
    public void The_page_shows_its_samples_in_the_order_a_person_writes_them()
    {
        Page.Blocks("luau").ShouldBe([Code("door_report.luau"), Code("hear_counter.luau"), Code("flicker.luau")]);
    }

    [Fact]
    public void The_door_says_what_it_does_and_reads_the_speed_it_was_built_with()
    {
        // No speed set: the script reads what the class gives a door.
        using ScriptLevel level = Build(out _, out SceneNode door, doorSpeed: null);
        level.ScriptAt(DoorScript, Code("door_report.luau"), under: door);

        level.Play().Tick();
        foreach (string input in new[] { "Open", "Close", "Open", "Close" })
        {
            level.Entities.QueueInput(Live(level, door), input);
            level.Tick(30);
        }

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "the door moves 2 units a second", "opening", "open for the first time", "closed again", "opening",
            "closed again",
        ]);
        Page.Text.ShouldContain($"`{level.Printed[0]}`");
    }

    // The page has the reader wire the button to the counter again, and
    // leaves the door's own script in its file.
    [Fact]
    public void A_script_hears_the_counter_count_in_numbers_and_opens_the_door_a_second_after_the_third()
    {
        using ScriptLevel level = Build(out SceneNode button, out SceneNode door);
        AddCounter(level, opensTheDoor: false);
        button.Entity.ShouldNotBeNull().Connections.Add(Wire(FuncButton.OnPressed, "Presses", "Add"));
        level.ScriptAt(DoorScript, Code("door_report.luau"), under: door);
        level.ScriptAt("Scripts/Level.luau", Code("hear_counter.luau"));

        level.Play().Tick();
        for (int press = 1; press <= 3; press++)
        {
            Press(level, button);
            level.Tick(TicksPerPress);
        }

        level.Printed.ShouldBe(
        [
            "the door moves 6 units a second", "pressed 1 of 3 times, 2 to go", "pressed 2 of 3 times, 1 to go",
            "pressed 3 of 3 times, 0 to go",
        ]);
        Live(level, door).TicksTravelled.ShouldBe(0, "the script waits a second first");

        level.Tick(60 + TicksPerPress);

        level.Errors.ShouldBeEmpty();
        Live(level, door).IsFullyOpen.ShouldBeTrue();
        Page.Text.ShouldContain($"`{level.Printed[1]}`");
        Page.Text.ShouldContain("add a wire under Sends: `OnPressed`, Target `Presses`, Input `Add`");
    }

    [Fact]
    public void With_no_wire_from_the_button_the_counter_never_counts_and_the_script_hears_nothing()
    {
        using ScriptLevel level = Build(out SceneNode button, out _);
        AddCounter(level, opensTheDoor: false);
        level.ScriptAt("Scripts/Level.luau", Code("hear_counter.luau"));

        level.Play().Tick();
        Press(level, button);
        level.Tick(TicksPerPress);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBeEmpty();
    }

    [Fact]
    public void The_lamp_flickers_every_six_ticks_until_the_door_opens_and_is_as_it_was_built_at_Stop()
    {
        using ScriptLevel level = Build(out _, out SceneNode door);
        SceneNode lamp = level.Scene.Root.CreateChild("Lamp");
        Light light = lamp.Light = new Light { Kind = LightKind.Point };
        level.ScriptAt("Scripts/Lamp.luau", Code("flicker.luau"), under: lamp);

        level.Play();
        for (int tick = 1; tick <= 24; tick++)
        {
            level.Tick();
            light.Enabled.ShouldBe(((tick - 1) / 6) % 2 == 1, $"tick {tick}");
        }

        level.Entities.QueueInput(Live(level, door), "Open");
        level.Tick(30);

        level.Errors.ShouldBeEmpty();
        light.Enabled.ShouldBeTrue();

        light.Enabled = false;
        level.Tick(30);
        light.Enabled.ShouldBeFalse("the loop has ended, so nothing switches the lamp again");

        level.Stop();
        light.Enabled.ShouldBeTrue();
    }

    // The page's table of what an output hands a script, row by row.
    [Theory]
    [InlineData(KeyvalueType.Float, "number", "| A number | a number |")]
    [InlineData(KeyvalueType.Int, "number", "| A number | a number |")]
    [InlineData(KeyvalueType.Bool, "boolean", "| `0` or `1` | a boolean |")]
    [InlineData(KeyvalueType.String, "string", "| Text | a string |")]
    [InlineData(KeyvalueType.Vec3, "Vector3", "| A vector, a colour or angles | a `Vector3` |")]
    [InlineData(KeyvalueType.Color, "Vector3", "| A vector, a colour or angles | a `Vector3` |")]
    [InlineData(KeyvalueType.Angles, "Vector3", "| A vector, a colour or angles | a `Vector3` |")]
    [InlineData(KeyvalueType.Vec2, "number number", "| Two or four numbers | that many numbers |")]
    [InlineData(KeyvalueType.Vec4, "number number number number", "| Two or four numbers | that many numbers |")]
    [InlineData(KeyvalueType.NodeRef, "Instance?", "| A node | the node, or `nil` |")]
    public void The_table_of_what_an_output_hands_a_script_has_a_row_for_every_kind(
        KeyvalueType sent, string handed, string row)
    {
        IReadOnlyList<ScriptParameter> values = EntityClassApi.SentValuesOf(ParameterType.Of(sent));

        string.Join(' ', values.Select(value => value.Type)).ShouldBe(handed);
        Page.Text.ShouldContain(row);
    }

    [Fact]
    public void An_output_that_sends_nothing_hands_a_script_nothing()
    {
        EntityClassApi.SentValuesOf(ParameterType.None).ShouldBeEmpty();
        Page.Text.ShouldContain("| Nothing | nothing |");
    }
}
