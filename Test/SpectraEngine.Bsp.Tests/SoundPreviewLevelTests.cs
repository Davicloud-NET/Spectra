using SpectraEngine.Core.Audio;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A preview beside a running level: neither takes a source from the other,
/// and the level ending leaves the preview playing.
/// </summary>
public sealed class SoundPreviewLevelTests
{
    [Fact]
    public void A_level_on_every_source_keeps_its_voices_when_a_preview_starts()
    {
        using var rig = new SoundPresenterRig(sources: 4, spareSources: 1);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        FillEverySource(rig);

        preview.Play(SoundPresenterRig.Speech, out string refusal).ShouldBeTrue(refusal);
        rig.Frame(5);

        preview.Path.ShouldBe(SoundPresenterRig.Speech);
        rig.Backend.PlayingSources().Length.ShouldBe(5);
        rig.Audio.StolenVoiceCount.ShouldBe(0);
        rig.Audio.DroppedVoiceCount.ShouldBe(0);
        rig.Stats.ShouldBe(new SoundStats(Playing: 4, WithSource: 4, WithoutSource: 0, RefusedStarts: 0, Unplayable: 0));
    }

    [Fact]
    public void A_full_pool_takes_a_source_from_its_own_sounds_and_never_from_the_preview()
    {
        using var rig = new SoundPresenterRig(sources: 2, spareSources: 1);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        preview.Play(SoundPresenterRig.Speech, out _).ShouldBeTrue();
        AudioClip click = rig.Audio.CreateClip(new AudioFormat(SoundPresenterRig.Rate, 1), new short[600]).ShouldNotBeNull();

        for (int i = 0; i < 5; i++)
            rig.Audio.Play(click).ShouldNotBeNull();

        rig.Audio.StolenVoiceCount.ShouldBe(3);
        rig.Audio.Preview.IsPlaying.ShouldBeTrue();
        rig.Backend.PlayingSources().Length.ShouldBe(3);

        rig.Audio.Update();
        preview.Update();
        preview.Path.ShouldBe(SoundPresenterRig.Speech);
    }

    [Fact]
    public void With_no_source_left_on_the_device_the_preview_is_refused_and_the_level_plays_on()
    {
        using var rig = new SoundPresenterRig(sources: 4);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        FillEverySource(rig);

        preview.Play(SoundPresenterRig.Speech, out string refusal).ShouldBeFalse();
        rig.Frame(5);

        refusal.ShouldBe("the audio device has no source left for it");
        preview.Path.ShouldBeEmpty();
        rig.Backend.PlayingSources().Length.ShouldBe(4);
        rig.Audio.StolenVoiceCount.ShouldBe(0);
        rig.Stats.ShouldBe(new SoundStats(Playing: 4, WithSource: 4, WithoutSource: 0, RefusedStarts: 0, Unplayable: 0));
    }

    [Fact]
    public void The_level_ending_leaves_the_preview_playing()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        rig.Play(rig.Place("hum", new Vector3(0, 0, -1)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();
        preview.Play(SoundPresenterRig.Speech, out _).ShouldBeTrue();
        rig.Backend.PlayingSources().Length.ShouldBe(2);

        rig.Presenter.Update(null, SoundPresenterRig.TickSeconds);
        rig.Audio.Update();
        preview.Update();

        preview.Path.ShouldBe(SoundPresenterRig.Speech);
        rig.Audio.ActiveVoiceCount.ShouldBe(0);
        rig.OnlyVoice().Relative.ShouldBeTrue();
    }

    [Fact]
    public void Stopping_a_preview_leaves_the_levels_voices_alone()
    {
        using var rig = new SoundPresenterRig(sources: 4, spareSources: 1);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        FillEverySource(rig);
        preview.Play(SoundPresenterRig.Speech, out _).ShouldBeTrue();

        preview.Stop().ShouldBeTrue();
        rig.Frame(2);

        rig.Backend.PlayingSources().Length.ShouldBe(4);
        rig.Stats.WithSource.ShouldBe(4);
    }

    // Two loops and two sounds that play once, all in earshot: one voice a source.
    private static void FillEverySource(SoundPresenterRig rig)
    {
        rig.Play(rig.Place("a", new Vector3(0, 0, -1)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Play(rig.Place("b", new Vector3(0, 0, -2)), SoundPresenterRig.Music, SoundPresenterRig.Looped);
        rig.Play(rig.Place("c", new Vector3(1, 0, -1)), SoundPresenterRig.Beep, SoundPresenterRig.Once);
        rig.Play(rig.Place("d", new Vector3(-1, 0, -1)), SoundPresenterRig.Speech, SoundPresenterRig.Once);
        rig.Frame();

        rig.Stats.WithSource.ShouldBe(4);
    }
}
