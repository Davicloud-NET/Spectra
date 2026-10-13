using System.Numerics;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Entities;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The site's page on a door opened three ways, held to the code: the wire,
/// the counter and the script each open the built-in door in a level, and
/// what the page quotes of the watch and of Output is what they print.
/// </summary>
public sealed class TalkToLevelGuideTests
{
    private const string Folder = "Guides/talk_to_the_level/";
    private const string Typo = ScriptSamples.MistakesFolder + "/third_press_typo.luau";

    // The file Insert Script makes for a part that is called Door.
    private const string DoorScript = "Scripts/Door.luau";

    // A press every 12 ticks. The button is back out two ticks after one.
    private const int TicksPerPress = 12;

    private static readonly SitePage Page = SitePage.At("guides/talk-to-the-level.mdx");

    private static string Code(string name) => ScriptSamples.At(Folder + name).Code;

    [Fact]
    public void The_page_shows_its_samples_in_the_order_a_person_writes_them()
    {
        Page.Blocks("luau").ShouldBe(
        [
            Code("third_press.luau"), Code("third_press_checked.luau"), ScriptSamples.At(Typo).Code,
            Code("door_report.luau"), Code("hear_counter.luau"), Code("flicker.luau"),
        ]);
    }

    [Fact]
    public void One_wire_opens_the_door_on_the_first_press()
    {
        using ScriptLevel level = Level(out SceneNode button, out SceneNode door);
        button.Entity.ShouldNotBeNull().Connections.Add(Wire(FuncButton.OnPressed, "Door", "Open"));

        level.Play().Tick();
        Press(level, button);
        level.Tick(TicksPerPress);

        level.Errors.ShouldBeEmpty();
        Live(level, door).IsFullyOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_counter_and_two_wires_open_the_door_on_the_third_press()
    {
        using ScriptLevel level = Level(out SceneNode button, out SceneNode door);
        Counter(level);
        button.Entity.ShouldNotBeNull().Connections.Add(Wire(FuncButton.OnPressed, "Presses", "Add"));

        level.Play().Tick();
        for (int press = 1; press <= 2; press++)
        {
            Press(level, button);
            level.Tick(TicksPerPress);
        }

        Live(level, door).TicksTravelled.ShouldBe(0);
        Press(level, button);
        level.Tick(TicksPerPress);

        level.Errors.ShouldBeEmpty();
        Live(level, door).IsFullyOpen.ShouldBeTrue();
    }

    [Fact]
    public void The_script_counts_the_presses_and_opens_the_door_on_the_third()
    {
        using ScriptLevel level = Level(out SceneNode button, out SceneNode door);
        level.ScriptAt(DoorScript, Code("third_press.luau"), under: door);

        level.Play().Tick();
        for (int press = 1; press <= 2; press++)
        {
            Press(level, button);
            level.Tick(TicksPerPress);
        }

        Live(level, door).TicksTravelled.ShouldBe(0);
        Press(level, button);
        level.Tick(TicksPerPress);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["press 1", "press 2", "press 3"]);
        Live(level, door).IsFullyOpen.ShouldBeTrue();
        foreach (string line in level.Printed)
            Page.Text.ShouldContain($"`{line}`");
    }

    [Fact]
    public void The_checked_script_does_what_the_plain_one_does()
    {
        using ScriptLevel level = Level(out SceneNode button, out SceneNode door);
        level.ScriptAt(DoorScript, Code("third_press_checked.luau"), under: door);

        level.Play().Tick();
        for (int press = 1; press <= 3; press++)
        {
            Press(level, button);
            level.Tick(TicksPerPress);
        }

        level.Errors.ShouldBeEmpty();
        Live(level, door).IsFullyOpen.ShouldBeTrue();
    }

    [Fact]
    public void The_slip_on_the_page_fails_on_the_third_press_with_the_line_the_page_quotes()
    {
        using ScriptLevel level = Level(out SceneNode button, out SceneNode door);
        level.ScriptAt(DoorScript, ScriptSamples.At(Typo).Code, under: door);

        level.Play().Tick();
        for (int press = 1; press <= 3; press++)
        {
            Press(level, button);
            level.Tick(TicksPerPress);
        }

        ScriptMessage failure = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        failure.Text.ShouldBe("Opn is not a valid member of func_door \"Door\"");
        Live(level, door).TicksTravelled.ShouldBe(0);

        Page.Text.ShouldContain($"`{failure.Text}`");
        Page.Text.ShouldContain($"`Door.luau:{failure.Line}`");
        ScriptSamples.At(Typo).MarkedLines.ShouldBe([failure.Line], "the checker and the level name the same line");
    }

    [Fact]
    public void The_watch_prints_the_lines_the_page_shows_for_the_third_press()
    {
        using ScriptLevel level = Level(out SceneNode button, out SceneNode door);
        level.ScriptAt(DoorScript, Code("third_press.luau"), under: door);
        var output = new ConsoleOutput();
        var watch = new EntityWatch(output);
        watch.TurnOn();
        level.Manager.EntityTrace = watch.ActiveTrace;

        level.Play().Tick();
        for (int press = 1; press <= 2; press++)
        {
            Press(level, button);
            level.Tick(TicksPerPress);
        }

        output.Drain();
        Press(level, button);
        level.Tick();

        // The page stops at the door. This button is all the way in within
        // the tick, which one a person builds is not.
        string[] lines = [.. output.Drain().Select(line => line.Text).Take(4)];
        int tick = 2 + 2 * TicksPerPress;
        lines.ShouldBe(
        [
            $"ent {tick} send game -> Button.Use",
            $"ent {tick} fire Button.OnPressed wires=0 listeners=1",
            $"ent {tick} send Scripts/Door.luau:10 -> Door.Open",
            $"ent {tick} fire Door.OnOpen wires=0",
        ]);

        // The page shows them at a tick of its own.
        foreach (string line in lines)
            Page.Text.ShouldContain(line.Replace($"ent {tick} ", "ent 412 ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_door_says_what_it_does_and_reads_the_speed_it_was_built_with()
    {
        // No speed set: the script reads what the class gives a door.
        using ScriptLevel level = Level(out _, out SceneNode door, doorSpeed: null);
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

    [Fact]
    public void A_script_hears_the_counter_count_in_numbers_and_opens_the_door_a_second_after_the_third()
    {
        using ScriptLevel level = Level(out SceneNode button, out SceneNode door);
        Counter(level, opensTheDoor: false);
        button.Entity.ShouldNotBeNull().Connections.Add(Wire(FuncButton.OnPressed, "Presses", "Add"));
        level.ScriptAt("Scripts/Level.luau", Code("hear_counter.luau"));

        level.Play().Tick();
        for (int press = 1; press <= 3; press++)
        {
            Press(level, button);
            level.Tick(TicksPerPress);
        }

        level.Printed.ShouldBe(
        [
            "pressed 1 of 3 times, 2 to go", "pressed 2 of 3 times, 1 to go", "pressed 3 of 3 times, 0 to go",
        ]);
        Live(level, door).TicksTravelled.ShouldBe(0, "the script waits a second first");

        level.Tick(60 + TicksPerPress);

        level.Errors.ShouldBeEmpty();
        Live(level, door).IsFullyOpen.ShouldBeTrue();
        Page.Text.ShouldContain($"`{level.Printed[0]}`");
    }

    [Fact]
    public void The_lamp_flickers_every_six_ticks_until_the_door_opens_and_is_as_it_was_built_at_Stop()
    {
        using ScriptLevel level = Level(out _, out SceneNode door);
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

    // A button and a door, away from where the player starts so the door
    // squeezes nobody. No wire between them.
    private static ScriptLevel Level(out SceneNode button, out SceneNode door, string? doorSpeed = "6")
    {
        var level = new ScriptLevel();

        button = level.Part("Button", new Vector3(20f, 1f, 20f));
        var buttonData = new EntityData("func_button");
        buttonData.SetValue("distance", "0.1");
        buttonData.SetValue("speed", "6");
        buttonData.SetValue("wait", "0");
        button.Entity = buttonData;

        door = level.Part("Door", new Vector3(24f, 1f, 20f));
        var doorData = new EntityData("func_door");
        doorData.SetValue("distance", "0.5");
        doorData.SetValue("wait", "-1");
        if (doorSpeed is not null)
            doorData.SetValue("speed", doorSpeed);

        door.Entity = doorData;
        return level;
    }

    // The counter of the page: it is full at three.
    private static void Counter(ScriptLevel level, bool opensTheDoor = true)
    {
        var data = new EntityData("math_counter");
        data.SetValue("max", "3");
        if (opensTheDoor)
            data.Connections.Add(Wire(MathCounter.OnHitMax, "Door", "Open"));

        level.Scene.Root.CreateChild("Presses").Entity = data;
    }

    private static EntityConnection Wire(string output, string target, string input) =>
        new(output, target, input, string.Empty, 0f, EntityConnection.Infinite);

    // What the player's use key sends.
    private static void Press(ScriptLevel level, SceneNode button) =>
        level.Entities.QueueInput(
            level.Entities.Entities.OfType<FuncButton>().Single(entity => ReferenceEquals(entity.Node, button)),
            Core.Play.PlaySession.UseInput);

    private static FuncDoor Live(ScriptLevel level, SceneNode door) =>
        level.Entities.Entities.OfType<FuncDoor>().Single(entity => ReferenceEquals(entity.Node, door));
}
