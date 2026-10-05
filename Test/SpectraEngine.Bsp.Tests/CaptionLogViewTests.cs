using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio.Captions;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Until a view draws captions, each one is written to the log once, when it
/// appears.
/// </summary>
public sealed class CaptionLogViewTests
{
    private const string Speech = SoundPresenterRig.Speech;
    private const string Click = CaptionFeedRig.Click;

    [Fact]
    public void A_voice_line_is_written_with_its_speaker_and_its_words()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", "WEBVTT\n\n00:00.000 --> 00:01.400\n<v Guard>Hey! You there!");
        rig.Play(Speech);

        rig.Step();

        rig.Written.ShouldBe(["Caption: Guard: Hey! You there!"]);
    }

    [Fact]
    public void A_voice_line_nobody_is_named_for_is_written_as_its_words()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(Speech, "en", "WEBVTT\n\n00:00.000 --> 00:01.400\nHey! You there!");
        rig.Play(Speech);

        rig.Step();

        rig.Written.ShouldBe(["Caption: Hey! You there!"]);
    }

    [Fact]
    public void A_sound_caption_is_written_in_brackets()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Click} = Door opens");
        rig.Play(Click);

        rig.Step();

        rig.Written.ShouldBe(["Caption: [Door opens]"]);
    }

    [Fact]
    public void A_caption_of_several_lines_is_written_on_one()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $@"{Click} = Radio crackles \n A voice fades in");
        rig.Play(Click);

        rig.Step();

        rig.Written.ShouldBe(["Caption: [Radio crackles A voice fades in]"]);
    }

    [Fact]
    public void A_caption_is_written_once_however_long_it_shows()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Speech} = Engine runs");
        rig.Play(Speech);

        rig.RunTo(1.9);

        rig.Shown.ShouldHaveSingleItem();
        rig.Written.ShouldBe(["Caption: [Engine runs]"]);
        rig.ViewLog.MessagesAt(LogLevel.Warning).ShouldBeEmpty();
    }

    [Fact]
    public void A_caption_that_is_refreshed_is_not_written_again()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Click} = Footsteps");

        for (int step = 0; step < 5; step++)
        {
            rig.Play(Click);
            rig.Step(12);
        }

        rig.Written.ShouldBe(["Caption: [Footsteps]"]);
    }

    [Fact]
    public void A_caption_that_shows_again_later_is_written_again()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Click} = Door opens");
        rig.Play(Click);
        rig.RunTo(2);

        rig.Play(Click);
        rig.Step();

        rig.Written.ShouldBe(["Caption: [Door opens]", "Caption: [Door opens]"]);
    }

    [Fact]
    public void Lines_that_appear_one_after_the_other_are_written_one_after_the_other()
    {
        using var rig = new CaptionFeedRig();
        rig.Subtitles(
            Speech,
            "en",
            """
            WEBVTT

            00:00.000 --> 00:00.800
            <v Guard>Hey! You there!

            00:01.000 --> 00:01.800
            <v Guard>Stop right where you are.
            """);
        rig.Play(Speech);

        rig.RunTo(0.9);
        string[] first = [.. rig.Written];
        rig.RunTo(1.1);

        first.ShouldBe(["Caption: Guard: Hey! You there!"]);
        rig.Written.ShouldBe(["Caption: Guard: Hey! You there!", "Caption: Guard: Stop right where you are."]);
    }

    [Fact]
    public void A_view_that_starts_late_writes_what_shows_then()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", $"{Speech} = Engine runs");
        rig.Play(Speech);
        rig.Step(30);
        var log = new CapturingLogger();
        var late = new CaptionLogView(log);

        late.Update(rig.Feed);
        late.Update(rig.Feed);

        log.MessagesAt(LogLevel.Information).ShouldBe(["Caption: [Engine runs]"]);
    }
}
