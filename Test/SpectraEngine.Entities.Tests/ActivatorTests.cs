using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Tests;

/// <summary>Who <c>!activator</c> names along a chain.</summary>
public sealed class ActivatorTests
{
    [Fact]
    public void An_entity_that_starts_a_chain_is_its_activator()
    {
        // The timer fires on its own, with nobody behind it. A wire back to
        // !activator has to reach the timer, or it resolves to nothing and
        // the timer never stops.
        var log = new List<string>();
        var scene = new Scene("Entities");

        SceneNode timer = EntityRuntime.Place(scene.Root, "timer", "logic_timer");
        timer.Entity!.SetValue("refiretime", "0.5");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(timer, LogicTimer.OnTimer, "sink", "Ping");
        EntityRuntime.Wire(timer, LogicTimer.OnTimer, TargetNameIndex.ActivatorToken, "Disable");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        for (int i = 0; i < 12; i++)
            world.Tick(0.25f);

        log.ShouldBe(["sink:Ping:"]);
        EntityRuntime.Live<LogicTimer>(world, timer).IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void A_chain_keeps_the_activator_it_was_given()
    {
        // The relay passes on whoever triggered it, not itself.
        var log = new List<string>();
        var scene = new Scene("Entities");

        SceneNode timer = EntityRuntime.Place(scene.Root, "timer", "logic_timer");
        timer.Entity!.SetValue("refiretime", "0.5");
        SceneNode relay = EntityRuntime.Place(scene.Root, "relay", "logic_relay");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");

        EntityRuntime.Wire(timer, LogicTimer.OnTimer, "relay", "Trigger");
        EntityRuntime.Wire(timer, LogicTimer.OnTimer, "sink", "Ping");
        EntityRuntime.Wire(relay, LogicRelay.OnTrigger, TargetNameIndex.ActivatorToken, "Disable");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        for (int i = 0; i < 12; i++)
            world.Tick(0.25f);

        log.ShouldBe(["sink:Ping:"]);
        EntityRuntime.Live<LogicTimer>(world, timer).IsEnabled.ShouldBeFalse();
        EntityRuntime.Live<LogicRelay>(world, relay).IsEnabled.ShouldBeTrue();
    }
}
