using System.Globalization;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <c>ent_fire</c>, <c>ent_list</c> and <c>ent_show</c> typed into a real
/// console, over a level that runs and one that does not.
/// </summary>
public sealed class EntityConsoleCommandTests
{
    [Fact]
    public void Firing_by_hand_delivers_on_the_next_tick_and_says_so()
    {
        var rig = new EntityConsoleRig();
        rig.Place("score", "counter");
        rig.Play();

        rig.Run("ent_fire score Add 5").ShouldBe(["ent_fire: Add(\"5\") queued for 1 entity named score"]);

        rig.Tick().ShouldBe(["ent 1 send console -> score.Add(\"5\")"]);
    }

    [Fact]
    public void Firing_at_a_prefix_reaches_every_match()
    {
        var rig = new EntityConsoleRig();
        rig.Place("door_north", "recorder");
        rig.Place("door_south", "recorder");
        rig.Place("lamp", "recorder");
        rig.Play();

        rig.Run("ent_fire door* Open").ShouldBe(["ent_fire: Open queued for 2 entities matching door*"]);

        rig.Tick().ShouldBe(
        [
            "ent 1 send console -> door_north.Open",
            "ent 1 send console -> door_south.Open",
        ]);
        rig.Received.ShouldBe(["door_north:Open::-:-", "door_south:Open::-:-"]);
    }

    [Fact]
    public void Firing_with_no_world_running_says_how_to_start_one()
    {
        var rig = new EntityConsoleRig();
        rig.Place("door", "recorder");

        ConsoleLine reply = rig.RunLines("ent_fire door Open").ShouldHaveSingleItem();

        reply.Severity.ShouldBe(LogLevel.Error);
        reply.Text.ShouldBe(
            "ent_fire: the level is not running. Press F8, or type play on in the editor, then fire again.");
    }

    [Fact]
    public void Firing_while_playing_a_level_that_was_replaced_says_to_enter_play_again()
    {
        // Pressing play is the wrong advice here: play is already on.
        var rig = new EntityConsoleRig();
        rig.Place("door", "recorder");

        ConsoleLine reply = rig.RunLines("ent_fire door Open", isPlaying: true).ShouldHaveSingleItem();

        reply.Severity.ShouldBe(LogLevel.Error);
        reply.Text.ShouldBe(
            "ent_fire: play mode is on but the level was replaced. Leave play mode and enter it again.");
    }

    [Fact]
    public void Firing_at_a_name_nothing_has_is_refused()
    {
        var rig = new EntityConsoleRig();
        rig.Place("door1", "recorder");
        rig.Play();

        rig.Run("ent_fire dor1 Open").ShouldBe(
            ["ent_fire: nothing is named 'dor1'. Names are case sensitive. ent_list shows them."]);
        rig.Run("ent_fire Door* Open").ShouldBe(
            ["ent_fire: nothing matches 'Door*'. Names are case sensitive. ent_list shows them."]);

        rig.World.PendingEventCount.ShouldBe(0);
        rig.World.Trace.ShouldBeNull();
    }

    [Fact]
    public void Firing_at_a_class_is_refused_and_says_a_target_is_a_name()
    {
        // The console matches what a wire with that target would match.
        var rig = new EntityConsoleRig();
        rig.Place("door1", "recorder");
        rig.Play();

        rig.Run("ent_fire recorder Open").ShouldBe(
        [
            "ent_fire: nothing is named 'recorder'. It is a class, and a target is a name. " +
            "ent_list recorder shows the names to fire at.",
        ]);
        rig.Received.ShouldBeEmpty();
    }

    [Fact]
    public void Firing_an_input_in_the_wrong_case_is_refused_and_suggests_the_right_one()
    {
        var rig = new EntityConsoleRig();
        rig.Place("relay1", "relay");
        rig.Place("score", "counter");
        rig.Play();

        rig.Run("ent_fire relay1 trigger").ShouldBe(
            ["ent_fire: relay has no input 'trigger'. Did you mean Trigger? Inputs: Trigger"]);
        rig.Run("ent_fire score Open").ShouldBe(
            ["ent_fire: counter has no input 'Open'. Inputs: Add SetValue"]);

        rig.World.PendingEventCount.ShouldBe(0);
    }

    [Fact]
    public void An_input_no_class_under_a_prefix_lists_is_refused_for_all_of_them()
    {
        var rig = new EntityConsoleRig();
        rig.Place("gate_relay", "relay");
        rig.Place("gate_score", "counter");
        rig.Play();

        rig.Run("ent_fire gate* add").ShouldBe(
        [
            "ent_fire: gate* reaches 2 classes and none has an input 'add'. Did you mean Add? " +
            "ent_show gate* lists their inputs.",
        ]);
    }

    [Fact]
    public void An_input_a_class_might_take_is_queued_and_the_delivery_tells_the_truth()
    {
        // The spawner's schema lists no inputs, so nothing can be refused up front.
        var rig = new EntityConsoleRig();
        rig.Place("auto", "spawner");
        rig.Play();

        rig.Run("ent_fire auto Open").ShouldBe(["ent_fire: Open queued for 1 entity named auto"]);

        IReadOnlyList<ConsoleLine> told = TickLines(rig);
        told.Select(line => line.Text).ShouldBe(
        [
            "ent 1 send console -> auto.Open",
            "ent 1 deny auto.Open  spawner has no input Open",
        ]);
        told[1].Severity.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void A_runtime_token_is_refused_on_the_console()
    {
        var rig = new EntityConsoleRig();
        rig.Place("door", "recorder");
        rig.Play();

        rig.Run("ent_fire !self Kill").ShouldBe(
        [
            "ent_fire: !self is the entity whose output is firing. There is none on the console. Name an entity.",
        ]);
        rig.Run("ent_fire !activator Kill").ShouldHaveSingleItem().ShouldStartWith("ent_fire: !activator is ");
        rig.Run("ent_fire !caller Kill").ShouldHaveSingleItem().ShouldStartWith("ent_fire: !caller is ");
        rig.Run("ent_fire !picker Kill").ShouldHaveSingleItem().ShouldStartWith("ent_fire: '!picker' is not a name.");

        rig.World.PendingEventCount.ShouldBe(0);
    }

    [Fact]
    public void An_empty_quoted_parameter_lets_a_delay_follow()
    {
        var rig = new EntityConsoleRig();
        rig.Place("door1", "recorder");
        rig.Place("door2", "recorder");
        rig.Place("door3", "recorder");
        rig.Play();

        rig.Run("ent_fire door* Open \"\" 2").ShouldBe(
            ["ent_fire: Open queued for 3 entities matching door*, in 2s"]);

        rig.Tick(60).ShouldBeEmpty();
        rig.Received.ShouldBeEmpty();

        string[] arrived = rig.TickUntilPrinted(limit: 70);
        long tick = rig.World.TickNumber;
        tick.ShouldBeInRange(120, 121);
        arrived.ShouldBe(
        [
            $"ent {tick} send console -> door1.Open",
            $"ent {tick} send console -> door2.Open",
            $"ent {tick} send console -> door3.Open",
        ]);
        rig.Received[0].ShouldBe("door1:Open::-:-");
    }

    [Fact]
    public void A_delay_is_read_with_a_dot_on_any_culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            // German writes one half as 0,5.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var rig = new EntityConsoleRig();
            rig.Place("door", "recorder");
            rig.Play();

            rig.Run("ent_fire door Open \"\" 0.5").ShouldBe(
                ["ent_fire: Open queued for 1 entity named door, in 0.5s"]);
            rig.Run("ent_fire door Open \"\" 0,5").ShouldBe(
            [
                "ent_fire: '0,5' is not a delay. Give seconds, zero or more, " +
                "with a dot for the decimal point, like 0.5.",
            ]);

            rig.World.PendingEventCount.ShouldBe(1);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_line_that_is_not_a_target_and_an_input_says_what_to_type()
    {
        var rig = new EntityConsoleRig();
        rig.Play();

        rig.Run("ent_fire door").ShouldHaveSingleItem().ShouldStartWith("ent_fire: give a target and an input");
        rig.Run("ent_fire Main Door Open now please").ShouldBe(
            ["ent_fire: too many arguments. Put a name or a parameter with spaces in quotes."]);
        rig.Run("ent_fire door Open \"\" -1").ShouldHaveSingleItem().ShouldStartWith("ent_fire: '-1' is not a delay.");
    }

    [Fact]
    public void Listing_works_before_play_and_marks_the_entities_as_not_running()
    {
        var rig = new EntityConsoleRig();
        rig.Place("relay1", "relay");
        rig.Place("score", "counter");
        rig.Place("spawner", "crate_spawner");

        rig.Run("ent_list").ShouldBe(
        [
            "ent_list: 3 placed, not running",
            "relay1  relay",
            "score  counter",
            "spawner  crate_spawner  (class not in this build)",
        ]);
    }

    [Fact]
    public void Listing_a_running_level_gives_the_tick_and_what_is_waiting()
    {
        var rig = new EntityConsoleRig();
        rig.Place("relay1", "relay");
        rig.Place("spawner", "crate_spawner");
        rig.Play();
        rig.Tick(2);
        rig.Run("ent_fire relay1 Trigger \"\" 5");

        rig.Run("ent_list").ShouldBe(
        [
            "ent_list: 2 running, tick 2 (0.03 s), 1 event waiting",
            "relay1  relay",
            "spawner  crate_spawner  (class not in this build)",
        ]);
    }

    [Fact]
    public void Listing_matches_a_pattern_against_name_or_class()
    {
        var rig = new EntityConsoleRig();
        rig.Place("relay1", "relay");
        rig.Place("door1", "relay");
        rig.Place("rel_score", "counter");
        rig.Place("lamp", "recorder");

        rig.Run("ent_list rel*").ShouldBe(
        [
            "ent_list: 3 of 4 placed match rel*, not running",
            "relay1  relay",
            "door1  relay",
            "rel_score  counter",
        ]);

        rig.Play();

        rig.Run("ent_list counter").ShouldBe(["ent_list: 1 of 4 running match counter", "rel_score  counter"]);
        rig.Run("ent_list Lamp").ShouldBe(["ent_list: 0 of 4 running match Lamp"]);
    }

    [Fact]
    public void A_list_longer_than_the_cap_says_how_many_it_left_out()
    {
        const int Extra = 5;
        var rig = new EntityConsoleRig();
        for (int i = 0; i < EntityListing.MaxListed + Extra; i++)
            rig.Place($"crate{i}", "recorder");

        string[] rows = rig.Run("ent_list");

        rows.Length.ShouldBe(EntityListing.MaxListed + 2);
        rows[0].ShouldBe("ent_list: 205 placed, not running");
        rows[^2].ShouldBe("crate199  recorder");
        rows[^1].ShouldBe("ent_list: 5 more not shown. Give a pattern to narrow the list.");
    }

    [Fact]
    public void Showing_an_entity_prints_its_state_and_the_fires_left_on_each_wire()
    {
        var rig = new EntityConsoleRig();
        SceneNode score = Scoreboard(rig);
        rig.Play();
        rig.Run("ent_fire score Add 10");
        rig.Tick();

        rig.Run("ent_show score").ShouldBe(
        [
            $"score: counter, node {score.Id:D}",
            "score keyvalue startvalue = 0 (default)",
            "score keyvalue max = 10",
            "score state value = 10",
            "score state refused inputs = 0",
            "score think: none",
            "score output OutValue -> display.SetText fires=unlimited, 1 entity named display",
            "score output OnHitMax -> door*.Open in=2s fires=2 of 3 left, 2 entities matching door*",
            "score output OnHitMax -> dor1.Close(\"now\") fires=unlimited, nothing is named dor1",
            "score output OnHitMin: not wired",
            "score inputs: Add SetValue",
        ]);
    }

    [Fact]
    public void Showing_an_entity_before_play_prints_wires_and_no_state()
    {
        var rig = new EntityConsoleRig();
        SceneNode score = Scoreboard(rig);

        rig.Run("ent_show score").ShouldBe(
        [
            $"score: counter, node {score.Id:D}",
            "score keyvalue startvalue = 0 (default)",
            "score keyvalue max = 10",
            "score not running: no live state",
            "score output OutValue -> display.SetText fires=unlimited, 1 entity named display",
            "score output OnHitMax -> door*.Open in=2s fires=3, 2 entities matching door*",
            "score output OnHitMax -> dor1.Close(\"now\") fires=unlimited, nothing is named dor1",
            "score output OnHitMin: not wired",
            "score inputs: Add SetValue",
        ]);
    }

    [Fact]
    public void Showing_says_what_the_class_does_not_declare()
    {
        var rig = new EntityConsoleRig();
        SceneNode relay = rig.Place("relay1", "relay");
        relay.Entity!.SetValue("speed", "");
        EntityRuntime.Wire(relay, "OnTriger", TargetNameIndex.ActivatorToken, "Ping");
        SceneNode crate = rig.Place("spawner", "crate_spawner");
        crate.Entity!.SetValue("count", "3");
        EntityRuntime.Wire(crate, "OnSpawn", TargetNameIndex.SelfToken, "Kill");
        rig.Play();

        rig.Run("ent_show relay1").ShouldBe(
        [
            $"relay1: relay, node {relay.Id:D}",
            "relay1 keyvalue speed = \"\" (not a relay keyvalue)",
            "relay1 think: none",
            "relay1 output OnTrigger: not wired",
            "relay1 output OnTriger -> !activator.Ping fires=unlimited, decided when it fires, " +
            "relay has no output OnTriger",
            "relay1 inputs: Trigger",
        ]);
        rig.Run("ent_show spawner").ShouldBe(
        [
            $"spawner: crate_spawner (class not in this build), node {crate.Id:D}",
            "spawner keyvalue count = 3",
            "spawner think: none",
            "spawner output OnSpawn -> !self.Kill fires=unlimited, this entity",
            "spawner inputs: not known",
        ]);
    }

    [Fact]
    public void Showing_matches_a_class_and_stops_at_its_cap()
    {
        const int Extra = 2;
        var rig = new EntityConsoleRig();
        for (int i = 0; i < EntityListing.MaxShown + Extra; i++)
            rig.Place($"relay{i}", "relay");

        string[] rows = rig.Run("ent_show relay");

        rows.Count(row => row.Contains(": relay, node ")).ShouldBe(EntityListing.MaxShown);
        rows[^1].ShouldBe("ent_show: 2 more entities match relay. Narrow the pattern to see them.");
    }

    [Fact]
    public void Showing_a_name_nothing_has_says_so()
    {
        var rig = new EntityConsoleRig();
        rig.Place("door1", "recorder");

        ConsoleLine reply = rig.RunLines("ent_show dor1").ShouldHaveSingleItem();

        reply.Severity.ShouldBe(LogLevel.Error);
        reply.Text.ShouldBe(
            "ent_show: nothing has the name or the class 'dor1'. Names are case sensitive. ent_list shows them.");
        rig.Run("ent_show").ShouldHaveSingleItem().ShouldStartWith("ent_show: give one name, class or prefix");
    }

    [Fact]
    public void Help_says_how_to_use_each_command()
    {
        var rig = new EntityConsoleRig();

        rig.Run("help ent_fire")[0].ShouldBe("ent_fire <target> <input> [parameter] [delay]");
        rig.Run("help ent_list")[0].ShouldBe("ent_list [pattern]");
        rig.Run("help ent_show")[0].ShouldBe("ent_show <pattern>");
        rig.Run("help ent_watch")[0].ShouldBe("ent_watch [on | off | <pattern> ...]");
    }

    // A counter with a ceiling of ten, wired to a display, to two doors by
    // prefix on a wire that fires three times, and to a name nothing has.
    private static SceneNode Scoreboard(EntityConsoleRig rig)
    {
        SceneNode score = rig.Place("score", "counter");
        score.Entity!.SetValue("max", "10");
        rig.Place("display", "recorder");
        rig.Place("door1", "recorder");
        rig.Place("door2", "recorder");

        EntityRuntime.Wire(score, CounterEntity.OutValue, "display", "SetText");
        EntityRuntime.Wire(score, CounterEntity.OnHitMax, "door*", "Open", delay: 2f, timesToFire: 3);
        EntityRuntime.Wire(score, CounterEntity.OnHitMax, "dor1", "Close", "now");
        return score;
    }

    private static IReadOnlyList<ConsoleLine> TickLines(EntityConsoleRig rig)
    {
        rig.World.Tick(1f / 60f);
        return rig.Console.Output.Drain();
    }
}
