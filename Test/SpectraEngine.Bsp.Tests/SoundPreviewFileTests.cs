using SpectraEngine.Core.Audio;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The file behind a preview: read again every time it is asked for, and let
/// go when the sound stops or ends.
/// </summary>
public sealed class SoundPreviewFileTests
{
    private const string Click = "Sounds/click.wav";
    private const string Slow = "Sounds/slow.wav";

    [Fact]
    public void A_file_that_changed_is_heard_as_it_is_now_the_next_time()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        rig.Cook(Click, HandBuiltSaudio.Resident(frames: 600));
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        preview.Play(Click, out string refusal).ShouldBeTrue(refusal);
        preview.Stop().ShouldBeTrue();
        rig.Backend.Uploads.Clear();

        rig.Cook(Click, HandBuiltSaudio.Resident(frames: 900));
        preview.Play(Click, out refusal).ShouldBeTrue(refusal);

        rig.Backend.Uploads[0].Length.ShouldBe(900);
    }

    [Fact]
    public void A_preview_keeps_no_sound_open_once_it_has_stopped()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        preview.Play(SoundPresenterRig.Beep, out string refusal).ShouldBeTrue(refusal);

        preview.Stop().ShouldBeTrue();

        rig.Assets.AudioCount.ShouldBe(0);
        Should.NotThrow(() => rig.Cook(SoundPresenterRig.Beep, HandBuiltSaudio.Resident(frames: 16)));
    }

    [Fact]
    public void A_preview_that_ended_by_itself_keeps_no_sound_open()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        preview.Play(SoundPresenterRig.Beep, out string refusal).ShouldBeTrue(refusal);

        SoundPreviewTests.RunFrames(rig, preview, 12);

        preview.IsPlaying.ShouldBeFalse();
        rig.Assets.AudioCount.ShouldBe(0);
        Should.NotThrow(() => rig.Cook(SoundPresenterRig.Beep, HandBuiltSaudio.Resident(frames: 16)));
    }

    [Fact]
    public void A_preview_leaves_a_sound_the_level_has_open_alone()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        rig.Play(rig.Scene.Root, SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame();
        int open = rig.Assets.AudioCount;

        preview.Play(SoundPresenterRig.Beep, out string refusal).ShouldBeTrue(refusal);
        preview.Stop().ShouldBeTrue();
        rig.Frame(3);

        rig.Assets.AudioCount.ShouldBe(open);
        rig.Stats.WithSource.ShouldBe(1);
    }

    [Fact]
    public void What_a_host_asks_for_is_read_off_the_frame_and_starts_in_a_later_one()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        using GatedContentSource slow = SlowSound(rig);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);

        preview.Apply(Slow);

        // The frame goes on while the file is read.
        slow.WaitUntilAsked().ShouldBeTrue("nothing read the file");
        slow.AskingThread.ShouldNotBe(Environment.CurrentManagedThreadId);
        preview.Update();
        preview.IsLoading.ShouldBeTrue();
        preview.IsPlaying.ShouldBeFalse();

        slow.Open();
        SoundPreviewTests.WaitUntilRead(preview);

        preview.Path.ShouldBe(Slow);
        rig.OnlyVoice().Relative.ShouldBeTrue();
    }

    [Fact]
    public void A_newer_request_takes_the_place_of_a_file_still_being_read()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        using GatedContentSource slow = SlowSound(rig);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        preview.Apply(Slow);
        slow.WaitUntilAsked().ShouldBeTrue("nothing read the file");

        preview.Apply(SoundPresenterRig.Beep);
        SoundPreviewTests.WaitUntilRead(preview);
        preview.Path.ShouldBe(SoundPresenterRig.Beep);

        // The first file lands late. It is never heard and not kept.
        slow.Open();
        WaitUntil(slow.EverythingWasReleased).ShouldBeTrue("the file nobody wanted stayed open");
        preview.Update();

        preview.Path.ShouldBe(SoundPresenterRig.Beep);
        rig.Backend.PlayingSources().ShouldHaveSingleItem();
    }

    [Fact]
    public void A_stop_gives_up_on_a_file_still_being_read()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        using GatedContentSource slow = SlowSound(rig);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        preview.Apply(Slow);
        slow.WaitUntilAsked().ShouldBeTrue("nothing read the file");

        preview.Apply(string.Empty);
        preview.IsLoading.ShouldBeFalse();

        slow.Open();
        WaitUntil(slow.EverythingWasReleased).ShouldBeTrue("the file nobody wanted stayed open");
        preview.Update();

        preview.Path.ShouldBeEmpty();
        rig.Backend.PlayingSources().ShouldBeEmpty();
    }

    [Fact]
    public void A_second_press_while_a_file_is_being_read_does_not_read_it_again()
    {
        using var rig = new SoundPresenterRig(spareSources: 1);
        using GatedContentSource slow = SlowSound(rig);
        SoundPreview preview = SoundPreviewTests.PreviewOn(rig);
        preview.Apply(Slow);
        slow.WaitUntilAsked().ShouldBeTrue("nothing read the file");

        preview.Apply(Slow);
        slow.Open();
        SoundPreviewTests.WaitUntilRead(preview);

        slow.OpenCount.ShouldBe(1);
        preview.Path.ShouldBe(Slow);
    }

    // A sound whose bytes arrive only when the test opens the gate.
    private static GatedContentSource SlowSound(SoundPresenterRig rig)
    {
        var source = new GatedContentSource();
        source.Add("Sounds/slow.saudio", HandBuiltSaudio.Resident(frames: SoundPresenterRig.Rate));
        rig.Assets.Content.Mount(source);
        return source;
    }

    private static bool WaitUntil(Func<bool> condition) =>
        SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10));
}
