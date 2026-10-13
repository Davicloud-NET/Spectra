using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <c>ent_watch</c> for what a script sends and hears: the script and its
/// line as the sender of an input, and the listeners a fired output reached.
/// </summary>
public sealed class EntityWatchScriptTests
{
    [Fact]
    public void An_input_a_script_sent_names_the_script_and_its_line_as_the_sender()
    {
        var rig = new EntityConsoleRig();
        SceneNode door = rig.Place("door1", "recorder");
        SceneNode score = rig.Place("score", "counter");
        rig.Run("ent_watch on");
        rig.Play();

        rig.World.QueueScriptInput(EntityRuntime.Live(rig.World, door), "Open", "", "Scripts/opener.luau", 12);
        rig.World.QueueScriptInput(EntityRuntime.Live(rig.World, score), "Add", "5", "Scripts/opener.luau", 13);

        rig.Tick().ShouldBe(
        [
            "ent 1 send Scripts/opener.luau:12 -> door1.Open",
            "ent 1 send Scripts/opener.luau:13 -> score.Add(\"5\")",
            "ent 1 fire score.OutValue wires=0",
        ]);
        rig.Received.ShouldBe(["door1:Open::-:-"]);
    }

    [Fact]
    public void A_script_in_a_folder_with_a_space_is_quoted_and_one_with_no_line_goes_by_its_file()
    {
        var rig = new EntityConsoleRig();
        SceneNode door = rig.Place("door1", "recorder");
        rig.Run("ent_watch on");
        rig.Play();
        Entity live = EntityRuntime.Live(rig.World, door);

        rig.World.QueueScriptInput(live, "Open", "", "My Scripts/opener.luau", 3);
        rig.World.QueueScriptInput(live, "Close", "", "Scripts/opener.luau", 0);

        rig.Tick().ShouldBe(
        [
            "ent 1 send \"My Scripts/opener.luau\":3 -> door1.Open",
            "ent 1 send Scripts/opener.luau -> door1.Close",
        ]);
    }

    [Fact]
    public void A_fired_output_says_how_many_listeners_of_scripts_it_was_queued_for()
    {
        var rig = new EntityConsoleRig();
        SceneNode relay = rig.Place("relay1", "relay");
        rig.Place("door1", "recorder");
        EntityRuntime.Wire(relay, "OnTrigger", "door1", "Open");
        rig.Run("ent_watch on");
        rig.Play();
        rig.World.Listeners.Add(relay, "OnTrigger", 1);
        rig.World.Listeners.Add(relay, "OnTrigger", 2);

        rig.World.QueueScriptInput(EntityRuntime.Live(rig.World, relay), "Trigger", "", "Scripts/press.luau", 7);

        rig.Tick().ShouldBe(
        [
            "ent 1 send Scripts/press.luau:7 -> relay1.Trigger",
            "ent 1 fire relay1.OnTrigger wires=1 listeners=2",
            "ent 1 send relay1.OnTrigger -> door1.Open",
        ]);
    }

    [Fact]
    public void A_pattern_passes_what_a_script_sent_by_the_entity_it_was_sent_to()
    {
        var rig = new EntityConsoleRig();
        SceneNode door = rig.Place("door1", "recorder");
        SceneNode gate = rig.Place("gate", "recorder");
        rig.Run("ent_watch door*");
        rig.Play();

        rig.World.QueueScriptInput(EntityRuntime.Live(rig.World, gate), "Open", "", "Scripts/opener.luau", 1);
        rig.World.QueueScriptInput(EntityRuntime.Live(rig.World, door), "Open", "", "Scripts/opener.luau", 2);

        rig.Tick().ShouldBe(["ent 1 send Scripts/opener.luau:2 -> door1.Open"]);
    }

    [Fact]
    public void With_the_watch_off_nothing_a_script_sends_is_printed_and_the_world_is_not_traced()
    {
        var rig = new EntityConsoleRig();
        SceneNode door = rig.Place("door1", "recorder");
        rig.Play();

        rig.World.QueueScriptInput(EntityRuntime.Live(rig.World, door), "Open", "", "Scripts/opener.luau", 2);

        rig.Tick().ShouldBeEmpty();
        rig.World.Trace.ShouldBeNull();
        rig.Received.ShouldBe(["door1:Open::-:-"]);
    }

    [Fact]
    public void A_trace_is_told_the_sender_of_an_input_and_the_listeners_of_an_output()
    {
        var rig = new EntityConsoleRig();
        SceneNode relay = rig.Place("relay1", "relay");
        var trace = new RecordingTrace();
        rig.Manager.EntityTrace = trace;
        rig.Play();
        rig.World.Listeners.Add(relay, "OnTrigger", 1);

        rig.World.QueueScriptInput(EntityRuntime.Live(rig.World, relay), "Trigger", "", "Scripts/press.luau", 7);
        rig.Tick();

        trace.Events.Select(traced => traced.Kind).ShouldBe(
            [EntityTraceKind.InputQueued, EntityTraceKind.InputDelivered, EntityTraceKind.OutputFired]);
        trace.Events[0].SenderScript.ShouldBe("Scripts/press.luau");
        trace.Events[0].SenderLine.ShouldBe(7);
        trace.Events[1].SenderScript.ShouldBe("Scripts/press.luau");
        trace.Events[1].Source.ShouldBeNull();
        trace.Events[2].SenderScript.ShouldBeEmpty();
        trace.Events[2].ListenersQueued.ShouldBe(1);
        trace.Events[2].WiresQueued.ShouldBe(0);
    }

    [Fact]
    public void A_fired_output_counts_the_listeners_the_queue_had_room_for()
    {
        var rig = new EntityConsoleRig();
        SceneNode relay = rig.Place("relay1", "relay");
        rig.Run("ent_watch on");
        rig.Play();
        rig.World.MaxPendingEvents = 2;
        for (int listener = 1; listener <= 5; listener++)
            rig.World.Listeners.Add(relay, "OnTrigger", listener);

        rig.World.QueueInput("relay1", "Trigger");

        rig.Tick().ShouldContain("ent 1 fire relay1.OnTrigger wires=0 listeners=2");
        rig.World.DiscardedEventCount.ShouldBe(3);
    }

    private sealed class RecordingTrace : IEntityTrace
    {
        public List<EntityTraceEvent> Events { get; } = [];

        public void Record(in EntityTraceEvent traced) => Events.Add(traced);

        public void EndTick(long tick, float time)
        {
        }
    }
}
