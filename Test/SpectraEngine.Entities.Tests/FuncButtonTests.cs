using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// A button run through a real <see cref="EntityWorld"/>: when it takes a
/// press, what it fires, and that it comes back out by itself.
/// </summary>
public sealed class FuncButtonTests
{
    // The fixture button is 0.2 deep: 0.18 of travel at 1 a second.
    private const int TravelTicks = 11;

    // The default wait of a second.
    private const int WaitTicks = 60;

    private const string OnPressed = "sink:OnPressed:";
    private const string OnIn = "sink:OnIn:";
    private const string OnOut = "sink:OnOut:";

    [Fact]
    public void A_press_fires_OnPressed_then_OnIn_and_after_the_wait_OnOut()
    {
        Rig rig = Start();

        rig.Send("Press");
        Movers.Run(rig.World, TravelTicks + WaitTicks + TravelTicks - 2);
        rig.Button.IsPressed.ShouldBeTrue();

        rig.World.Tick(Movers.Dt);

        rig.Button.IsOut.ShouldBeTrue();
        rig.Button.IsPressed.ShouldBeFalse();
        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);

        // The last output is delivered a tick after it is fired.
        rig.World.Tick(Movers.Dt);
        rig.Log.ShouldBe([OnPressed, OnIn, OnOut]);
        rig.World.TickingEntityCount.ShouldBe(0);
    }

    [Fact]
    public void Use_presses_the_button_as_Press_does()
    {
        Rig rig = Start();

        rig.Send("Use");
        rig.World.Tick(Movers.Dt);

        rig.Button.IsPressed.ShouldBeTrue();
        rig.Log.ShouldBe([OnPressed]);
    }

    [Fact]
    public void A_button_with_no_keyvalues_goes_in_by_its_depth_less_the_lip()
    {
        Rig rig = Start();
        Vector3 authored = rig.Node.LocalPosition;

        rig.Button.TravelTicks.ShouldBe(TravelTicks);
        rig.Send("Press");
        Movers.Run(rig.World, TravelTicks);

        rig.Button.IsIn.ShouldBeTrue();
        Vector3.Distance(rig.Node.LocalPosition, authored + new Vector3(0f, 0f, -0.18f)).ShouldBeLessThan(1e-5f);
    }

    [Fact]
    public void A_stated_distance_is_used_as_it_is()
    {
        Rig rig = Start(button => button.SetValue("distance", "0.5"));
        Vector3 authored = rig.Node.LocalPosition;

        rig.Button.TravelTicks.ShouldBe(30);
        rig.Send("Press");
        Movers.Run(rig.World, 30);

        rig.Node.LocalPosition.ShouldBe(authored + new Vector3(0f, 0f, -0.5f));
    }

    [Theory]
    [InlineData("0.5", 30)]
    [InlineData("1", 60)]
    [InlineData("0", 1)]
    public void The_button_stays_in_for_its_wait_counted_in_ticks(string wait, int waitTicks)
    {
        Rig rig = Start(button => button.SetValue("wait", wait));

        rig.Send("Press");
        Movers.Run(rig.World, TravelTicks + waitTicks - 1);
        rig.Button.IsIn.ShouldBeTrue();

        rig.World.Tick(Movers.Dt);

        rig.Button.TicksTravelled.ShouldBe(TravelTicks - 1);
    }

    [Fact]
    public void A_wait_of_minus_one_keeps_the_button_in()
    {
        Rig rig = Start(button => button.SetValue("wait", "-1"));

        rig.Send("Press");
        Movers.Run(rig.World, 2000);

        rig.Button.IsIn.ShouldBeTrue();
        rig.World.TickingEntityCount.ShouldBe(0);

        rig.Send("Press");
        Movers.Run(rig.World, 5);

        rig.Log.ShouldBe([OnPressed, OnIn]);
    }

    [Fact]
    public void A_press_while_going_in_does_nothing()
    {
        Rig rig = Start();

        rig.Send("Press");
        Movers.Run(rig.World, 5);
        rig.Send("Press");
        Movers.Run(rig.World, TravelTicks - 5);

        rig.Button.IsIn.ShouldBeTrue();
        Movers.Run(rig.World, 2);
        rig.Log.ShouldBe([OnPressed, OnIn]);
    }

    [Fact]
    public void A_press_while_in_does_not_restart_the_wait()
    {
        Rig rig = Start();

        rig.Send("Press");
        Movers.Run(rig.World, TravelTicks + 10);
        rig.Send("Use");
        Movers.Run(rig.World, WaitTicks - 10);

        rig.Button.TicksTravelled.ShouldBe(TravelTicks - 1);
        rig.Log.ShouldBe([OnPressed, OnIn]);
    }

    [Fact]
    public void A_press_while_coming_back_out_does_not_turn_the_button_round()
    {
        Rig rig = Start();

        rig.Send("Press");
        Movers.Run(rig.World, TravelTicks + WaitTicks + 5);
        rig.Button.TicksTravelled.ShouldBe(TravelTicks - 6);

        rig.Send("Press");
        Movers.Run(rig.World, TravelTicks - 6);

        rig.Button.IsOut.ShouldBeTrue();
        rig.World.Tick(Movers.Dt);
        rig.Log.ShouldBe([OnPressed, OnIn, OnOut]);
    }

    [Fact]
    public void A_button_that_is_back_out_takes_the_next_press()
    {
        Rig rig = Start();

        rig.Send("Press");
        Movers.Run(rig.World, TravelTicks + WaitTicks + TravelTicks);
        rig.Send("Press");
        Movers.Run(rig.World, TravelTicks);

        rig.Button.IsIn.ShouldBeTrue();
        rig.World.Tick(Movers.Dt);
        rig.Log.ShouldBe([OnPressed, OnIn, OnOut, OnPressed, OnIn]);
    }

    [Fact]
    public void OnPressed_carries_the_activator_of_the_input_that_pressed_the_button()
    {
        var log = new List<string>();
        var scene = new Scene("Button");
        SceneNode button = Movers.Part(scene.Root, "button", new Vector3(0.5f, 0.5f, 0.2f), Vector3.Zero);
        button.Entity = new EntityData("func_button");
        SceneNode presser = EntityRuntime.Place(scene.Root, "presser", "test_recorder");
        EntityRuntime.Wire(button, FuncButton.OnPressed, TargetNameIndex.ActivatorToken, "Pressing");
        EntityRuntime.Wire(button, FuncButton.OnIn, TargetNameIndex.ActivatorToken, "Arrived");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        world.QueueInput(
            EntityRuntime.Live<FuncButton>(world, button), "Press",
            activator: EntityRuntime.Live<RecordingEntity>(world, presser));
        Movers.Run(world, TravelTicks + 1);

        // The arrival starts with the button, which records nothing.
        log.ShouldBe(["presser:Pressing:"]);
    }

    [Fact]
    public void A_press_with_no_activator_has_the_button_as_its_own()
    {
        // What a use from the player is: the player is not an entity.
        var logger = new CapturingLogger();
        var scene = new Scene("Button");
        SceneNode button = Movers.Part(scene.Root, "button", new Vector3(0.5f, 0.5f, 0.2f), Vector3.Zero);
        button.Entity = new EntityData("func_button");
        EntityRuntime.Wire(button, FuncButton.OnPressed, TargetNameIndex.ActivatorToken, "Echo");

        var world = new EntityWorld(scene, logger, EntityRuntime.Catalog([]));
        world.Activate();
        world.QueueInput(EntityRuntime.Live<FuncButton>(world, button), "Use");
        world.Tick(Movers.Dt);

        // The wire came back to the button, which has no such input.
        logger.MessagesAt(LogLevel.Debug)
            .ShouldContain(message => message.Contains("'button'") && message.Contains("'Echo'"));
    }

    [Theory]
    [InlineData("0 0 0")]
    [InlineData("sideways")]
    public void A_zero_or_unreadable_movedir_is_refused_and_the_default_is_used(string movedir)
    {
        Rig rig = Start(button => button.SetValue("movedir", movedir));
        Vector3 authored = rig.Node.LocalPosition;

        rig.Logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem().ShouldContain($"'movedir' = '{movedir}'");

        rig.Send("Press");
        Movers.Run(rig.World, TravelTicks);

        Vector3.Distance(rig.Node.LocalPosition, authored + new Vector3(0f, 0f, -0.18f)).ShouldBeLessThan(1e-5f);
    }

    [Fact]
    public void Movedir_is_in_the_buttons_own_axes()
    {
        // A quarter turn about Y takes the button's -Z to the parent's -X.
        Rig rig = Start(rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f));
        Vector3 authored = rig.Node.LocalPosition;

        rig.Send("Press");
        Movers.Run(rig.World, TravelTicks);

        Vector3.Distance(rig.Node.LocalPosition, authored + new Vector3(-0.18f, 0f, 0f)).ShouldBeLessThan(1e-5f);
    }

    [Fact]
    public void A_button_no_deeper_than_its_lip_still_presses_where_it_stands()
    {
        Rig rig = Start(button => button.SetValue("lip", "0.5"));

        rig.Button.TravelTicks.ShouldBe(1);
        rig.Send("Press");
        Movers.Run(rig.World, 2);

        rig.Button.IsIn.ShouldBeTrue();
        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
        rig.Log.ShouldBe([OnPressed, OnIn]);
    }

    [Fact]
    public void A_button_with_a_world_brush_fires_OnPressed_and_stays_out()
    {
        var log = new List<string>();
        var logger = new CapturingLogger();
        var scene = new Scene("Button");
        SceneNode button = scene.Root.CreateChild("button");
        button.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(
            new Vector3(-0.25f, -0.25f, -0.1f), new Vector3(0.25f, 0.25f, 0.1f));
        button.Entity = new EntityData("func_button");
        Movers.WireToSink(button, FuncButton.OnPressed, FuncButton.OnIn);
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        byte[] authored = Movers.Bits(button.LocalTransform);

        var world = new EntityWorld(scene, logger, EntityRuntime.Catalog(log));
        world.Activate();
        FuncButton live = EntityRuntime.Live<FuncButton>(world, button);
        world.QueueInput(live, "Press");
        Movers.Run(world, 10);

        log.ShouldBe([OnPressed]);
        live.IsPressed.ShouldBeFalse();
        Movers.Bits(button.LocalTransform).ShouldBe(authored);
        world.RefusedMoveCount.ShouldBe(1);
        world.TickingEntityCount.ShouldBe(0);
        logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem().ShouldContain("'button'");
    }

    [Fact]
    public void Stopping_while_the_button_is_in_restores_the_authored_transform()
    {
        Rig rig = Start();

        rig.Send("Press");
        Movers.Run(rig.World, TravelTicks);
        Movers.Bits(rig.Node.LocalTransform).ShouldNotBe(rig.Authored);

        rig.World.Deactivate();

        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
        rig.Scene.StaticWorldDirty.ShouldBeFalse();
    }

    [Fact]
    public void The_schema_describes_what_the_class_declares()
    {
        EntitySchema schema = FuncButton.SpectraSchema;

        schema.ClassName.ShouldBe("func_button");
        schema.DisplayName.ShouldBe("Button");
        schema.Group.ShouldBe("Movers");
        schema.Placement.ShouldBe(EntityPlacement.Brush);
        schema.Inputs.ShouldBe(["Use", "Press"]);
        schema.Outputs.ShouldBe(["OnPressed", "OnIn", "OnOut"]);

        schema.Keyvalues.Select(keyvalue => keyvalue.Name)
            .ShouldBe(["movedir", "distance", "lip", "speed", "wait"]);
        schema.Keyvalues.Select(keyvalue => keyvalue.Default)
            .ShouldBe(["0 0 -1", "0", "0.02", "1", "1"]);
        schema.Keyvalues.Select(keyvalue => keyvalue.Type).ShouldBe(
        [
            KeyvalueType.Vec3, KeyvalueType.Float, KeyvalueType.Float,
            KeyvalueType.Float, KeyvalueType.Float,
        ]);
        schema.Keyvalues[3].Min.ShouldBe(LinearMover.MinimumSpeed);
    }

    [Fact]
    public void The_schema_defaults_are_the_values_a_bare_button_has()
    {
        var bare = new FuncButton();
        var parsed = new FuncButton();
        foreach (KeyvalueDescriptor keyvalue in FuncButton.SpectraSchema.Keyvalues)
            parsed.ParseKeyValue(keyvalue.Name, keyvalue.Default).ShouldBeTrue(keyvalue.Name);

        parsed.MoveDirection.ShouldBe(bare.MoveDirection);
        parsed.Distance.ShouldBe(bare.Distance);
        parsed.Lip.ShouldBe(bare.Lip);
        parsed.Speed.ShouldBe(bare.Speed);
        parsed.Wait.ShouldBe(bare.Wait);
    }

    private sealed record Rig(
        Scene Scene, SceneNode Node, EntityWorld World, FuncButton Button,
        List<string> Log, CapturingLogger Logger, byte[] Authored)
    {
        public void Send(string input) => World.QueueInput(Button, input);
    }

    // One button, half a unit square and 0.2 deep, every output wired to a
    // recorder. A part, so the scene starts with a clean static world.
    private static Rig Start(Action<EntityData>? author = null, Quaternion? rotation = null)
    {
        var log = new List<string>();
        var logger = new CapturingLogger();
        var scene = new Scene("Button");

        SceneNode node = Movers.Part(
            scene.Root, "button", new Vector3(0.5f, 0.5f, 0.2f), new Vector3(3.3f, 1.1f, -2.7f));
        node.LocalRotation = rotation ?? Quaternion.Identity;
        node.Entity = new EntityData("func_button");
        author?.Invoke(node.Entity);
        Movers.WireToSink(node, FuncButton.OnPressed, FuncButton.OnIn, FuncButton.OnOut);
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        byte[] authored = Movers.Bits(node.LocalTransform);

        var world = new EntityWorld(scene, logger, EntityRuntime.Catalog(log));
        world.Activate();
        return new Rig(scene, node, world, EntityRuntime.Live<FuncButton>(world, node), log, logger, authored);
    }
}
