using System.Globalization;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <c>ent_watch</c>: what it prints for a running level, what it leaves out,
/// and when the world is traced at all.
/// </summary>
public sealed class EntityWatchTests
{
    [Fact]
    public void A_watched_cascade_prints_one_line_per_event_in_causal_order()
    {
        var rig = new EntityConsoleRig();
        SceneNode auto = rig.Place("auto", "spawner");
        SceneNode relay = rig.Place("relay1", "relay");
        rig.Place("score", "counter");
        rig.Place("door1", "recorder");
        EntityRuntime.Wire(auto, SpawnFiringEntity.OnSpawned, "relay1", "Trigger");
        EntityRuntime.Wire(relay, "OnTrigger", "door1", "Open", delay: 2f);
        EntityRuntime.Wire(relay, "OnTrigger", "score", "Add", "5");
        EntityRuntime.Wire(relay, "OnTrigger", "dor1", "Close");

        rig.Run("ent_watch on").ShouldBe(["ent_watch: on, every entity. Nothing prints until the level runs."]);
        rig.Play();

        // Set before play, so the output fired while spawning is on record
        // ahead of the delivery it causes.
        rig.Printed().ShouldBe(["ent 0 fire auto.OnSpawned wires=1"]);
        rig.Tick().ShouldBe(
        [
            "ent 1 send auto.OnSpawned -> relay1.Trigger",
            "ent 1 fire relay1.OnTrigger wires=3 by=auto",
            "ent 1 wait relay1.OnTrigger -> door1.Open in=2s",
            "ent 1 send relay1.OnTrigger -> score.Add(\"5\") by=auto",
            "ent 1 fire score.OutValue wires=0 by=auto",
            "ent 1 miss relay1.OnTrigger -> dor1.Close  nothing is named dor1",
        ]);

        string[] later = rig.TickUntilPrinted(limit: 130);
        rig.World.TickNumber.ShouldBeInRange(121, 122);
        later.ShouldBe([$"ent {rig.World.TickNumber} send relay1.OnTrigger -> door1.Open by=auto"]);
    }

    [Fact]
    public void A_pattern_passes_an_event_by_source_or_by_target()
    {
        var rig = new EntityConsoleRig();
        SceneNode relay1 = rig.Place("relay1", "relay");
        SceneNode relay2 = rig.Place("relay2", "relay");
        rig.Place("door1", "recorder");
        rig.Place("score", "counter");
        EntityRuntime.Wire(relay1, "OnTrigger", "door1", "Open");
        EntityRuntime.Wire(relay1, "OnTrigger", "score", "Add", "1");
        EntityRuntime.Wire(relay2, "OnTrigger", "score", "Add", "2");
        rig.Play();

        rig.Run("ent_watch door*").ShouldBe(["ent_watch: on, names matching door*"]);
        TriggerBoth(rig).ShouldBe(["ent 1 send relay1.OnTrigger -> door1.Open"]);

        rig.Run("ent_watch relay2");
        TriggerBoth(rig).ShouldBe(
        [
            "ent 2 send game -> relay2.Trigger",
            "ent 2 fire relay2.OnTrigger wires=1",
            "ent 2 send relay2.OnTrigger -> score.Add(\"2\")",
        ]);

        // A class is a pattern too.
        rig.Run("ent_watch counter door1").ShouldBe(["ent_watch: on, names matching counter or door1"]);
        TriggerBoth(rig).ShouldBe(
        [
            "ent 3 send relay1.OnTrigger -> door1.Open",
            "ent 3 send relay1.OnTrigger -> score.Add(\"1\")",
            "ent 3 fire score.OutValue wires=0 by=relay1",
            "ent 3 send relay2.OnTrigger -> score.Add(\"2\")",
            "ent 3 fire score.OutValue wires=0 by=relay2",
        ]);
    }

    [Fact]
    public void A_wire_to_a_misspelled_name_shows_under_a_pattern_for_that_name()
    {
        var rig = new EntityConsoleRig();
        SceneNode relay = rig.Place("relay1", "relay");
        rig.Place("door1", "recorder");
        EntityRuntime.Wire(relay, "OnTrigger", "door1", "Open");
        EntityRuntime.Wire(relay, "OnTrigger", "dor1", "Close", delay: 0.5f);
        rig.Play();
        rig.Run("ent_watch dor*");

        rig.World.QueueInput(EntityRuntime.Live(rig.World, relay), "Trigger");

        rig.Tick().ShouldBe(["ent 1 wait relay1.OnTrigger -> dor1.Close in=0.5s"]);
        ConsoleLine missed = TickUntilLine(rig, limit: 40);
        missed.Severity.ShouldBe(LogLevel.Warning);
        missed.Text.ShouldBe($"ent {rig.World.TickNumber} miss relay1.OnTrigger -> dor1.Close  nothing is named dor1");
    }

    [Fact]
    public void A_wait_is_written_with_a_dot_on_any_culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            // German writes one half as 0,5.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var rig = new EntityConsoleRig();
            SceneNode relay = rig.Place("relay1", "relay");
            rig.Place("door1", "recorder");
            EntityRuntime.Wire(relay, "OnTrigger", "door1", "Open", delay: 0.5f);
            rig.Play();
            rig.Run("ent_watch door1");

            rig.World.QueueInput(EntityRuntime.Live(rig.World, relay), "Trigger");

            rig.Tick().ShouldBe(["ent 1 wait relay1.OnTrigger -> door1.Open in=0.5s"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_hand_fired_input_is_reported_with_watch_off()
    {
        var rig = new EntityConsoleRig();
        SceneNode relay = rig.Place("relay1", "relay");
        rig.Place("door1", "recorder");
        EntityRuntime.Wire(relay, "OnTrigger", "door1", "Open");
        rig.Play();

        rig.Run("ent_fire relay1 Trigger");

        // What was fired, and nothing of the cascade it started.
        rig.Tick().ShouldBe(["ent 1 send console -> relay1.Trigger"]);
        rig.Received.ShouldBe(["door1:Open::relay1:relay1"]);
    }

    [Fact]
    public void A_hand_fired_input_whose_target_is_gone_when_it_comes_due_is_reported_as_missed()
    {
        var rig = new EntityConsoleRig();
        SceneNode door = rig.Place("door1", "recorder");
        rig.Play();
        rig.Run("ent_fire door1 Open \"\" 0.1");

        door.Name = "gate1";

        rig.TickUntilPrinted(limit: 10).ShouldBe(
            [$"ent {rig.World.TickNumber} miss console -> door1.Open  nothing is named door1"]);
    }

    [Fact]
    public void With_watch_off_and_nothing_pending_the_world_has_no_trace()
    {
        var rig = new EntityConsoleRig();
        rig.Place("door1", "recorder");
        rig.Play();
        rig.World.Trace.ShouldBeNull();

        rig.Run("ent_fire door1 Open \"\" 0.1");
        rig.World.Trace.ShouldBeSameAs(rig.Watch);

        rig.Tick(3);
        rig.World.Trace.ShouldBeSameAs(rig.Watch);

        rig.TickUntilPrinted(limit: 10);
        rig.World.Trace.ShouldBeNull();
        rig.Watch.ActiveTrace.ShouldBeNull();

        rig.Run("ent_watch on");
        rig.World.Trace.ShouldBeSameAs(rig.Watch);

        rig.Run("ent_watch off");
        rig.World.Trace.ShouldBeNull();
    }

    [Fact]
    public void An_input_fired_by_hand_at_a_level_that_stopped_does_not_hold_the_next_one()
    {
        var rig = new EntityConsoleRig();
        rig.Place("door1", "recorder");
        rig.Play();
        rig.Run("ent_fire door1 Open \"\" 30");
        rig.Stop();

        rig.Play();

        rig.Watch.ActiveTrace.ShouldBeNull();
        rig.Tick().ShouldBeEmpty();
        rig.World.Trace.ShouldBeNull();
    }

    [Fact]
    public void The_watch_stays_on_from_one_play_session_to_the_next()
    {
        var rig = new EntityConsoleRig();
        rig.Place("auto", "spawner");
        rig.Play();
        rig.Run("ent_watch auto").ShouldBe(["ent_watch: on, names matching auto"]);
        rig.Stop();

        rig.Play();

        rig.Printed().ShouldBe(["ent 0 fire auto.OnSpawned wires=0"]);
    }

    [Fact]
    public void A_runaway_tick_prints_the_limit_and_one_summary()
    {
        const int Budget = 100;
        var rig = new EntityConsoleRig();
        SceneNode ping = rig.Place("ping", "relay");
        SceneNode pong = rig.Place("pong", "relay");
        EntityRuntime.Wire(ping, "OnTrigger", "pong", "Trigger");
        EntityRuntime.Wire(pong, "OnTrigger", "ping", "Trigger");
        rig.Play();
        rig.World.MaxDispatchesPerTick = Budget;
        rig.Run("ent_watch on");
        rig.Run("ent_fire ping Trigger");

        IReadOnlyList<ConsoleLine> printed = TickLines(rig);

        // Every dispatch is a send and the output it fires.
        int events = Budget * 2;
        printed.Count.ShouldBe(EntityWatch.MaxLinesPerTick + 1);
        printed[0].Text.ShouldBe("ent 1 send console -> ping.Trigger");
        printed[^1].Severity.ShouldBe(LogLevel.Warning);
        printed[^1].Text.ShouldBe(
            $"ent 1 drop {events - EntityWatch.MaxLinesPerTick} more events this tick (limit 64 a tick)");

        // The count starts again with the next tick.
        rig.World.QueueInput("ping", "Trigger");
        rig.Tick().Length.ShouldBe(EntityWatch.MaxLinesPerTick + 1);
    }

    [Fact]
    public void Each_form_of_the_command_sets_the_whole_state()
    {
        var rig = new EntityConsoleRig();

        rig.Run("ent_watch").ShouldBe(["ent_watch: on, every entity. Nothing prints until the level runs."]);
        rig.Watch.IsOn.ShouldBeTrue();
        rig.Watch.Patterns.ShouldBeEmpty();

        rig.Run("ent_watch door* relay1 score").ShouldBe(
        [
            "ent_watch: on, names matching door*, relay1 or score. Nothing prints until the level runs.",
        ]);
        rig.Watch.Patterns.ShouldBe(["door*", "relay1", "score"]);

        rig.Run("ent_watch lamp");
        rig.Watch.Patterns.ShouldBe(["lamp"]);

        // Said twice, on stays on and off stays off.
        rig.Run("ent_watch on");
        rig.Run("ent_watch ON").ShouldBe(["ent_watch: on, every entity. Nothing prints until the level runs."]);
        rig.Watch.IsOn.ShouldBeTrue();
        rig.Watch.Patterns.ShouldBeEmpty();

        rig.Run("ent_watch off").ShouldBe(["ent_watch: off"]);
        rig.Run("ent_watch off").ShouldBe(["ent_watch: off"]);
        rig.Watch.IsOn.ShouldBeFalse();
    }

    [Fact]
    public void A_pattern_that_can_match_nothing_is_refused_and_the_watch_is_left_alone()
    {
        var rig = new EntityConsoleRig();
        rig.Run("ent_watch door*");

        ConsoleLine reply = rig.RunLines("ent_watch lamp !activator").ShouldHaveSingleItem();

        reply.Severity.ShouldBe(LogLevel.Error);
        reply.Text.ShouldBe(
            "ent_watch: '!activator' never matches an entity. Give a name, a class or a prefix ending in *.");
        rig.Watch.Patterns.ShouldBe(["door*"]);
    }

    [Fact]
    public void Watching_while_playing_a_level_that_was_replaced_says_to_enter_play_again()
    {
        var rig = new EntityConsoleRig();

        rig.RunLines("ent_watch on", isPlaying: true).ShouldHaveSingleItem().Text.ShouldBe(
            "ent_watch: on, every entity. Nothing prints until the level runs: " +
            "play mode is on but the level was replaced. Leave play mode and enter it again.");
    }

    [Fact]
    public void A_name_with_a_space_is_quoted()
    {
        var rig = new EntityConsoleRig();
        SceneNode relay = rig.Place("Hall Relay", "relay");
        rig.Place("Main Door", "recorder");
        EntityRuntime.Wire(relay, "OnTrigger", "Main Door", "Open", "half way");
        rig.Play();
        rig.Run("ent_watch on");

        rig.Run("ent_fire \"Hall Relay\" Trigger").ShouldBe(
            ["ent_fire: Trigger queued for 1 entity named \"Hall Relay\""]);

        rig.Tick().ShouldBe(
        [
            "ent 1 send console -> \"Hall Relay\".Trigger",
            "ent 1 fire \"Hall Relay\".OnTrigger wires=1",
            "ent 1 send \"Hall Relay\".OnTrigger -> \"Main Door\".Open(\"half way\")",
        ]);
        rig.Run("ent_list Main*").ShouldBe(
            ["ent_list: 1 of 2 running match Main*", "\"Main Door\"  recorder"]);
    }

    // Sends Trigger to both relays the way the game would, with no console in it.
    private static string[] TriggerBoth(EntityConsoleRig rig)
    {
        foreach (Entity entity in rig.World.Entities)
        {
            if (entity is RelayEntity)
                rig.World.QueueInput(entity, "Trigger");
        }

        return rig.Tick();
    }

    private static IReadOnlyList<ConsoleLine> TickLines(EntityConsoleRig rig)
    {
        rig.World.Tick(1f / 60f);
        return rig.Console.Output.Drain();
    }

    private static ConsoleLine TickUntilLine(EntityConsoleRig rig, int limit)
    {
        for (int i = 0; i < limit; i++)
        {
            IReadOnlyList<ConsoleLine> printed = TickLines(rig);
            if (printed.Count > 0)
                return printed.ShouldHaveSingleItem();
        }

        throw new InvalidOperationException($"Nothing was printed in {limit} ticks.");
    }
}
