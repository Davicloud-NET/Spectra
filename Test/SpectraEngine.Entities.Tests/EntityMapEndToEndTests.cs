using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// End to end: a bundle loads, its wiring runs, and saving it afterwards
/// writes the same bytes back.
/// </summary>
public sealed class EntityMapEndToEndTests
{
    private const float Tick = 1f / 60f;

    private const int TwoSeconds = 120;

    // A timer drives a relay into a counter, which disables the relay at its
    // ceiling. The ceiling and the "times":1 wire are the two values a runtime
    // would be tempted to write back.
    // No editor member, scene.spawn or unknown members: FromScene cannot
    // rebuild those, and that limit is not what this file tests.
    // 'spectramap' is the engine's current version: a save stamps it.
    private const string RelayFixture = """
        {
          "spectramap": 4,
          "minimumReadableVersion": 3,
          "engine": "1.0.0",
          "scene": {
            "name": "RelayFixture"
          },
          "nodes": [
            {
              "id": "7c0f4a21-9d38-4b52-8e61-0a5c2d7f3b90",
              "name": "Floor",
              "transform": {"p":[0,-1,0]},
              "brush": {
                "planes": [
                  [1,0,0,-8],
                  [-1,0,0,-8],
                  [0,1,0,-1],
                  [0,-1,0,-1],
                  [0,0,1,-8],
                  [0,0,-1,-8]
                ],
                "faces": [
                  {},
                  {},
                  {},
                  {},
                  {},
                  {}
                ]
              },
              "children": []
            },
            {
              "id": "1b8e6c04-52a7-4f13-9c80-6d3f1e7a20b5",
              "name": "Metronome",
              "transform": {"p":[0,0,0]},
              "entity": {
                "class": "logic_timer",
                "keys": {"refiretime":"0.25"},
                "outputs": [
                  {"output":"OnTimer","target":"Gate","input":"Trigger"}
                ]
              },
              "children": []
            },
            {
              "id": "d4a91f37-6b20-4e85-a1cd-38027f9b4e16",
              "name": "Gate",
              "transform": {"p":[0,0,0]},
              "entity": {
                "class": "logic_relay",
                "keys": {"startdisabled":"0"},
                "outputs": [
                  {"output":"OnTrigger","target":"Tally","input":"Add","param":"2"}
                ]
              },
              "children": []
            },
            {
              "id": "0e5d2a68-4c71-4930-bf24-91a6c80d5f73",
              "name": "Tally",
              "transform": {"p":[0,0,0]},
              "entity": {
                "class": "math_counter",
                "keys": {"startvalue":"0","min":"0","max":"6"},
                "outputs": [
                  {"output":"OnHitMax","target":"Gate","input":"Disable","delay":0.1,"times":1}
                ]
              },
              "children": []
            }
          ]
        }
        """;

    // A logic_auto adds 2 to a counter. The count picks a case, the case feeds a
    // compare, and the compare sets a branch that adds 10 more. The second
    // count matches no case, which is what stops it.
    private const string BranchingFixture = """
        {
          "spectramap": 4,
          "minimumReadableVersion": 3,
          "engine": "1.0.0",
          "scene": {
            "name": "BranchingFixture"
          },
          "nodes": [
            {
              "id": "3f2b7c10-8a4d-4e6f-9b21-5c0d7e8f1a32",
              "name": "Start",
              "transform": {"p":[0,0,0]},
              "entity": {
                "class": "logic_auto",
                "outputs": [
                  {"output":"OnMapSpawn","target":"Tally","input":"Add","param":"2"}
                ]
              },
              "children": []
            },
            {
              "id": "a81d4e56-0c3b-47f9-8e12-6b9f2d0c7a45",
              "name": "Tally",
              "transform": {"p":[0,0,0]},
              "entity": {
                "class": "math_counter",
                "keys": {"startvalue":"0"},
                "outputs": [
                  {"output":"OutValue","target":"Sorter","input":"InValue"}
                ]
              },
              "children": []
            },
            {
              "id": "5c9e0b73-1d2a-4f84-b6c5-e3a7104f9d68",
              "name": "Sorter",
              "transform": {"p":[0,0,0]},
              "entity": {
                "class": "logic_case",
                "keys": {"case01":"1","case02":"2.0"},
                "outputs": [
                  {"output":"OnCase02","target":"Gauge","input":"SetValueCompare","param":"5"}
                ]
              },
              "children": []
            },
            {
              "id": "e2674f09-b5a1-4c3d-9f70-28d1c6b3e5a7",
              "name": "Gauge",
              "transform": {"p":[0,0,0]},
              "entity": {
                "class": "logic_compare",
                "keys": {"initialvalue":"0","comparevalue":"4"},
                "outputs": [
                  {"output":"OnGreaterThan","target":"Flag","input":"SetValueTest","param":"1"}
                ]
              },
              "children": []
            },
            {
              "id": "9b30d1c8-7e45-4a62-8f0b-c4e2a9d7f316",
              "name": "Flag",
              "transform": {"p":[0,0,0]},
              "entity": {
                "class": "logic_branch",
                "keys": {"initialvalue":"0"},
                "outputs": [
                  {"output":"OnTrue","target":"Tally","input":"Add","param":"10"}
                ]
              },
              "children": []
            }
          ]
        }
        """;

    [Fact]
    public void The_fixture_map_survives_a_read_and_a_write_byte_for_byte()
    {
        byte[] source = Utf8(RelayFixture);

        Same(source, MapWriter.Write(MapReader.Read(source)));
    }

    [Fact]
    public void The_fixture_map_binds_to_a_scene_and_projects_back_to_the_same_bytes()
    {
        // Checked before anything plays. A projection that dropped a wire
        // would otherwise give equal bytes before and after play.
        byte[] source = Utf8(RelayFixture);
        Scene scene = Load(source);

        Same(source, MapWriter.Write(MapSceneBinder.FromScene(scene)));
    }

    [Fact]
    public void The_wired_map_runs_its_own_machine_when_it_is_played()
    {
        Scene scene = Load(Utf8(RelayFixture));

        Outcome outcome = Play(scene, TwoSeconds);

        outcome.TimerFires.ShouldBeGreaterThanOrEqualTo(4,
            "a logic_timer starts itself in OnActivate and refires forever");
        outcome.RelayTriggers.ShouldBe(3,
            "the fourth timer fire lands after OnHitMax's 0.1 s wire has disabled the relay");
        outcome.Count.ShouldBe(6f,
            "three triggers at the wire's param of 2 reach the counter's max of 6 and clamp there");
        outcome.RelayEnabled.ShouldBeFalse("OnHitMax fires once, on arrival, and its wire sends Disable");
    }

    [Fact]
    public void An_unresolved_target_is_the_only_thing_that_stops_the_machine()
    {
        // Negative control for the test above: break the one wire target.
        Scene scene = Load(Utf8(RelayFixture));
        Find(scene, "Tally").Name = "TallyRenamed";

        Outcome outcome = Play(scene, TwoSeconds, counterName: "TallyRenamed");

        outcome.Count.ShouldBe(0f, "Add never arrives, because 'Tally' now names nothing");
        outcome.RelayEnabled.ShouldBeTrue("and OnHitMax therefore never fires either");
        outcome.RelayTriggers.ShouldBeGreaterThan(3, "so nothing ever switches the relay off");
    }

    [Fact]
    public void Two_play_sessions_leave_the_authored_document_byte_identical()
    {
        // Played twice and the outcomes compared: a runtime that decremented
        // the authored times:1 wire would change the second session's result.
        byte[] source = Utf8(RelayFixture);
        Scene scene = Load(source);

        Outcome first = Play(scene, TwoSeconds);
        Outcome second = Play(scene, TwoSeconds);

        second.ShouldBe(first, "a second play session is a second run of the same document");
        first.Count.ShouldBe(6f, "and both of them really did run");

        Same(source, MapWriter.Write(MapSceneBinder.FromScene(scene)));
    }

    [Fact]
    public void A_bundle_that_has_been_played_twice_is_not_rewritten_when_it_is_saved()
    {
        // MapBundle.Save returns false when it wrote nothing.
        string bundle = Path.Combine(
            Path.GetTempPath(), $"spectra_entity_map_{Guid.NewGuid():N}{MapFormat.BundleExtension}");
        try
        {
            Directory.CreateDirectory(bundle);
            File.WriteAllBytes(MapBundle.DocumentPath(bundle), Utf8(RelayFixture));
            DateTime written = File.GetLastWriteTimeUtc(MapBundle.DocumentPath(bundle));

            var scene = new Scene("Empty");
            MapSceneBinder.ApplyTo(MapBundle.Load(bundle), scene);
            Play(scene, TwoSeconds);
            Play(scene, TwoSeconds);

            MapBundle.Save(bundle, MapSceneBinder.FromScene(scene))
                .ShouldBeFalse("a save with no edit in it must not touch the file");
            File.GetLastWriteTimeUtc(MapBundle.DocumentPath(bundle)).ShouldBe(written);
            Same(Utf8(RelayFixture), File.ReadAllBytes(MapBundle.DocumentPath(bundle)));
        }
        finally
        {
            if (Directory.Exists(bundle))
                Directory.Delete(bundle, recursive: true);
        }
    }

    [Fact]
    public void Playing_edits_the_runtime_wires_and_never_the_authored_ones()
    {
        Scene scene = Load(Utf8(RelayFixture));
        EntityData tally = Find(scene, "Tally").Entity.ShouldNotBeNull();

        Play(scene, TwoSeconds);

        tally.Connections.Count.ShouldBe(1);
        tally.Connections[0].TimesToFire.ShouldBe(1, "the wire fired, and the authored budget is untouched");

        tally.TryGetValue("startvalue", out string startValue).ShouldBeTrue();
        startValue.ShouldBe("0");
        Find(scene, "Gate").Entity!.TryGetValue("startdisabled", out string startDisabled).ShouldBeTrue();
        startDisabled.ShouldBe("0");
    }

    [Fact]
    public void The_branching_map_binds_to_a_scene_and_projects_back_to_the_same_bytes()
    {
        byte[] source = Utf8(BranchingFixture);

        Same(source, MapWriter.Write(MapSceneBinder.FromScene(Load(source))));
    }

    [Fact]
    public void The_branching_map_starts_itself_and_runs_to_its_end_on_the_first_tick()
    {
        Scene scene = Load(Utf8(BranchingFixture));
        var logger = new CapturingLogger();
        var world = new EntityWorld(scene, logger, EntityRuntime.Catalog([]));
        world.Activate();

        world.Tick(Tick);

        EntityRuntime.Live<MathCounter>(world, Find(scene, "Tally")).Value.ShouldBe(12f,
            "logic_auto added 2, and the branch added 10 once the compare saw 5 above 4");
        EntityRuntime.Live<LogicCompare>(world, Find(scene, "Gauge")).Value.ShouldBe(5f,
            "the count of 2 matched case02's 2.0 as a number, and its wire sent 5");
        EntityRuntime.Live<LogicBranch>(world, Find(scene, "Flag")).Value.ShouldBeTrue();
        world.PendingEventCount.ShouldBe(0, "the count of 12 matches no case, and OnDefault is unwired");
        logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty(logger.Describe());

        world.Deactivate();
        Same(Utf8(BranchingFixture), MapWriter.Write(MapSceneBinder.FromScene(scene)));
    }

    private readonly record struct Outcome(int TimerFires, int RelayTriggers, float Count, bool RelayEnabled);

    private static Scene Load(byte[] document)
    {
        var scene = new Scene("Empty");
        MapSceneBinder.ApplyTo(MapReader.Read(document), scene);
        return scene;
    }

    // Own catalogue: EntityCatalog.Shared freezes on first read.
    private static Outcome Play(Scene scene, int ticks, string counterName = "Tally")
    {
        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();

        for (int i = 0; i < ticks; i++)
            world.Tick(Tick);

        // Read before Deactivate; no instances exist after it.
        var outcome = new Outcome(
            EntityRuntime.Live<LogicTimer>(world, Find(scene, "Metronome")).FireCount,
            EntityRuntime.Live<LogicRelay>(world, Find(scene, "Gate")).TriggerCount,
            EntityRuntime.Live<MathCounter>(world, Find(scene, counterName)).Value,
            EntityRuntime.Live<LogicRelay>(world, Find(scene, "Gate")).IsEnabled);

        world.Deactivate();
        return outcome;
    }

    private static SceneNode Find(Scene scene, string name)
    {
        foreach (SceneNode node in scene.Root.Traverse())
        {
            if (node.Name == name)
                return node;
        }

        throw new Xunit.Sdk.XunitException($"The fixture has no node named '{name}'.");
    }

    private static byte[] Utf8(string text) =>
        Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n") + "\n");

    private static void Same(byte[] expected, byte[] actual)
    {
        if (expected.AsSpan().SequenceEqual(actual)) return;

        string want = Encoding.UTF8.GetString(expected);
        string got = Encoding.UTF8.GetString(actual);
        int at = 0;
        while (at < want.Length && at < got.Length && want[at] == got[at]) at++;

        throw new Xunit.Sdk.XunitException(
            $"The document changed on the way through, first at character {at}.\n"
            + $"--- expected ---\n{want}\n--- actual ---\n{got}");
    }
}
