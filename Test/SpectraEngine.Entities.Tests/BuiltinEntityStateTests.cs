using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// What each built-in class tells a person who asks for its state while the
/// level runs.
/// </summary>
public sealed class BuiltinEntityStateTests
{
    private const float Tick = 1f / 60f;

    private readonly Scene _scene = new("State");
    private readonly FakePlayerPresence _player = new() { Feet = new Vector3(6f, 0f, 0f) };

    [Fact]
    public void A_relay_describes_whether_it_is_on_and_how_often_it_fired()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "relay", "logic_relay");
        EntityWorld world = Play();
        LogicRelay relay = EntityRuntime.Live<LogicRelay>(world, node);

        Describe(relay).ShouldBe([Row("enabled", "1"), Row("triggers", "0")]);

        EntityRuntime.Send(relay, "Trigger");
        EntityRuntime.Send(relay, "Trigger");
        EntityRuntime.Send(relay, "Disable");

        Describe(relay).ShouldBe([Row("enabled", "0"), Row("triggers", "2")]);
    }

    [Fact]
    public void A_branch_describes_its_value_and_the_inputs_it_refused()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "branch", "logic_branch");
        node.Entity!.SetValue("initialvalue", "1");
        EntityWorld world = Play();
        LogicBranch branch = EntityRuntime.Live<LogicBranch>(world, node);

        Describe(branch).ShouldBe([Row("value", "1"), Row("refused inputs", "0")]);

        EntityRuntime.Send(branch, "Toggle");
        EntityRuntime.Send(branch, "SetValue", "maybe");

        Describe(branch).ShouldBe([Row("value", "0"), Row("refused inputs", "1")]);
    }

    [Fact]
    public void A_timer_describes_whether_it_runs_its_fires_and_its_interval()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "timer", "logic_timer");
        node.Entity!.SetValue("refiretime", "0.5");
        EntityWorld world = Play();
        LogicTimer timer = EntityRuntime.Live<LogicTimer>(world, node);

        Describe(timer).ShouldBe([Row("enabled", "1"), Row("fires", "0"), Row("refire time", "0.5")]);

        EntityRuntime.Send(timer, "FireTimer");
        EntityRuntime.Send(timer, "RefireTime", "2");
        EntityRuntime.Send(timer, "Disable");

        Describe(timer).ShouldBe([Row("enabled", "0"), Row("fires", "1"), Row("refire time", "2")]);
    }

    [Fact]
    public void A_counter_describes_its_value_and_its_bounds()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "counter", "math_counter");
        node.Entity!.SetValue("startvalue", "1.5");
        node.Entity!.SetValue("max", "4");
        EntityWorld world = Play();
        MathCounter counter = EntityRuntime.Live<MathCounter>(world, node);

        Describe(counter).ShouldBe(
            [Row("value", "1.5"), Row("min", "0"), Row("max", "4"), Row("refused inputs", "0")]);

        EntityRuntime.Send(counter, "Add", "10");
        EntityRuntime.Send(counter, "SetHitMin", "2");
        EntityRuntime.Send(counter, "SetValue", "lots");

        Describe(counter).ShouldBe(
            [Row("value", "4"), Row("min", "2"), Row("max", "4"), Row("refused inputs", "1")]);
    }

    [Fact]
    public void A_compare_describes_both_of_its_values()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "compare", "logic_compare");
        node.Entity!.SetValue("initialvalue", "3");
        node.Entity!.SetValue("comparevalue", "5");
        EntityWorld world = Play();
        LogicCompare compare = EntityRuntime.Live<LogicCompare>(world, node);

        Describe(compare).ShouldBe(
            [Row("value", "3"), Row("compare value", "5"), Row("refused inputs", "0")]);

        EntityRuntime.Send(compare, "SetValue", "-1.25");
        EntityRuntime.Send(compare, "SetCompareValue", "7");
        EntityRuntime.Send(compare, "SetValue", "");

        Describe(compare).ShouldBe(
            [Row("value", "-1.25"), Row("compare value", "7"), Row("refused inputs", "1")]);
    }

    [Fact]
    public void A_multiple_describes_whether_it_senses_and_whether_someone_is_inside()
    {
        SceneNode zone = Zone("trigger_multiple");
        EntityWorld world = Play();
        TriggerMultiple trigger = EntityRuntime.Live<TriggerMultiple>(world, zone);

        Describe(trigger).ShouldBe([Row("enabled", "1"), Row("touched", "0"), Row("triggers", "0")]);

        _player.Feet = Vector3.Zero;
        world.Tick(Tick);

        Describe(trigger).ShouldBe([Row("enabled", "1"), Row("touched", "1"), Row("triggers", "1")]);

        EntityRuntime.Send(trigger, "Disable");

        Describe(trigger).ShouldBe([Row("enabled", "0"), Row("touched", "0"), Row("triggers", "1")]);
    }

    [Fact]
    public void A_once_describes_that_it_is_spent()
    {
        SceneNode zone = Zone("trigger_once");
        EntityWorld world = Play();
        TriggerOnce once = EntityRuntime.Live<TriggerOnce>(world, zone);

        Describe(once).ShouldBe([Row("enabled", "1"), Row("triggers", "0")]);

        _player.Feet = Vector3.Zero;
        world.Tick(Tick);

        Describe(once).ShouldBe([Row("enabled", "0"), Row("triggers", "1")]);
    }

    [Fact]
    public void A_teleport_describes_how_many_times_it_moved_the_player()
    {
        SceneNode zone = Zone("trigger_teleport");
        zone.Entity!.SetValue("target", "exit");
        EntityRuntime.Place(_scene.Root, "exit", "info_teleport_destination").LocalPosition =
            new Vector3(20f, 0f, 0f);
        EntityWorld world = Play();
        TriggerTeleport teleport = EntityRuntime.Live<TriggerTeleport>(world, zone);

        Describe(teleport).ShouldBe([Row("enabled", "1"), Row("touched", "0"), Row("teleports", "0")]);

        _player.Feet = Vector3.Zero;
        world.Tick(Tick);

        // Still counted as inside until the next tick's pass sees the player gone.
        Describe(teleport).ShouldBe([Row("enabled", "1"), Row("touched", "1"), Row("teleports", "1")]);
    }

    [Fact]
    public void Describing_an_entity_changes_nothing()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "counter", "math_counter");
        node.Entity!.SetValue("startvalue", "2");
        EntityWorld world = Play();
        MathCounter counter = EntityRuntime.Live<MathCounter>(world, node);

        List<KeyValuePair<string, string>> first = Describe(counter);
        List<KeyValuePair<string, string>> second = Describe(counter);

        second.ShouldBe(first);
        counter.Value.ShouldBe(2f);
    }

    // A 2 by 2 by 2 volume at the origin, stamped the way the editor stamps one.
    private SceneNode Zone(string className)
    {
        SceneNode node = _scene.Root.CreateChild("zone");
        node.LocalPosition = new Vector3(0f, 1f, 0f);
        node.Entity = new EntityData(className);

        // Kind before brush, or the node is briefly a world brush.
        node.BrushKind = BrushKind.Part;
        node.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        node.CanCollide = false;
        node.CanQuery = false;
        node.IsRendered = false;
        return node;
    }

    private EntityWorld Play()
    {
        var world = new EntityWorld(_scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();
        world.Player = _player;
        return world;
    }

    private static List<KeyValuePair<string, string>> Describe(Entity entity)
    {
        var rows = new List<KeyValuePair<string, string>>();
        entity.DescribeState(new EntityStateWriter(rows));
        return rows;
    }

    private static KeyValuePair<string, string> Row(string name, string value) => new(name, value);
}
