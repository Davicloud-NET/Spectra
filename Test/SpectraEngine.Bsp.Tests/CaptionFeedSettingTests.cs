using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio.Captions;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// One setting says which captions show: none, speech only, or speech and
/// other sounds. The language they are looked up in can be changed.
/// </summary>
public sealed class CaptionFeedSettingTests
{
    private const string Beep = SoundPresenterRig.Beep;
    private const string Speech = SoundPresenterRig.Speech;

    private const string BeepCaption = $"{Beep} = Beep sounds";

    private const string SpeechSubtitles = "WEBVTT\n\n00:00.000 --> 00:01.900\n<v Guard>Hey! You there!";

    [Fact]
    public void The_setting_starts_at_speech_only()
    {
        using var content = new CaptionContent();

        new CaptionFeed(content.Library()).Mode.ShouldBe(CaptionMode.Voice);
    }

    [Fact]
    public void Speech_only_shows_a_voice_line_and_hides_a_sound_caption()
    {
        using var rig = new CaptionFeedRig(CaptionMode.Voice);
        rig.Captions("en", BeepCaption);
        rig.Subtitles(Speech, "en", SpeechSubtitles);
        rig.Play(Beep);
        rig.Play(Speech);

        rig.Step(10);

        rig.Shown.ShouldHaveSingleItem().Text.ShouldBe("Hey! You there!");
    }

    [Fact]
    public void Off_shows_nothing()
    {
        using var rig = new CaptionFeedRig(CaptionMode.Off);
        rig.Captions("en", BeepCaption);
        rig.Subtitles(Speech, "en", SpeechSubtitles);
        rig.Play(Beep);
        rig.Play(Speech);

        rig.Step(10);

        rig.Shown.ShouldBeEmpty();
        rig.Feed.LastId.ShouldBe(0L);
        rig.Sound.CaptionLog.Describe().ShouldBe("(no log entries)");
    }

    [Fact]
    public void All_shows_both()
    {
        using var rig = new CaptionFeedRig(CaptionMode.All);
        rig.Captions("en", BeepCaption);
        rig.Subtitles(Speech, "en", SpeechSubtitles);
        rig.Play(Beep);
        rig.Play(Speech);

        rig.Step();

        rig.Shown.Select(caption => caption.Kind).ShouldBe([CaptionKind.Sound, CaptionKind.Voice]);
    }

    [Fact]
    public void A_kind_that_is_switched_off_goes_at_once()
    {
        using var rig = new CaptionFeedRig(CaptionMode.All);
        rig.Captions("en", BeepCaption);
        rig.Subtitles(Speech, "en", SpeechSubtitles);
        rig.Play(Beep);
        rig.Play(Speech);
        rig.Step();

        rig.Feed.Mode = CaptionMode.Voice;
        string[] speechOnly = [.. rig.Shown.Select(caption => caption.Text)];
        rig.Feed.Mode = CaptionMode.Off;

        speechOnly.ShouldBe(["Hey! You there!"]);
        rig.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void A_kind_that_is_switched_on_shows_what_is_heard_already()
    {
        using var rig = new CaptionFeedRig(CaptionMode.Off);
        rig.Captions("en", BeepCaption);
        rig.Subtitles(Speech, "en", SpeechSubtitles);
        rig.Play(Beep, CaptionFeedRig.Near, looped: true);
        rig.Play(Speech);
        rig.Step(30);

        rig.Feed.Mode = CaptionMode.All;
        rig.Step();

        rig.Shown.Select(caption => caption.Text).ShouldBe(["Beep sounds", "Hey! You there!"]);
        rig.Shown[0].StartedAt.ShouldBe(rig.Feed.Now);
    }

    [Fact]
    public void A_speech_caption_keeps_its_id_when_sound_captions_are_switched_on()
    {
        using var rig = new CaptionFeedRig(CaptionMode.Voice);
        rig.Subtitles(Speech, "en", SpeechSubtitles);
        rig.Play(Speech);
        rig.Step(5);
        Caption before = rig.Shown.ShouldHaveSingleItem();

        rig.Feed.Mode = CaptionMode.All;
        rig.Step(5);

        Caption after = rig.Shown.ShouldHaveSingleItem();
        after.Id.ShouldBe(before.Id);
        after.StartedAt.ShouldBe(before.StartedAt);
    }

    [Fact]
    public void The_language_is_the_projects_until_another_is_set()
    {
        using var content = new CaptionContent();
        CaptionLibrary library = content.Library(projectLanguage: "de");
        var feed = new CaptionFeed(library);

        feed.Language.ShouldBe("de");
        library.ProjectLanguage = "fr";
        feed.Language.ShouldBe("fr");

        feed.Language = "pt-br";
        library.ProjectLanguage = "en";
        feed.Language.ShouldBe("pt-br");
    }

    [Theory]
    [InlineData("German")]
    [InlineData("DE")]
    [InlineData("")]
    public void A_language_that_is_not_a_tag_is_refused(string language)
    {
        using var content = new CaptionContent();
        var feed = new CaptionFeed(content.Library());

        Should.Throw<ArgumentException>(() => feed.Language = language);
        feed.Language.ShouldBe("en");
    }

    [Fact]
    public void Setting_another_language_switches_the_words_of_what_shows()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", BeepCaption);
        rig.Captions("de", $"{Beep} = Piepton");
        rig.Play(Beep, CaptionFeedRig.Near, looped: true);
        rig.Step();
        Caption english = rig.Shown.ShouldHaveSingleItem();

        rig.Feed.Language = "de";
        rig.Step();

        Caption german = rig.Shown.ShouldHaveSingleItem();
        english.Text.ShouldBe("Beep sounds");
        german.Text.ShouldBe("Piepton");
        german.Id.ShouldBeGreaterThan(english.Id);
    }

    [Fact]
    public void A_sound_with_nothing_in_the_language_set_shows_the_projects_language()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", BeepCaption);
        rig.Captions("de", "Sounds/other.wav = Etwas anderes");
        rig.Subtitles(Speech, "en", SpeechSubtitles);
        rig.Feed.Language = "de";
        rig.Play(Beep);
        rig.Play(Speech);

        rig.Step();

        rig.Shown.Select(caption => caption.Text).ShouldBe(["Beep sounds", "Hey! You there!"]);
        rig.Sound.CaptionLog.MessagesAt(LogLevel.Warning).ShouldBeEmpty();
    }

    [Fact]
    public void A_language_with_no_caption_file_is_said_once_however_many_sounds_play()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", BeepCaption);
        rig.Feed.Language = "de";
        rig.Play(Beep);
        rig.Play(Speech);
        rig.Play(CaptionFeedRig.Click);

        rig.Step(30);

        rig.Sound.CaptionLog.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem().ShouldStartWith(
            "Captions: the project has no Captions/de.txt.");
    }

    [Fact]
    public void Ending_the_level_takes_every_caption_away_at_once()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", BeepCaption);
        rig.Play(Beep);
        rig.Step();
        rig.Shown.ShouldHaveSingleItem();

        rig.Sound.Presenter.Update(null, SoundPresenterRig.TickSeconds);

        rig.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void The_next_level_reads_the_caption_files_again()
    {
        using var rig = new CaptionFeedRig();
        rig.Captions("en", BeepCaption);
        rig.Play(Beep);
        rig.Step();
        long first = rig.Shown.ShouldHaveSingleItem().Id;

        rig.Captions("en", $"{Beep} = A short beep");
        rig.Sound.StartLevel();
        rig.Play(Beep);
        rig.Step();

        Caption next = rig.Shown.ShouldHaveSingleItem();
        next.Text.ShouldBe("A short beep");
        next.Id.ShouldBeGreaterThan(first);
    }
}
