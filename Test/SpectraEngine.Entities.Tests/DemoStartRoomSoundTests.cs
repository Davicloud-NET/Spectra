using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// The demo's start room played with its sounds cooked and no sound card:
/// what plays when, and that a sound under a mover goes where the mover goes.
/// </summary>
public sealed class DemoStartRoomSoundTests : IClassFixture<CookedDemoSounds>
{
    private const int TicksASecond = 60;

    // How far the door slides: its width of 1.4 less its lip.
    private const float DoorSlide = 1.35f;

    private static readonly CharacterCommand East = new() { MoveForward = CharacterCommand.Axis(1f), Yaw = 0f };

    private readonly CookedDemoSounds _cooked;

    public DemoStartRoomSoundTests(CookedDemoSounds cooked) => _cooked = cooked;

    // Where the door was and what its sound reported, after one tick of sliding.
    private readonly record struct Slide(Vector3 Door, Vector3 Emitter, int SourcesAtTheDoor);

    [Fact]
    public void The_cook_has_nothing_to_say_about_the_sounds()
    {
        _cooked.Cook.Cooked.ShouldBeGreaterThan(0);
        _cooked.Cook.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Every_sound_in_the_start_room_names_a_file_in_the_sounds_folder()
    {
        List<EntityData> sounds = SoundsOfTheStartRoom();

        sounds.Count.ShouldBe(6);
        foreach (EntityData sound in sounds)
        {
            sound.TryGetValue("sound", out string path).ShouldBeTrue();

            Path.GetDirectoryName(path).ShouldBe("Sounds");
            File.Exists(ContentRoot.ResolveAbsolute(ContentRoot.Path, path)).ShouldBeTrue(path);
        }
    }

    [Fact]
    public void A_looped_sound_in_the_start_room_repeats_its_whole_file()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        var catalog = new AssetSoundCatalog(rig.Assets);
        int looped = 0;

        foreach (EntityData sound in SoundsOfTheStartRoom())
        {
            if (!sound.TryGetValue("looped", out string value) || value != "1")
                continue;

            sound.TryGetValue("sound", out string path).ShouldBeTrue();
            catalog.TryDescribe(path, out SoundDescription described, out string reason).ShouldBeTrue(reason);

            described.Loop.ShouldBe(new LoopRegion(0, described.FrameCount), path);
            looped++;
        }

        looped.ShouldBe(2);
    }

    [Fact]
    public void The_room_tone_plays_from_the_start_and_is_as_loud_in_every_corner()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        Vector3 tone = rig.Node(DemoPlayArea.RoomToneName).WorldPosition;
        rig.Session.Enter();

        rig.TryGetEmitter(DemoPlayArea.RoomToneName, out SoundEmitter emitter).ShouldBeTrue();
        emitter.StartTick.ShouldBe(0L);
        emitter.IsLooped.ShouldBeTrue();

        // Ears in the four corners of a room that is 8 by 8.
        foreach ((float x, float z) in new[] { (122.4f, -3.6f), (122.4f, 3.6f), (129.6f, -3.6f), (129.6f, 3.6f) })
        {
            rig.PresentFrom(new Vector3(x, 1.62f, z));
            rig.SourcesAt(tone).ShouldHaveSingleItem().Gain.ShouldBe(1f);
        }
    }

    [Fact]
    public void The_doors_sound_is_where_the_door_is_on_every_tick_the_door_slides()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        Vector3 built = rig.Node(DemoPlayArea.DoorOpenSoundName).WorldPosition;
        built.ShouldBe(rig.Node(DemoPlayArea.StartDoorName).WorldPosition);
        rig.Session.Enter();

        List<Slide> slides = WalkUntilTheDoorIsOpen(rig);

        slides.Count.ShouldBe(rig.Live<FuncDoor>(DemoPlayArea.StartDoorName).TravelTicks);
        for (int i = 0; i < slides.Count; i++)
        {
            slides[i].Emitter.ShouldBe(slides[i].Door, $"tick {i + 1} of the slide");

            Vector3 expected = built + new Vector3(0f, 0f, DoorSlide * (i + 1) / slides.Count);
            Vector3.Distance(slides[i].Emitter, expected).ShouldBeLessThan(1e-5f, $"tick {i + 1} of the slide");
        }
    }

    [Fact]
    public void The_device_is_told_where_the_door_is_on_every_tick_the_door_slides()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        rig.Session.Enter();

        List<Slide> slides = WalkUntilTheDoorIsOpen(rig);

        // The room tone has a source too, and it is not at the door.
        slides.ShouldNotBeEmpty();
        slides.ShouldAllBe(slide => slide.SourcesAtTheDoor == 1);
        rig.Log.MessagesAt(LogLevel.Warning).ShouldBeEmpty();
    }

    [Fact]
    public void Stop_puts_the_doors_sound_back_where_it_was_built_and_frees_its_source()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        SceneNode sound = rig.Node(DemoPlayArea.DoorOpenSoundName);
        Vector3 built = sound.WorldPosition;
        rig.Session.Enter();
        EntityWorld world = rig.World;
        WalkUntilTheDoorIsOpen(rig);
        (sound.WorldPosition.Z - built.Z).ShouldBe(DoorSlide, 1e-5f);

        rig.Session.Exit();
        rig.Present();

        sound.WorldPosition.ShouldBe(built);
        world.Sounds.Count.ShouldBe(0);
        rig.Backend.PlayingSources().ShouldBeEmpty();
    }

    [Fact]
    public void The_doors_sound_ends_on_the_tick_its_length_says()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        rig.Session.Enter();
        WalkUntilTheDoorIsOpen(rig);
        rig.TryGetEmitter(DemoPlayArea.DoorOpenSoundName, out SoundEmitter emitter).ShouldBeTrue();

        // One second long.
        emitter.FrameCount.ShouldBe(emitter.SampleRate);
        long end = emitter.StartTick + TicksASecond;

        while (rig.World.TickNumber < end - 1)
            rig.Tick(default);
        rig.IsPlaying(DemoPlayArea.DoorOpenSoundName).ShouldBeTrue();

        rig.Tick(default);

        rig.IsPlaying(DemoPlayArea.DoorOpenSoundName).ShouldBeFalse();
        rig.Outputs.Fired.ShouldContain($"{end}:{DemoPlayArea.DoorOpenSoundName}.{PointSound.OnEnded}");
    }

    [Fact]
    public void A_door_turned_round_halfway_plays_only_the_sound_of_the_way_it_now_goes()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        rig.Session.Enter();
        FuncDoor door = rig.Live<FuncDoor>(DemoPlayArea.StartDoorName);
        while (door.TicksTravelled < 10)
            rig.Tick(in East);

        // The wire from OnClose is delivered on the next tick.
        EntityRuntime.Send(door, "Close");
        rig.Tick(default);

        rig.IsPlaying(DemoPlayArea.DoorOpenSoundName).ShouldBeFalse();
        rig.IsPlaying(DemoPlayArea.DoorCloseSoundName).ShouldBeTrue();
    }

    [Fact]
    public void The_lift_hums_on_every_tick_it_moves_on_the_way_up_and_on_the_way_down()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        rig.Session.Enter();
        FuncMoveLinear lift = rig.Live<FuncMoveLinear>(DemoPlayArea.LiftName);
        PressTheLiftButton(rig);

        int moved = 0;
        bool wasUp = false;
        for (int i = 0; i < 600 && !(wasUp && lift.TicksTravelled == 0); i++)
        {
            int before = lift.TicksTravelled;
            rig.Tick(default);
            wasUp |= lift.TicksTravelled == lift.TravelTicks;

            if (lift.TicksTravelled == before)
                continue;

            moved++;
            rig.IsPlaying(DemoPlayArea.LiftMoveSoundName).ShouldBeTrue($"at {lift.TicksTravelled} of {lift.TravelTicks}");
        }

        moved.ShouldBe(2 * lift.TravelTicks);
    }

    [Fact]
    public void The_hum_stops_and_the_clunk_plays_the_tick_after_the_lift_arrives()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        rig.Session.Enter();
        FuncMoveLinear lift = rig.Live<FuncMoveLinear>(DemoPlayArea.LiftName);
        PressTheLiftButton(rig);
        for (int i = 0; i < 300 && lift.TicksTravelled < lift.TravelTicks; i++)
            rig.Tick(default);

        // It fires OnFullyOpen from its own tick, so the wires land on the next.
        rig.Tick(default);

        rig.IsPlaying(DemoPlayArea.LiftMoveSoundName).ShouldBeFalse();
        rig.IsPlaying(DemoPlayArea.LiftStopSoundName).ShouldBeTrue();

        // Quiet for the rest of its three seconds at the top.
        for (int i = 0; i < 170; i++)
        {
            rig.Tick(default);
            rig.IsPlaying(DemoPlayArea.LiftMoveSoundName).ShouldBeFalse($"{i + 2} ticks after it arrived");
        }

        lift.TicksTravelled.ShouldBe(lift.TravelTicks);
    }

    // Walks at the door until it has slid open. One entry for each tick it slid on.
    private static List<Slide> WalkUntilTheDoorIsOpen(StartRoomSoundRig rig)
    {
        SceneNode node = rig.Node(DemoPlayArea.StartDoorName);
        FuncDoor door = rig.Live<FuncDoor>(DemoPlayArea.StartDoorName);
        var slides = new List<Slide>();

        for (int i = 0; i < 300 && !door.IsFullyOpen; i++)
        {
            rig.Tick(in East);
            if (door.IsFullyClosed)
                continue;

            rig.TryGetEmitter(DemoPlayArea.DoorOpenSoundName, out SoundEmitter emitter)
                .ShouldBeTrue($"the door is at {door.TicksTravelled} of {door.TravelTicks}");

            slides.Add(new Slide(node.WorldPosition, emitter.Node.WorldPosition, rig.SourcesAt(node.WorldPosition).Count));
        }

        door.IsFullyOpen.ShouldBeTrue();
        return slides;
    }

    // Stands on the lift and uses the button, which is on the wall to its south.
    private static void PressTheLiftButton(StartRoomSoundRig rig)
    {
        rig.Character.Teleport(new Vector3(123f, 0.35f, -3f));
        rig.Idle(30);

        var press = new CharacterCommand
        {
            Yaw = MathF.Atan2(1.5f, -0.85f),
            Buttons = CharacterButtons.Use,
        };
        rig.Tick(in press);
    }

    private static List<EntityData> SoundsOfTheStartRoom()
    {
        var scene = new Scene("StartRoom");
        DemoPlayArea.Build(scene, MaterialRef.Default, MaterialRef.Default, MaterialRef.Default);

        var sounds = new List<EntityData>();
        Collect(scene.Root, sounds);
        return sounds;
    }

    private static void Collect(SceneNode node, List<EntityData> sounds)
    {
        if (node.Entity is { ClassName: "point_sound" } sound)
            sounds.Add(sound);

        foreach (SceneNode child in node.Children)
            Collect(child, sounds);
    }
}
