using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// The demo's start room heard through its walls: the room's air behind its
/// door, and the door's and the lift's own sounds, which nothing of theirs
/// may muffle.
/// </summary>
// The rig builds the room with no materials, so every wall and the door are
// of the generic kind. The door is 0.8 thick in a wall of 1.
public sealed class DemoStartRoomWallTests : IClassFixture<CookedDemoSounds>
{
    // In the room, and on the course two units outside its door.
    private static readonly Vector3 Inside = new(127f, 1.62f, 0f);
    private static readonly Vector3 Outside = new(133f, 1.62f, 0f);

    private readonly CookedDemoSounds _cooked;

    public DemoStartRoomWallTests(CookedDemoSounds cooked) => _cooked = cooked;

    [Fact]
    public void The_rooms_air_is_clear_in_the_room_and_muffled_outside_its_shut_door()
    {
        using var rig = new StartRoomSoundRig(_cooked, walls: true);
        Vector3 tone = rig.Node(DemoPlayArea.RoomToneName).WorldPosition;
        rig.Session.Enter();

        rig.PresentFrom(Inside);
        rig.SourcesAt(tone).ShouldHaveSingleItem().Gain.ShouldBe(1f);
        rig.SourcesAt(tone).ShouldHaveSingleItem().GainHf.ShouldBe(1f);

        Listen(rig, Outside, ticks: 150);

        AcousticGains door = AcousticPresets.Generic.GainsThrough(0.8f);
        float reach = SoundFalloff.Gain(Vector3.Distance(tone, Outside), 6f, 16f);
        AudioSourceSettings heard = rig.SourcesAt(tone).ShouldHaveSingleItem();
        heard.Gain.ShouldBe(reach * door.Gain, 0.002f);
        heard.GainHf.ShouldBe(door.GainHf, 0.005f);
    }

    [Fact]
    public void The_rooms_air_comes_clear_through_the_open_doorway_and_goes_dull_when_the_door_shuts()
    {
        using var rig = new StartRoomSoundRig(_cooked, walls: true);
        Vector3 tone = rig.Node(DemoPlayArea.RoomToneName).WorldPosition;
        rig.Session.Enter();
        FuncDoor door = rig.Live<FuncDoor>(DemoPlayArea.StartDoorName);
        Listen(rig, Outside, ticks: 150);
        rig.SourcesAt(tone).ShouldHaveSingleItem().GainHf.ShouldBeLessThan(0.1f);

        // It stays open for three seconds.
        EntityRuntime.Send(door, "Open");
        Listen(rig, Outside, ticks: 150);

        door.IsFullyOpen.ShouldBeTrue();
        float reach = SoundFalloff.Gain(Vector3.Distance(tone, Outside), 6f, 16f);
        rig.SourcesAt(tone).ShouldHaveSingleItem().Gain.ShouldBe(reach, 1e-3f);
        rig.SourcesAt(tone).ShouldHaveSingleItem().GainHf.ShouldBe(1f, 1e-3f);

        Listen(rig, Outside, ticks: 300);

        door.IsFullyClosed.ShouldBeTrue();
        rig.SourcesAt(tone).ShouldHaveSingleItem().GainHf.ShouldBeLessThan(0.1f);
    }

    [Theory]
    [InlineData(127f)]
    [InlineData(133f)]
    public void The_doors_own_sound_is_clear_while_the_door_slides_into_the_wall(float earX)
    {
        using var rig = new StartRoomSoundRig(_cooked, walls: true);
        SceneNode sound = rig.Node(DemoPlayArea.DoorOpenSoundName);
        var ear = new Vector3(earX, 1.62f, 0f);
        rig.Session.Enter();
        FuncDoor door = rig.Live<FuncDoor>(DemoPlayArea.StartDoorName);
        Listen(rig, ear, ticks: 5);

        EntityRuntime.Send(door, "Open");
        int heard = 0;

        // The sound is a second long and the door is in the wall after half of it.
        for (int tick = 0; tick < 55; tick++)
        {
            Listen(rig, ear, ticks: 1);

            foreach (AudioSourceSettings voice in rig.SourcesAt(sound.WorldPosition))
            {
                // The smoother is a little behind the distance while the door moves.
                float reach = SoundFalloff.Gain(Vector3.Distance(sound.WorldPosition, ear), 3f, 20f);
                voice.GainHf.ShouldBe(1f, $"tick {tick}, door at {door.TicksTravelled} of {door.TravelTicks}");
                voice.Gain.ShouldBe(reach, 0.03f, $"tick {tick}");
                heard++;
            }
        }

        door.IsFullyOpen.ShouldBeTrue();
        heard.ShouldBeGreaterThan(40);
    }

    [Fact]
    public void The_lifts_hum_is_not_muffled_by_the_lift_for_someone_riding_it()
    {
        using var rig = new StartRoomSoundRig(_cooked, walls: true);
        SceneNode hum = rig.Node(DemoPlayArea.LiftMoveSoundName);
        rig.Session.Enter();
        rig.PressTheLiftButton();
        int heard = 0;

        for (int tick = 0; tick < 240; tick++)
        {
            rig.Tick(default);
            if (!rig.IsPlaying(DemoPlayArea.LiftMoveSoundName))
                continue;

            foreach (AudioSourceSettings voice in rig.SourcesAt(hum.WorldPosition))
            {
                voice.GainHf.ShouldBe(1f, $"tick {tick}");
                heard++;
            }
        }

        heard.ShouldBeGreaterThan(60);
    }

    // The level runs on and the listener stands at one place, wherever the
    // player is.
    private static void Listen(StartRoomSoundRig rig, Vector3 ear, int ticks)
    {
        CharacterCommand still = default;

        for (int i = 0; i < ticks; i++)
        {
            rig.Session.Tick(StartRoomSoundRig.Dt, in still);
            rig.PresentFrom(ear);
        }
    }
}
