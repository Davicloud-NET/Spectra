using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Entities;
using static SpectraEngine.Scripting.Tests.DoorLevel;

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

    private static readonly SitePage Page = SitePage.At("guides/talk-to-the-level.mdx");

    private static string Code(string name) => ScriptSamples.At(Folder + name).Code;

    [Fact]
    public void The_page_shows_its_samples_in_the_order_a_person_writes_them()
    {
        Page.Blocks("luau").ShouldBe(
            [Code("third_press.luau"), Code("third_press_checked.luau"), ScriptSamples.At(Typo).Code]);
    }

    [Fact]
    public void One_wire_opens_the_door_on_the_first_press()
    {
        using ScriptLevel level = Build(out SceneNode button, out SceneNode door);
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
        using ScriptLevel level = Build(out SceneNode button, out SceneNode door);
        AddCounter(level, opensTheDoor: true);
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
        using ScriptLevel level = Build(out SceneNode button, out SceneNode door);
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

    // The page has the reader name the button, since the script looks for it by that name.
    [Fact]
    public void With_a_button_of_another_name_the_script_prints_nothing_and_warns_after_five_seconds()
    {
        using ScriptLevel level = Build(out SceneNode button, out SceneNode door);
        button.Name = "func_button1";
        level.ScriptAt(DoorScript, Code("third_press.luau"), under: door);

        level.Play().Tick();
        Press(level, button);
        level.Tick(4 * 60);
        level.Sink.Texts(ScriptSeverity.Warning).ShouldBeEmpty();

        level.Tick(2 * 60);

        level.Printed.ShouldBeEmpty();
        level.Errors.ShouldBeEmpty();
        level.Sink.Texts(ScriptSeverity.Warning).ShouldHaveSingleItem().ShouldStartWith("Infinite yield possible on ");
        Page.Text.ShouldContain("The editor called it `func_button1`");
        Page.Text.ShouldContain("warns in Output after five seconds and waits on");
    }

    [Fact]
    public void The_checked_script_does_what_the_plain_one_does()
    {
        using ScriptLevel level = Build(out SceneNode button, out SceneNode door);
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
        using ScriptLevel level = Build(out SceneNode button, out SceneNode door);
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

    // The words the page sends a reader to the Properties panel for, and
    // the ones it says a card shows in their place.
    [Fact]
    public void The_classes_the_checked_script_names_are_the_schemas_class_names_and_not_their_display_names()
    {
        FuncDoor.SpectraSchema.ClassName.ShouldBe("func_door");
        FuncButton.SpectraSchema.ClassName.ShouldBe("func_button");
        Code("third_press_checked.luau").ShouldContain(":: func_door");
        Code("third_press_checked.luau").ShouldContain(":: func_button");

        FuncDoor.SpectraSchema.DisplayName.ShouldBe("Door");
        Page.Text.ShouldContain("the Class row under Entity in the Properties panel");
        Page.Text.ShouldContain("The card in the Logic view shows the class's plain name, Door");
    }

    // By then the reader's file is the checked script, with the slip put right.
    [Fact]
    public void The_watch_prints_the_lines_the_page_shows_for_the_third_press()
    {
        using ScriptLevel level = Build(out SceneNode button, out SceneNode door);
        level.ScriptAt(DoorScript, Code("third_press_checked.luau"), under: door);
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
            $"ent {tick} send Scripts/Door.luau:11 -> Door.Open",
            $"ent {tick} fire Door.OnOpen wires=0",
        ]);

        // The page shows them at a tick of its own.
        foreach (string line in lines)
            Page.Text.ShouldContain(line.Replace($"ent {tick} ", "ent 412 ", StringComparison.Ordinal));

        Page.Text.ShouldContain("at line 11 of `Door.luau`");
    }
}
