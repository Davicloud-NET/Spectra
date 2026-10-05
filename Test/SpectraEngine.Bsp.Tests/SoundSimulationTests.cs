using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a sound has switched off, and what the engine has switched off for
/// every sound: placed, fades, walls and Doppler, each by itself.
/// </summary>
public sealed class SoundSimulationTests
{
    private const string Beep = SoundPresenterRig.Beep;

    private static readonly Vector3 Away = new(0, 0, -8);

    [Fact]
    public void A_sound_has_everything_on_unless_it_says_otherwise()
    {
        SoundPresenterRig.Once.Simulated.ShouldBe(SoundSimulation.All);
        new SoundQuery(Away, 2f, 30f).Simulated.ShouldBe(SoundSimulation.All);
        new SoundSimulationSwitches().Enabled.ShouldBe(SoundSimulation.All);

        using var rig = new SoundPresenterRig();
        int id = rig.Play(rig.Scene.Root, Beep, SoundPresenterRig.Once);

        rig.World.Sounds.TryGet(id, out SoundEmitter emitter).ShouldBeTrue();
        emitter.Simulated.ShouldBe(SoundSimulation.All);
    }

    [Fact]
    public void A_sound_that_is_not_placed_plays_at_the_listener_and_still_fades()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("speaker", Away), Beep, Without(SoundSimulation.Placed));

        rig.Frame();

        AudioSourceSettings voice = rig.OnlyVoice();
        voice.Relative.ShouldBeTrue();
        voice.Position.ShouldBe(Vector3.Zero);
        voice.Gain.ShouldBe(SoundFalloff.Gain(8f, 2f, 30f));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_caption_of_a_sound_has_a_place_only_when_the_sound_is_placed(bool isPlaced)
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Beep} = Beep sounds");
        SoundEmitterSettings settings = isPlaced ? SoundPresenterRig.Looped : Without(SoundSimulation.Placed);
        rig.Play(Beep, rig.Place(new Vector3(3, 0, -1)), settings);

        rig.Step(2);

        Vector3? place = isPlaced ? new Vector3(3, 0, -1) : null;
        Caption caption = rig.Shown.ShouldHaveSingleItem();
        caption.Position.ShouldBe(place);
        caption.Audibility.ShouldBeGreaterThan(0f);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(30f)]
    [InlineData(500f)]
    public void A_sound_that_does_not_fade_is_as_loud_at_any_distance_as_up_close(float distance)
    {
        using var rig = new SoundPresenterRig();
        var place = new Vector3(0, 0, -distance);
        rig.Play(rig.Place("alarm", place), Beep, Without(SoundSimulation.Fades));

        rig.Frame();

        AudioSourceSettings voice = rig.OnlyVoice();
        voice.Gain.ShouldBe(1f);
        voice.Relative.ShouldBeFalse();
        voice.Position.ShouldBe(place);
    }

    [Fact]
    public void A_sound_that_fades_is_silent_at_its_far_distance()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("speaker", new Vector3(0, 0, -30)), Beep, SoundPresenterRig.Looped);

        rig.Frame();

        rig.Backend.PlayingSources().ShouldBeEmpty();
        rig.Stats.Silent.ShouldBe(1);
    }

    [Fact]
    public void A_sound_can_fade_and_not_be_placed_or_be_placed_and_not_fade()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("hum", Away), Beep, Without(SoundSimulation.Placed));
        rig.Play(rig.Place("alarm", Away * 2), Beep, Without(SoundSimulation.Fades));

        rig.Frame();

        AudioSourceSettings[] voices = [.. rig.Backend.PlayingSources().Select(rig.Backend.SettingsOf)];
        AudioSourceSettings hum = voices.Single(voice => voice.Relative);
        AudioSourceSettings alarm = voices.Single(voice => !voice.Relative);
        hum.Gain.ShouldBe(SoundFalloff.Gain(8f, 2f, 30f));
        alarm.Gain.ShouldBe(1f);
        alarm.Position.ShouldBe(Away * 2);
    }

    [Fact]
    public void A_stereo_file_is_not_placed_even_with_its_switch_on()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("radio", Away), SoundPresenterRig.Music, SoundPresenterRig.Looped);

        rig.Frame(2);

        SoundPresenterRig.Looped.Simulated.HasFlag(SoundSimulation.Placed).ShouldBeTrue();
        AudioSourceSettings voice = rig.OnlyVoice();
        voice.Relative.ShouldBeTrue();
        voice.Position.ShouldBe(Vector3.Zero);
    }

    [Theory]
    [InlineData(SoundSimulation.Placed)]
    [InlineData(SoundSimulation.Fades)]
    [InlineData(SoundSimulation.Walls)]
    [InlineData(SoundSimulation.Doppler)]
    public void What_a_sound_has_switched_off_reaches_the_propagation(SoundSimulation part)
    {
        var propagation = new RecordingPropagation();
        using var rig = new SoundPresenterRig(propagation: propagation);
        rig.Play(rig.Place("plain", Away), Beep, SoundPresenterRig.Looped);
        rig.Play(rig.Place("switched", Away), Beep, Without(part));

        rig.Frame();

        propagation.Asked.Count.ShouldBe(2);
        propagation.Asked[0].Simulated.ShouldBe(SoundSimulation.All);
        propagation.Asked[1].Simulated.ShouldBe(SoundSimulation.All & ~part);
    }

    [Theory]
    [InlineData(SoundSimulation.Placed)]
    [InlineData(SoundSimulation.Fades)]
    [InlineData(SoundSimulation.Walls)]
    [InlineData(SoundSimulation.Doppler)]
    public void A_part_the_engine_has_off_is_off_for_a_sound_that_has_it_on(SoundSimulation part)
    {
        var propagation = new RecordingPropagation();
        using var rig = new SoundPresenterRig(propagation: propagation);
        rig.Play(rig.Place("speaker", Away), Beep, SoundPresenterRig.Looped);

        rig.Presenter.Simulation.Set(part, isOn: false);
        rig.Frame();
        propagation.Asked.ShouldHaveSingleItem().Simulated.ShouldBe(SoundSimulation.All & ~part);

        rig.Presenter.Simulation.Set(part, isOn: true);
        rig.Frame();
        propagation.Asked.ShouldHaveSingleItem().Simulated.ShouldBe(SoundSimulation.All);
    }

    [Theory]
    [InlineData(SoundSimulation.Placed)]
    [InlineData(SoundSimulation.Fades)]
    [InlineData(SoundSimulation.Walls)]
    [InlineData(SoundSimulation.Doppler)]
    public void A_part_the_engine_has_on_stays_off_for_a_sound_that_has_it_off(SoundSimulation part)
    {
        var propagation = new RecordingPropagation();
        using var rig = new SoundPresenterRig(propagation: propagation);
        rig.Play(rig.Place("speaker", Away), Beep, Without(part));

        rig.Presenter.Simulation.Set(SoundSimulation.All, isOn: true);
        rig.Frame();

        propagation.Asked.ShouldHaveSingleItem().Simulated.ShouldBe(SoundSimulation.All & ~part);
    }

    [Fact]
    public void With_placed_off_in_the_engine_every_sound_plays_at_the_listener()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("speaker", Away), Beep, SoundPresenterRig.Looped);
        rig.Frame();
        uint source = rig.Backend.PlayingSources().ShouldHaveSingleItem();
        rig.OnlyVoice().Relative.ShouldBeFalse();

        rig.Presenter.Simulation.Set(SoundSimulation.Placed, isOn: false);
        rig.Frame();

        // The same voice, moved. Nothing was started again.
        rig.Backend.PlayingSources().ShouldBe([source]);
        rig.OnlyVoice().Relative.ShouldBeTrue();
        rig.OnlyVoice().Position.ShouldBe(Vector3.Zero);

        rig.Presenter.Simulation.Set(SoundSimulation.Placed, isOn: true);
        rig.Frame();

        rig.Backend.PlayingSources().ShouldBe([source]);
        rig.OnlyVoice().Relative.ShouldBeFalse();
        rig.OnlyVoice().Position.ShouldBe(Away);
    }

    [Fact]
    public void With_fades_off_in_the_engine_a_sound_out_of_earshot_comes_up_to_full_volume()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("speaker", new Vector3(0, 0, -90)), Beep, SoundPresenterRig.Looped);
        rig.Frame();
        rig.Backend.PlayingSources().ShouldBeEmpty();

        rig.Presenter.Simulation.Set(SoundSimulation.Fades, isOn: false);
        rig.Frame();

        // Eased in like any other change of loudness, so the switch does not click.
        rig.OnlyVoice().Gain.ShouldBeInRange(SoundPresenter.SilenceGain, 0.5f);

        rig.Frame(120);

        rig.OnlyVoice().Gain.ShouldBe(1f);
    }

    [Fact]
    public void The_switches_are_one_set_for_each_presenter()
    {
        using var rig = new SoundPresenterRig();
        var other = new SoundPresenter(
            rig.Audio, rig.Assets, new DirectPropagation(), rig.NewCaptionFeed(), rig.Log);

        rig.Presenter.Simulation.Set(SoundSimulation.All, isOn: false);

        other.Simulation.Enabled.ShouldBe(SoundSimulation.All);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(4.5f)]
    [InlineData(float.NaN)]
    public void A_doppler_strength_outside_0_to_4_is_refused(float strength)
    {
        var switches = new SoundSimulationSwitches();

        Should.Throw<ArgumentOutOfRangeException>(() => switches.DopplerStrength = strength);

        switches.DopplerStrength.ShouldBe(1f);
    }

    private static SoundEmitterSettings Without(SoundSimulation part) =>
        SoundPresenterRig.Looped with { Simulated = SoundSimulation.All & ~part };
}
