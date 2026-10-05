using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// The entity commands over the built-in classes, reading the schemas the
/// generator wrote for them.
/// </summary>
public sealed class EntityCommandsOverBuiltinsTests
{
    private const float Tick = 1f / 60f;

    [Fact]
    public void An_input_in_the_wrong_case_is_refused_with_the_inputs_the_class_declares()
    {
        Scene scene = Level();
        var console = new SpectraConsole();
        EntityConsoleCommands.Register(console.Commands, new EntityWatch(console.Output));
        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();

        console.Execute("ent_fire relay1 trigger", new ConsoleFrame(scene, world, IsPlaying: true));

        Printed(console).ShouldBe(
        [
            "ent_fire: logic_relay has no input 'trigger'. Did you mean Trigger? " +
            "Inputs: Trigger Enable Disable Toggle",
        ]);
    }

    [Fact]
    public void A_chain_from_map_spawn_is_watched_from_tick_zero()
    {
        Scene scene = Level();
        var console = new SpectraConsole();
        var watch = new EntityWatch(console.Output);
        EntityConsoleCommands.Register(console.Commands, watch);
        console.Execute("ent_watch on", new ConsoleFrame(scene, null));
        console.Output.Drain();

        // Before Activate, as the scene manager does it.
        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]))
        {
            Trace = watch.ActiveTrace,
        };
        world.Activate();
        world.Tick(Tick);

        Printed(console).ShouldBe(
        [
            "ent 0 fire auto.OnMapSpawn wires=1",
            "ent 1 send auto.OnMapSpawn -> relay1.Trigger",
            "ent 1 fire relay1.OnTrigger wires=3 by=auto",
            "ent 1 wait relay1.OnTrigger -> door1.Open in=2s",
            "ent 1 send relay1.OnTrigger -> counter.Add(\"5\") by=auto",
            "ent 1 fire counter.OutValue wires=0 by=auto",
            "ent 1 miss relay1.OnTrigger -> dor1.Close  nothing is named dor1",
        ]);
    }

    // An auto that triggers a relay, which opens a door late, adds to a
    // counter and fires at a name nothing has.
    private static Scene Level()
    {
        var scene = new Scene("Wired")
        {
            EntitySchemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(BuiltinEntities.Schemas)),
        };

        SceneNode auto = EntityRuntime.Place(scene.Root, "auto", "logic_auto");
        SceneNode relay = EntityRuntime.Place(scene.Root, "relay1", "logic_relay");
        EntityRuntime.Place(scene.Root, "counter", "math_counter");
        EntityRuntime.Place(scene.Root, "door1", "test_recorder");

        EntityRuntime.Wire(auto, LogicAuto.OnMapSpawn, "relay1", "Trigger");
        EntityRuntime.Wire(relay, LogicRelay.OnTrigger, "door1", "Open", delay: 2f);
        EntityRuntime.Wire(relay, LogicRelay.OnTrigger, "counter", "Add", "5");
        EntityRuntime.Wire(relay, LogicRelay.OnTrigger, "dor1", "Close");
        return scene;
    }

    private static string[] Printed(SpectraConsole console) =>
        [.. console.Output.Drain().Select(line => line.Text)];
}
