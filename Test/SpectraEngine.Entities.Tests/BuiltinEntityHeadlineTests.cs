using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// The one line each built-in class says about itself where there is room
/// for one, such as a card in a wiring view.
/// </summary>
public sealed class BuiltinEntityHeadlineTests
{
    private readonly Scene _scene = new("Headlines");
    private readonly EntityHeadline _headline = new();

    [Fact]
    public void A_door_says_closed_then_how_far_open_then_open()
    {
        // 1.95 of travel at 3 a second is 39 ticks.
        SceneNode node = Movers.Part(_scene.Root, "door", new Vector3(1f, 2f, 0.2f), new Vector3(0f, 1f, 0f));
        node.Entity = new EntityData("func_door");
        node.Entity.SetValue("speed", "3");
        node.Entity.SetValue("wait", "-1");
        EntityWorld world = Play();
        FuncDoor door = EntityRuntime.Live<FuncDoor>(world, node);

        Headline(door).ShouldBe(("state", "closed"));

        EntityRuntime.Send(door, "Open");
        Movers.Run(world, 14);
        Headline(door).ShouldBe(("opening", "14 of 39 ticks"));

        Movers.Run(world, 25);
        Headline(door).ShouldBe(("state", "open"));

        EntityRuntime.Send(door, "Close");
        Movers.Run(world, 9);
        Headline(door).ShouldBe(("closing", "30 of 39 ticks"));
    }

    [Fact]
    public void A_linear_mover_says_where_along_its_travel_it_is()
    {
        SceneNode node = Movers.Part(_scene.Root, "lift", new Vector3(2f, 0.4f, 2f), Vector3.Zero);
        node.Entity = new EntityData("func_movelinear");
        node.Entity.SetValue("distance", "2");
        node.Entity.SetValue("speed", "2");
        EntityWorld world = Play();
        FuncMoveLinear lift = EntityRuntime.Live<FuncMoveLinear>(world, node);

        Headline(lift).ShouldBe(("at", "0 of 60 ticks"));

        EntityRuntime.Send(lift, "Open");
        Movers.Run(world, 12);

        Headline(lift).ShouldBe(("at", "12 of 60 ticks"));
    }

    [Fact]
    public void A_button_says_whether_it_is_in_or_out()
    {
        SceneNode node = Movers.Part(_scene.Root, "button", new Vector3(0.5f, 0.5f, 0.2f), Vector3.Zero);
        node.Entity = new EntityData("func_button");
        EntityWorld world = Play();
        FuncButton button = EntityRuntime.Live<FuncButton>(world, node);

        Headline(button).ShouldBe(("state", "out"));

        EntityRuntime.Send(button, "Use");
        Movers.Run(world, 2);

        Headline(button).ShouldBe(("state", "in"));
    }

    [Fact]
    public void A_counter_says_its_value_and_its_most_when_it_has_one()
    {
        SceneNode clamped = EntityRuntime.Place(_scene.Root, "clamped", "math_counter");
        clamped.Entity!.SetValue("startvalue", "1.5");
        clamped.Entity!.SetValue("max", "4");
        SceneNode free = EntityRuntime.Place(_scene.Root, "free", "math_counter");
        free.Entity!.SetValue("startvalue", "2");
        EntityWorld world = Play();

        Headline(EntityRuntime.Live<MathCounter>(world, clamped)).ShouldBe(("value", "1.5 of 4"));
        Headline(EntityRuntime.Live<MathCounter>(world, free)).ShouldBe(("value", "2"));
    }

    [Fact]
    public void A_relay_says_how_often_it_fired_or_that_it_is_disabled()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "relay", "logic_relay");
        EntityWorld world = Play();
        LogicRelay relay = EntityRuntime.Live<LogicRelay>(world, node);

        Headline(relay).ShouldBe(("fired", "0 times"));

        EntityRuntime.Send(relay, "Trigger");
        Headline(relay).ShouldBe(("fired", "1 time"));

        EntityRuntime.Send(relay, "Disable");
        Headline(relay).ShouldBe(("state", "disabled"));
    }

    [Fact]
    public void Every_built_in_class_with_state_gives_a_headline()
    {
        foreach (EntitySchema schema in BuiltinEntities.Schemas)
        {
            SceneNode node = schema.Placement is EntityPlacement.Brush or EntityPlacement.Volume
                ? Movers.Part(_scene.Root, schema.ClassName, Vector3.One, Vector3.Zero)
                : _scene.Root.CreateChild(schema.ClassName);
            node.Entity = new EntityData(schema.ClassName);
        }

        EntityWorld world = Play();

        foreach (Entity entity in world.Entities)
        {
            var rows = new List<KeyValuePair<string, string>>();
            entity.DescribeState(new EntityStateWriter(rows));

            _headline.Clear();
            entity.DescribeState(new EntityStateWriter(_headline));

            // A card shows the first row otherwise, which is rarely the one to show.
            _headline.IsSet.ShouldBe(
                rows.Count > 0,
                $"{entity.ClassName} has state rows, so it should also call Headline");
        }
    }

    private (string Label, string Value) Headline(Entity entity)
    {
        _headline.Clear();
        entity.DescribeState(new EntityStateWriter(_headline));
        _headline.IsSet.ShouldBeTrue();
        return (_headline.Label, _headline.Value);
    }

    private EntityWorld Play()
    {
        var world = new EntityWorld(_scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();
        return world;
    }
}
