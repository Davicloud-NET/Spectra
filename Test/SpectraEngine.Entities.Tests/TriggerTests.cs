using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// The trigger classes: a player walks into a volume and what the volume is
/// wired to hears about it.
/// </summary>
public sealed class TriggerTests
{
    private const float Tick = 1f / 60f;

    private static readonly Vector3 Inside = Vector3.Zero;
    private static readonly Vector3 Outside = new(6f, 0f, 0f);

    private readonly List<string> _log = [];
    private readonly CapturingLogger _logger = new();
    private readonly Scene _scene = new("Triggers");
    private readonly FakePlayerPresence _player = new() { Feet = Outside };

    [Fact]
    public void A_multiple_fires_on_the_way_in_and_on_the_way_out()
    {
        SceneNode zone = Zone("trigger_multiple");
        EntityRuntime.Wire(zone, TriggerMultiple.OnStartTouch, "sink", "Start");
        EntityRuntime.Wire(zone, TriggerMultiple.OnTrigger, "sink", "Trigger");
        EntityRuntime.Wire(zone, TriggerMultiple.OnEndTouch, "sink", "End");
        EntityWorld world = Play();
        world.Tick(Tick);
        _log.ShouldBeEmpty();

        _player.Feet = Inside;
        world.Tick(Tick);
        _log.ShouldBe(["sink:Start:", "sink:Trigger:"]);

        _player.Feet = Outside;
        world.Tick(Tick);
        _log.ShouldBe(["sink:Start:", "sink:Trigger:", "sink:End:"]);
    }

    [Fact]
    public void A_multiple_refires_every_wait_while_touched()
    {
        SceneNode zone = Zone("trigger_multiple");
        zone.Entity!.SetValue("wait", "0.5");
        EntityRuntime.Wire(zone, TriggerMultiple.OnTrigger, "sink", "Trigger");
        _player.Feet = Inside;
        EntityWorld world = Play();

        var heardIn = new List<long>();
        for (int i = 0; i < 100; i++)
        {
            int before = _log.Count;
            world.Tick(Tick);
            if (_log.Count > before)
                heardIn.Add(world.TickNumber);
        }

        // Half a second is 30 ticks.
        heardIn.ShouldBe([1L, 31L, 61L, 91L]);
    }

    [Fact]
    public void A_wait_of_zero_fires_OnTrigger_on_entry_only()
    {
        SceneNode zone = Zone("trigger_multiple");
        zone.Entity!.SetValue("wait", "0");
        EntityRuntime.Wire(zone, TriggerMultiple.OnTrigger, "sink", "Trigger");
        _player.Feet = Inside;
        EntityWorld world = Play();

        for (int i = 0; i < 200; i++)
            world.Tick(Tick);

        _log.ShouldBe(["sink:Trigger:"]);
        world.TickingEntityCount.ShouldBe(0);
    }

    [Fact]
    public void Leaving_a_multiple_stops_its_refire()
    {
        SceneNode zone = Zone("trigger_multiple");
        zone.Entity!.SetValue("wait", "0.1");
        EntityRuntime.Wire(zone, TriggerMultiple.OnTrigger, "sink", "Trigger");
        _player.Feet = Inside;
        EntityWorld world = Play();

        // A tenth of a second is 6 ticks: heard in ticks 1, 7, 13 and 19.
        for (int i = 0; i < 20; i++)
            world.Tick(Tick);
        _log.Count.ShouldBe(4);
        world.TickingEntityCount.ShouldBe(1);

        _player.Feet = Outside;
        for (int i = 0; i < 30; i++)
            world.Tick(Tick);

        _log.Count.ShouldBe(4);
        world.TickingEntityCount.ShouldBe(0);
    }

    [Fact]
    public void Disable_while_touched_fires_OnEndTouch_and_Enable_starts_a_new_touch()
    {
        SceneNode zone = Zone("trigger_multiple");
        zone.Entity!.SetValue("wait", "0");
        EntityRuntime.Wire(zone, TriggerMultiple.OnStartTouch, "sink", "Start");
        EntityRuntime.Wire(zone, TriggerMultiple.OnEndTouch, "sink", "End");
        _player.Feet = Inside;
        EntityWorld world = Play();
        TriggerMultiple trigger = EntityRuntime.Live<TriggerMultiple>(world, zone);
        world.Tick(Tick);
        trigger.IsTouched.ShouldBeTrue();

        world.QueueInput(trigger, "Disable");
        world.Tick(Tick);

        _log.ShouldBe(["sink:Start:", "sink:End:"]);
        trigger.IsEnabled.ShouldBeFalse();
        trigger.IsTouched.ShouldBeFalse();

        // The player has not moved, and a disabled trigger does not sense it.
        for (int i = 0; i < 5; i++)
            world.Tick(Tick);
        _log.ShouldBe(["sink:Start:", "sink:End:"]);

        // Enable lands after its tick's touch pass, so the next tick starts the touch.
        world.QueueInput(trigger, "Enable");
        world.Tick(Tick);
        world.Tick(Tick);

        _log.ShouldBe(["sink:Start:", "sink:End:", "sink:Start:"]);
        trigger.IsTouched.ShouldBeTrue();
    }

    [Fact]
    public void Toggle_switches_a_trigger_off_and_then_on()
    {
        SceneNode zone = Zone("trigger_multiple");
        EntityWorld world = Play();
        TriggerMultiple trigger = EntityRuntime.Live<TriggerMultiple>(world, zone);

        world.QueueInput(trigger, "Toggle");
        world.Tick(Tick);
        trigger.IsEnabled.ShouldBeFalse();

        world.QueueInput(trigger, "Toggle");
        world.Tick(Tick);
        trigger.IsEnabled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("trigger_multiple")]
    [InlineData("trigger_once")]
    [InlineData("trigger_teleport")]
    public void A_trigger_that_starts_disabled_senses_nothing_until_it_is_enabled(string className)
    {
        SceneNode zone = Zone(className);
        zone.Entity!.SetValue("startdisabled", "1");
        EntityRuntime.Wire(zone, "OnStartTouch", "sink", "Start");
        _player.Feet = Inside;
        EntityWorld world = Play();

        for (int i = 0; i < 5; i++)
            world.Tick(Tick);
        _log.ShouldBeEmpty();

        world.Index!.TryGetByNodeId(zone.Id, out Entity? trigger).ShouldBeTrue();
        world.QueueInput(trigger!, "Enable");
        world.Tick(Tick);
        world.Tick(Tick);

        _log.ShouldBe(["sink:Start:"]);
    }

    [Fact]
    public void A_trigger_is_its_own_activator()
    {
        // The player is not an entity, so nothing else could be.
        SceneNode zone = Zone("trigger_multiple");
        EntityRuntime.Wire(zone, TriggerMultiple.OnStartTouch, TargetNameIndex.ActivatorToken, "Disable");
        _player.Feet = Inside;
        EntityWorld world = Play();

        world.Tick(Tick);

        EntityRuntime.Live<TriggerMultiple>(world, zone).IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void A_once_fires_on_its_first_touch_and_never_again()
    {
        SceneNode zone = Zone("trigger_once");
        EntityRuntime.Wire(zone, TriggerOnce.OnStartTouch, "sink", "Start");
        EntityRuntime.Wire(zone, TriggerOnce.OnTrigger, "sink", "Trigger");
        EntityWorld world = Play();
        TriggerOnce once = EntityRuntime.Live<TriggerOnce>(world, zone);

        for (int visit = 0; visit < 3; visit++)
            WalkInAndOut(world);

        _log.ShouldBe(["sink:Start:", "sink:Trigger:"]);
        once.TriggerCount.ShouldBe(1);
        once.IsEnabled.ShouldBeFalse();

        // Switched off, not removed.
        zone.Parent.ShouldBeSameAs(_scene.Root);
        world.Entities.ShouldContain(once);
    }

    [Fact]
    public void Enable_arms_a_spent_once_for_one_more_touch()
    {
        SceneNode zone = Zone("trigger_once");
        EntityRuntime.Wire(zone, TriggerOnce.OnTrigger, "sink", "Trigger");
        EntityWorld world = Play();
        TriggerOnce once = EntityRuntime.Live<TriggerOnce>(world, zone);
        WalkInAndOut(world);

        world.QueueInput(once, "Enable");
        world.Tick(Tick);
        for (int visit = 0; visit < 3; visit++)
            WalkInAndOut(world);

        _log.ShouldBe(["sink:Trigger:", "sink:Trigger:"]);
        once.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void A_teleport_puts_the_player_at_its_destination_facing_the_way_it_faces()
    {
        SceneNode zone = Zone("trigger_teleport");
        zone.Entity!.SetValue("target", "exit");
        EntityRuntime.Wire(zone, TriggerTeleport.OnStartTouch, "sink", "Start");
        EntityRuntime.Wire(zone, TriggerTeleport.OnEndTouch, "sink", "End");

        const float Turn = 0.6f;
        SceneNode exit = EntityRuntime.Place(_scene.Root, "exit", "info_teleport_destination");
        exit.LocalPosition = new Vector3(20f, 3f, -7f);
        exit.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Turn);

        _player.Feet = Inside;
        EntityWorld world = Play();
        world.Tick(Tick);

        _player.Teleports.Count.ShouldBe(1);
        _player.Feet.ShouldBe(new Vector3(20f, 3f, -7f));
        EntityRuntime.Live<TriggerTeleport>(world, zone).TeleportCount.ShouldBe(1);

        // The node's +Z turned by Turn about Y, as a command's yaw reads it.
        float? yaw = _player.Teleports[0].Yaw;
        yaw.ShouldNotBeNull();
        yaw.Value.ShouldBe(MathF.PI / 2f - Turn, 1e-5f);
        var facing = new Vector3(MathF.Cos(yaw.Value), 0f, MathF.Sin(yaw.Value));
        Vector3.Distance(facing, Vector3.Transform(Vector3.UnitZ, exit.LocalRotation)).ShouldBeLessThan(1e-5f);
        _log.ShouldBe(["sink:Start:"]);

        // Gone from the volume, which the next tick's pass sees.
        world.Tick(Tick);
        _log.ShouldBe(["sink:Start:", "sink:End:"]);
        _player.Teleports.Count.ShouldBe(1);
    }

    [Fact]
    public void A_destination_pointing_straight_up_leaves_the_facing_alone()
    {
        SceneNode zone = Zone("trigger_teleport");
        zone.Entity!.SetValue("target", "exit");
        SceneNode exit = EntityRuntime.Place(_scene.Root, "exit", "info_teleport_destination");
        exit.LocalPosition = new Vector3(20f, 0f, 0f);
        exit.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f);

        _player.Feet = Inside;
        EntityWorld world = Play();
        world.Tick(Tick);

        _player.Teleports.ShouldBe([(new Vector3(20f, 0f, 0f), (float?)null)]);
    }

    [Fact]
    public void A_teleport_uses_the_first_destination_in_scene_order()
    {
        SceneNode zone = Zone("trigger_teleport");
        zone.Entity!.SetValue("target", "exit");
        EntityRuntime.Place(_scene.Root, "exit", "info_teleport_destination").LocalPosition = new Vector3(20f, 0f, 0f);
        EntityRuntime.Place(_scene.Root, "exit", "info_teleport_destination").LocalPosition = new Vector3(40f, 0f, 0f);

        _player.Feet = Inside;
        EntityWorld world = Play();
        world.Tick(Tick);

        _player.Feet.ShouldBe(new Vector3(20f, 0f, 0f));
    }

    [Theory]
    [InlineData("nowhere")]
    [InlineData("")]
    public void A_teleport_with_no_destination_moves_nobody_and_warns_once(string target)
    {
        SceneNode zone = Zone("trigger_teleport");
        zone.Entity!.SetValue("target", target);
        EntityRuntime.Wire(zone, TriggerTeleport.OnStartTouch, "sink", "Start");
        EntityWorld world = Play();

        for (int visit = 0; visit < 3; visit++)
            WalkInAndOut(world);

        _player.Teleports.ShouldBeEmpty();
        _log.ShouldBe(["sink:Start:", "sink:Start:", "sink:Start:"]);
        _logger.MessagesAt(LogLevel.Warning)
            .Count(message => message.Contains("'zone'") && message.Contains("moves nobody"))
            .ShouldBe(1, _logger.Describe());
    }

    [Fact]
    public void A_character_walking_into_a_trigger_sets_off_what_it_is_wired_to()
    {
        Floor();
        SceneNode zone = Zone("trigger_once");
        zone.LocalPosition = new Vector3(4f, 1f, 0f);
        EntityRuntime.Wire(zone, TriggerOnce.OnTrigger, "sink", "Open");
        CharacterSimulation character = Walker();
        EntityWorld world = Play(character);

        // Yaw zero walks along +X, towards the volume's near face at 3.
        var walk = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f) };
        for (int i = 0; i < 240 && _log.Count == 0; i++)
        {
            world.Tick(Tick);
            character.Tick(in walk, Tick);
        }

        _log.ShouldBe(["sink:Open:"]);
        float reach = 3f - character.Tuning.Radius;
        character.State.Position.X.ShouldBeInRange(reach, reach + 0.3f);
    }

    [Fact]
    public void A_teleport_moves_a_walking_character_and_clears_its_ground()
    {
        Floor();
        SceneNode zone = Zone("trigger_teleport");
        zone.LocalPosition = new Vector3(4f, 1f, 0f);
        zone.Entity!.SetValue("target", "exit");
        SceneNode exit = EntityRuntime.Place(_scene.Root, "exit", "info_teleport_destination");
        exit.LocalPosition = new Vector3(-10f, 0f, 2f);
        CharacterSimulation character = Walker();
        EntityWorld world = Play(character);
        TriggerTeleport teleport = EntityRuntime.Live<TriggerTeleport>(world, zone);

        var walk = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f) };
        CharacterState before = default;
        for (int i = 0; i < 240; i++)
        {
            before = character.State;
            world.Tick(Tick);
            if (teleport.TeleportCount > 0)
                break;

            character.Tick(in walk, Tick);
        }

        before.Grounded.ShouldBeTrue();
        before.Velocity.X.ShouldBeGreaterThan(1f);

        character.State.Position.ShouldBe(new Vector3(-10f, 0f, 2f));
        character.State.Velocity.ShouldBe(Vector3.Zero);
        character.State.Grounded.ShouldBeFalse();
        character.State.GroundNodeId.ShouldBe(Guid.Empty);

        // An unturned destination faces +Z.
        character.TryTakeTeleport(out float? yaw).ShouldBeTrue();
        yaw.ShouldNotBeNull().ShouldBe(MathF.PI / 2f, 1e-5f);

        // Standing still, it finds the floor under the destination.
        for (int i = 0; i < 30; i++)
        {
            world.Tick(Tick);
            character.Tick(default, Tick);
        }

        character.State.Grounded.ShouldBeTrue();
        teleport.TeleportCount.ShouldBe(1);
        teleport.IsTouched.ShouldBeFalse();
    }

    [Fact]
    public void The_trigger_classes_declare_what_a_map_wires_against()
    {
        EntitySchema multiple = TriggerMultiple.SpectraSchema;
        multiple.ClassName.ShouldBe("trigger_multiple");
        multiple.Placement.ShouldBe(EntityPlacement.Volume);
        multiple.Keyvalues.Select(keyvalue => keyvalue.Name).ShouldBe(["startdisabled", "wait"]);
        multiple.Inputs.ShouldBe(["Enable", "Disable", "Toggle"]);
        multiple.Outputs.ShouldBe(
            [TriggerMultiple.OnStartTouch, TriggerMultiple.OnEndTouch, TriggerMultiple.OnTrigger]);

        KeyvalueDescriptor wait = multiple.Keyvalues[1];
        wait.Type.ShouldBe(KeyvalueType.Float);
        wait.Default.ShouldBe("1");
        wait.Min.ShouldBe(0f);
        new TriggerMultiple().Wait.ShouldBe(1f);

        EntitySchema once = TriggerOnce.SpectraSchema;
        once.ClassName.ShouldBe("trigger_once");
        once.Placement.ShouldBe(EntityPlacement.Volume);
        once.Keyvalues.Select(keyvalue => keyvalue.Name).ShouldBe(["startdisabled"]);
        once.Inputs.ShouldBe(["Enable", "Disable", "Toggle"]);
        once.Outputs.ShouldBe([TriggerOnce.OnStartTouch, TriggerOnce.OnTrigger]);

        EntitySchema teleport = TriggerTeleport.SpectraSchema;
        teleport.ClassName.ShouldBe("trigger_teleport");
        teleport.Placement.ShouldBe(EntityPlacement.Volume);
        teleport.Keyvalues.Select(keyvalue => keyvalue.Name).ShouldBe(["target", "startdisabled"]);
        teleport.Inputs.ShouldBe(["Enable", "Disable", "Toggle"]);
        teleport.Outputs.ShouldBe([TriggerTeleport.OnStartTouch, TriggerTeleport.OnEndTouch]);

        KeyvalueDescriptor target = teleport.Keyvalues[0];
        target.Type.ShouldBe(KeyvalueType.TargetName);
        target.Widget.ShouldBe(KeyvalueWidget.EntityPicker);

        EntitySchema destination = InfoTeleportDestination.SpectraSchema;
        destination.ClassName.ShouldBe("info_teleport_destination");
        destination.Placement.ShouldBe(EntityPlacement.Point);
        destination.Keyvalues.ShouldBeEmpty();
        destination.Inputs.ShouldBeEmpty();
        destination.Outputs.ShouldBeEmpty();

        foreach (EntitySchema schema in new[] { multiple, once, teleport, destination })
            schema.Group.ShouldBe("Triggers", schema.ClassName);
    }

    // A 2 by 2 by 2 volume standing on the ground at the origin, stamped the
    // way the editor stamps one: a part that is not drawn, not solid and
    // hidden from queries, with touch left on.
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

    // A part, so the character stands on it with no compile.
    private void Floor()
    {
        SceneNode floor = _scene.Root.CreateChild("floor");
        floor.LocalPosition = new Vector3(0f, -0.5f, 0f);
        floor.BrushKind = BrushKind.Part;
        floor.Brush = Brush.CreateBox(new Vector3(-20f, -0.5f, -6f), new Vector3(20f, 0.5f, 6f));
    }

    private CharacterSimulation Walker()
    {
        var character = new CharacterSimulation(_scene) { SpawnPosition = new Vector3(0f, 0.05f, 0f) };
        character.Spawn();
        return character;
    }

    private EntityWorld Play(IPlayerPresence? player = null)
    {
        EntityRuntime.Place(_scene.Root, "sink", "test_recorder");
        var world = new EntityWorld(_scene, _logger, EntityRuntime.Catalog(_log));
        world.Activate();
        world.Player = player ?? _player;
        return world;
    }

    private void WalkInAndOut(EntityWorld world)
    {
        _player.Feet = Inside;
        world.Tick(Tick);
        _player.Feet = Outside;
        world.Tick(Tick);
    }
}
