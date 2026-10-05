using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Walls through the presenter: what stands between a level's sound and the
/// listener reaches the audio device as a quieter, duller voice, and a sound
/// that can still be heard keeps its caption.
/// </summary>
// The listener is at the origin at ear height, and the sound 8 units down -z
// unless a test says otherwise.
public sealed class SoundPresenterWallTests
{
    private const float NearEnough = 2e-3f;

    private static readonly Vector3 Ear = new(0f, 1.5f, 0f);
    private static readonly Vector3 Behind = new(0f, 1.5f, -8f);
    private static readonly Vector3 HalfWay = new(0f, 1.5f, -4f);
    private static readonly Vector3 SheetOfWood = new(6f, 1.5f, 0.025f);
    private static readonly Vector3 DoorStart = new(3f, 1.5f, -5f);

    private static readonly float AtEight = SoundFalloff.Gain(8f, 2f, 30f);

    [Fact]
    public void A_sound_behind_a_wall_reaches_the_device_with_the_gain_and_high_end_the_wall_leaves()
    {
        using WalledSoundRig rig = Listening();
        rig.Level.Box("Wall", HalfWay, SheetOfWood, SpanLevel.Wood);
        rig.Level.Compile();
        rig.Sound.Play(rig.Sound.Place("speaker", Behind), SoundPresenterRig.Beep, SoundPresenterRig.Looped);

        rig.Step();

        AcousticGains wood = AcousticPresets.Wood.GainsThrough(0.05f);
        AudioSourceSettings voice = rig.Sound.OnlyVoice();
        voice.Position.ShouldBe(Behind);
        voice.Gain.ShouldBe(AtEight * wood.Gain, NearEnough);
        voice.GainHf.ShouldBe(wood.GainHf, NearEnough);
    }

    [Fact]
    public void With_nothing_in_the_way_the_voice_is_the_one_distance_alone_gives()
    {
        using WalledSoundRig rig = Listening();
        rig.Level.Box("Aside", new Vector3(20f, 1.5f, -4f), SheetOfWood, SpanLevel.Wood);
        rig.Level.Compile();
        rig.Sound.Play(rig.Sound.Place("speaker", Behind), SoundPresenterRig.Beep, SoundPresenterRig.Looped);

        rig.Step();

        rig.Sound.OnlyVoice().Gain.ShouldBe(AtEight);
        rig.Sound.OnlyVoice().GainHf.ShouldBe(1f);
    }

    [Fact]
    public void A_door_that_shuts_is_eased_in_by_the_smoother_and_ends_at_the_doors_numbers()
    {
        using WalledSoundRig rig = Listening();
        SceneNode door = rig.Level.Part("Door", HalfWay + new Vector3(4f, 0f, 0f), new Vector3(1f, 1.5f, 0.2f), SpanLevel.Wood);
        rig.Sound.Play(rig.Sound.Place("speaker", Behind), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Step();
        rig.Sound.OnlyVoice().Gain.ShouldBe(AtEight);

        door.LocalPosition = HalfWay;

        AcousticGains wood = AcousticPresets.Wood.GainsThrough(0.4f);
        float lastHigh = 1f;
        int framesBetween = 0;

        for (int frame = 0; frame < 150; frame++)
        {
            rig.Step();
            AudioSourceSettings voice = rig.Sound.OnlyVoice();

            (lastHigh - voice.GainHf).ShouldBeInRange(0f, SoundPathSmoother.MaxGainHfStep + 1e-6f);
            lastHigh = voice.GainHf;

            if (voice.Gain < AtEight * 0.99f && voice.Gain > AtEight * wood.Gain * 1.01f)
                framesBetween++;
        }

        framesBetween.ShouldBeGreaterThan(10);
        rig.Sound.OnlyVoice().Gain.ShouldBe(AtEight * wood.Gain, NearEnough);
        rig.Sound.OnlyVoice().GainHf.ShouldBe(wood.GainHf, NearEnough);
    }

    [Fact]
    public void A_sound_under_a_door_is_not_muffled_by_its_own_door()
    {
        using WalledSoundRig rig = Listening();
        SceneNode door = rig.Level.Part("Door", HalfWay, new Vector3(1f, 1.5f, 0.2f), SpanLevel.Wood);
        rig.Sound.Play(door.CreateChild("squeak"), SoundPresenterRig.Beep, SoundPresenterRig.Looped);

        rig.Step();

        rig.Sound.OnlyVoice().Gain.ShouldBe(SoundFalloff.Gain(4f, 2f, 30f));
        rig.Sound.OnlyVoice().GainHf.ShouldBe(1f);
    }

    [Fact]
    public void A_muffled_sound_that_can_still_be_heard_keeps_its_caption()
    {
        using WalledSoundRig rig = Listening();
        rig.Sound.WriteText($"{CaptionFile.Folder}/en{CaptionFile.FileExtension}", $"{SoundPresenterRig.Beep} = Beep sounds");
        rig.Sound.Captions.Mode = CaptionMode.All;
        rig.Level.Box("Wall", HalfWay, SheetOfWood with { Z = 0.5f }, WallRig.Concrete);
        rig.Level.Compile();
        rig.Sound.Play(rig.Sound.Place("speaker", Behind), SoundPresenterRig.Beep, SoundPresenterRig.Looped);

        rig.Step();

        // A metre of concrete takes all walls can take, and it is still heard.
        float heard = AtEight * AcousticPresets.Concrete.GainsThrough(1f).Gain;
        heard.ShouldBeInRange(SoundPresenter.SilenceGain, 0.01f);

        Caption caption = rig.Sound.Captions.Captions.ShouldHaveSingleItem();
        caption.Text.ShouldBe("Beep sounds");
        caption.Audibility.ShouldBe(heard, 1e-4f);
        rig.Sound.OnlyVoice().Gain.ShouldBe(heard, 1e-4f);
    }

    [Fact]
    public void A_sound_that_a_wall_takes_below_hearing_has_no_voice_and_no_caption()
    {
        using WalledSoundRig rig = Listening();
        rig.Sound.WriteText($"{CaptionFile.Folder}/en{CaptionFile.FileExtension}", $"{SoundPresenterRig.Beep} = Beep sounds");
        rig.Sound.Captions.Mode = CaptionMode.All;
        rig.Level.Box("Wall", HalfWay, SheetOfWood with { Z = 0.5f }, WallRig.Concrete);
        rig.Level.Compile();

        // Heard at this distance with nothing in the way, at a gain of 0.018.
        var far = new Vector3(0f, 1.5f, -24f);
        rig.Sound.Play(rig.Sound.Place("speaker", far), SoundPresenterRig.Beep, SoundPresenterRig.Looped);

        rig.Step(5);

        rig.Sound.Backend.PlayingSources().ShouldBeEmpty();
        rig.Sound.Captions.Captions.ShouldBeEmpty();
        rig.Sound.Stats.Silent.ShouldBe(1);
    }

    [Fact]
    public void A_wall_is_made_of_what_its_material_file_says()
    {
        using WalledSoundRig rig = Listening(materialFiles: true);
        rig.Sound.WriteText("Materials/span_wood.spectramat", "shader = lit\nacoustic = metal\n");
        rig.Level.Box("Wall", HalfWay, SheetOfWood, SpanLevel.Wood);
        rig.Level.Compile();
        rig.Sound.Play(rig.Sound.Place("speaker", Behind), SoundPresenterRig.Beep, SoundPresenterRig.Looped);

        rig.Step();

        AcousticGains metal = AcousticPresets.Metal.GainsThrough(0.05f);
        rig.Sound.OnlyVoice().Gain.ShouldBe(AtEight * metal.Gain, NearEnough);
        rig.Sound.OnlyVoice().GainHf.ShouldBe(metal.GainHf, NearEnough);
    }

    [Fact]
    public void A_material_file_that_arrives_late_is_heard_once_the_engine_gives_the_material_another_try()
    {
        const string path = "Materials/span_late.spectramat";
        var late = SpectraEngine.Core.Assets.MaterialRegistry.Intern(path);

        using WalledSoundRig rig = Listening(materialFiles: true);
        rig.Level.Box("Wall", HalfWay, SheetOfWood, late);
        rig.Level.Compile();
        rig.Sound.Play(rig.Sound.Place("speaker", Behind), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Step();
        rig.Sound.OnlyVoice().GainHf.ShouldBe(AcousticPresets.Generic.GainsThrough(0.05f).GainHf, NearEnough);

        rig.Sound.WriteText(path, "acoustic = fabric\n");
        rig.Sound.Assets.ForgetFailedMaterial(path);
        rig.Step(120);

        rig.Sound.OnlyVoice().GainHf.ShouldBe(AcousticPresets.Fabric.GainsThrough(0.05f).GainHf, NearEnough);
    }

    [Fact]
    public void Two_hundred_sounds_behind_walls_on_thirty_two_sources_allocate_nothing_a_frame_once_running()
    {
        using WalledSoundRig rig = Listening(sources: 32);
        rig.Level.Box("Wall", HalfWay, SheetOfWood, SpanLevel.Wood);
        rig.Level.Box("Pillar", new Vector3(-3f, 1.5f, -6f), new Vector3(0.5f, 1.5f, 0.5f), SpanLevel.Brick);
        rig.Level.Compile();
        SceneNode door = rig.Level.Part("Door", DoorStart, new Vector3(1f, 1.5f, 0.2f), SpanLevel.Wood);

        // Thirty-two in the open that stay the loudest, so no source changes
        // hands: a voice that starts is not a cost per frame.
        for (int i = 0; i < 32; i++)
        {
            SceneNode speaker = rig.Sound.Place($"near{i}", new Vector3((i % 8) - 4, 1.5f, -1f - (i / 8 * 0.25f)));
            rig.Sound.Play(speaker, SoundPresenterRig.Beep, SoundPresenterRig.Looped with { MinDistance = 12f });
        }

        // The rest behind the wall, every fourth one riding on the door.
        for (int i = 0; i < 168; i++)
        {
            var place = new Vector3((i % 20) - 10, 1.5f, -8f - (i / 20));
            SceneNode speaker = i % 4 == 0 ? door.CreateChild($"far{i}") : rig.Sound.Place($"far{i}", place);
            if (i % 4 == 0)
                speaker.LocalPosition = place - DoorStart;

            rig.Sound.Play(speaker, i % 3 == 0 ? SoundPresenterRig.Music : SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        }

        rig.Sound.Backend.KeepsUploads = false;
        Walk(rig, door, 120);
        rig.Sound.Stats.WithSource.ShouldBe(32);
        rig.Walls.Stats.Sounds.ShouldBe(200);

        // The least of several rounds: a one-off from the runtime is not a
        // cost per frame.
        long least = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Walk(rig, door, 100);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        least.ShouldBe(0L);
        rig.Walls.Stats.Traces.ShouldBe(rig.Walls.Settings.TracesPerFrame);
        rig.Sound.Stats.WithSource.ShouldBe(32);
    }

    // A tick and a frame each, with the listener walking and the door
    // sliding, so every answer is due all the time.
    private static void Walk(WalledSoundRig rig, SceneNode door, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            rig.Sound.Listen(Ear + new Vector3((i % 40) * 0.075f, 0f, 0f));
            door.LocalPosition = DoorStart + new Vector3((i % 30) * 0.03f, 0f, 0f);
            rig.Sound.Backend.ConsumeOneEverywhere();
            rig.Step();
        }
    }

    private static WalledSoundRig Listening(int sources = AudioManager.DefaultSourceCount, bool materialFiles = false)
    {
        var rig = new WalledSoundRig(sources, materialFiles);
        rig.Sound.Listen(Ear);
        return rig;
    }
}
