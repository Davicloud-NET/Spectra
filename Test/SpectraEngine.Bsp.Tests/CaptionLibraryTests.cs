using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio.Captions;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The library answers what captions a sound has in a language: speech from a
/// subtitle file, a sound caption from the caption file, the project's
/// language when the one asked for has neither.
/// </summary>
public sealed class CaptionLibraryTests
{
    private const string Door = "Sounds/door_open.wav";
    private const string Guard = "Sounds/vo/guard_hey.wav";

    private const string GuardInEnglish =
        """
        WEBVTT

        00:00.000 --> 00:01.400
        <v Guard>Hey! You there!

        00:01.900 --> 00:03.600
        <v Guard>Stop right where you are.
        """;

    [Fact]
    public void A_sound_with_a_line_in_the_caption_file_has_one_line_that_lasts_as_long_as_it_does()
    {
        using var content = new CaptionContent();
        content.Write("Captions/en.txt", $"{Door} = Door opens");

        SoundCaptions captions = content.Library().Find(Door, "en").ShouldNotBeNull();

        captions.Kind.ShouldBe(CaptionKind.Sound);
        captions.Language.ShouldBe("en");
        captions.Lines.ShouldHaveSingleItem().ShouldBe(
            new CaptionLine(0d, double.PositiveInfinity, null, "Door opens"));
    }

    [Fact]
    public void A_sound_with_a_subtitle_file_beside_it_is_speech()
    {
        using var content = new CaptionContent();
        content.Write("Sounds/vo/guard_hey.en.vtt", GuardInEnglish);

        SoundCaptions captions = content.Library().Find(Guard, "en").ShouldNotBeNull();

        captions.Kind.ShouldBe(CaptionKind.Voice);
        captions.Lines.ShouldBe(
        [
            new CaptionLine(0d, 1.4d, "Guard", "Hey! You there!"),
            new CaptionLine(1.9d, 3.6d, "Guard", "Stop right where you are."),
        ]);
    }

    [Fact]
    public void A_sound_with_both_is_speech_because_the_subtitle_file_wins()
    {
        using var content = new CaptionContent();
        content.Write("Captions/en.txt", $"{Guard} = Guard shouts");
        content.Write("Sounds/vo/guard_hey.en.vtt", GuardInEnglish);

        content.Library().Find(Guard, "en").ShouldNotBeNull().Kind.ShouldBe(CaptionKind.Voice);
    }

    [Fact]
    public void A_sound_with_nothing_in_the_language_asked_for_gets_the_projects_language()
    {
        using var content = new CaptionContent();
        content.Write("Captions/en.txt", $"{Door} = Door opens");
        content.Write("Captions/de.txt", "Sounds/lift_hum.wav = Aufzug summt");
        content.Write("Sounds/vo/guard_hey.en.vtt", GuardInEnglish);
        CaptionLibrary library = content.Library();

        SoundCaptions door = library.Find(Door, "de").ShouldNotBeNull();
        SoundCaptions guard = library.Find(Guard, "de").ShouldNotBeNull();

        door.Language.ShouldBe("en");
        door.Lines[0].Text.ShouldBe("Door opens");
        guard.Language.ShouldBe("en");
        guard.Kind.ShouldBe(CaptionKind.Voice);
        library.Find("Sounds/lift_hum.wav", "de").ShouldNotBeNull().Language.ShouldBe("de");
    }

    [Fact]
    public void Each_kind_falls_back_by_itself()
    {
        // German has the subtitles and no caption line, English the other way round.
        using var content = new CaptionContent();
        content.Write("Captions/en.txt", $"{Guard} = Guard shouts");
        content.Write("Sounds/vo/guard_hey.de.vtt", "WEBVTT\n\n00:00.000 --> 00:01.000\nHe! Sie da!");
        CaptionLibrary library = content.Library();

        library.Find(Guard, "de").ShouldNotBeNull().Kind.ShouldBe(CaptionKind.Voice);
        library.Find(Guard, "en").ShouldNotBeNull().Kind.ShouldBe(CaptionKind.Sound);
    }

    [Fact]
    public void The_projects_language_is_what_the_project_says_it_is()
    {
        using var content = new CaptionContent();
        content.Write("Captions/de.txt", $"{Door} = Tür öffnet sich");
        CaptionLibrary library = content.Library(projectLanguage: "de");

        library.Find(Door, "fr").ShouldNotBeNull().Language.ShouldBe("de");

        library.ProjectLanguage = "en";
        library.Find(Door, "fr").ShouldBeNull();
    }

    [Fact]
    public void A_sound_with_nothing_in_either_language_has_no_captions()
    {
        using var content = new CaptionContent();
        content.Write("Captions/en.txt", $"{Door} = Door opens");
        CaptionLibrary library = content.Library();

        library.Find("Sounds/lift_hum.wav", "de").ShouldBeNull();
        library.Find("Sounds/lift_hum.wav", "en").ShouldBeNull();
        library.Find("../outside.wav", "en").ShouldBeNull();
    }

    [Fact]
    public void A_sound_is_found_however_its_path_is_spelled()
    {
        using var content = new CaptionContent();
        content.Write("Captions/en.txt", $"{Door} = Door opens");
        CaptionLibrary library = content.Library();

        SoundCaptions spelledOddly = library.Find(@"sounds\DOOR_OPEN.wav", "en").ShouldNotBeNull();

        spelledOddly.Lines[0].Text.ShouldBe("Door opens");
        library.Find(Door, "en").ShouldBeSameAs(spelledOddly);
        library.Find("/Sounds//door_open.wav", "en").ShouldBeSameAs(spelledOddly);
    }

    [Fact]
    public void A_missing_language_file_is_said_once_and_not_once_for_each_sound()
    {
        using var content = new CaptionContent();
        content.Write("Captions/en.txt", $"{Door} = Door opens");
        CaptionLibrary library = content.Library();

        library.Find(Door, "de");
        library.Find("Sounds/lift_hum.wav", "de");
        library.Reload();
        library.Find(Guard, "de");

        string said = content.Log.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        said.ShouldBe(
            "Captions: the project has no Captions/de.txt. A sound with no subtitle file in de gets its " +
            "caption in en, the project's language");
        content.Log.MessagesAt(LogLevel.Information).ShouldBeEmpty();
    }

    [Fact]
    public void A_project_with_no_caption_file_of_its_own_is_told_once_and_not_warned()
    {
        using var content = new CaptionContent();
        CaptionLibrary library = content.Library();

        library.Find(Door, "en").ShouldBeNull();
        library.Find(Guard, "en").ShouldBeNull();

        content.Log.MessagesAt(LogLevel.Information).ShouldHaveSingleItem().ShouldBe(
            "Captions: the project has no Captions/en.txt, so a sound with no subtitle file has no caption");
        content.Log.MessagesAt(LogLevel.Warning).ShouldBeEmpty();
    }

    [Fact]
    public void A_subtitle_file_that_is_refused_is_warned_about_once_and_the_caption_line_shows()
    {
        using var content = new CaptionContent();
        content.Write("Captions/en.txt", $"{Guard} = Guard shouts");
        content.Write("Sounds/vo/guard_hey.en.vtt", "WEBVTT\n\n00:00,000 --> 00:01,400\nHey!");
        CaptionLibrary library = content.Library();

        SoundCaptions captions = library.Find(Guard, "en").ShouldNotBeNull();
        library.Find(Guard, "en");
        library.Find(@"sounds\vo\GUARD_HEY.wav", "en");

        captions.Kind.ShouldBe(CaptionKind.Sound);
        content.Log.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem().ShouldStartWith(
            "Subtitles will not show: Sounds/vo/guard_hey.en.vtt(3): '00:00,000' is not a time");
    }

    [Fact]
    public void A_subtitle_file_that_is_still_refused_when_it_is_read_again_is_warned_about_again()
    {
        const string SecondCue = "\nHey!\n\n00:02 --> 00:03.000\nStop.";
        using var content = new CaptionContent();
        content.Write("Sounds/vo/guard_hey.en.vtt", "WEBVTT\n\n00:00,000 --> 00:01.400" + SecondCue);
        CaptionLibrary library = content.Library();
        library.Find(Guard, "en").ShouldBeNull();

        // The first mistake is put right and the second is still there.
        content.Write("Sounds/vo/guard_hey.en.vtt", "WEBVTT\n\n00:00.000 --> 00:01.400" + SecondCue);
        library.Reload();
        library.Find(Guard, "en").ShouldBeNull();

        IReadOnlyList<string> said = content.Log.MessagesAt(LogLevel.Warning);
        said.Count.ShouldBe(2);
        said[0].ShouldStartWith("Subtitles will not show: Sounds/vo/guard_hey.en.vtt(3): '00:00,000' is not a time");
        said[1].ShouldStartWith("Subtitles will not show: Sounds/vo/guard_hey.en.vtt(6): '00:02' is not a time");
    }

    [Fact]
    public void A_subtitle_file_that_was_refused_and_is_put_right_gives_its_lines_when_it_is_read_again()
    {
        using var content = new CaptionContent();
        content.Write("Sounds/vo/guard_hey.en.vtt", "WEBVTT\n\n00:00,000 --> 00:01,400\nHey!");
        CaptionLibrary library = content.Library();
        library.Find(Guard, "en").ShouldBeNull();

        content.Write("Sounds/vo/guard_hey.en.vtt", GuardInEnglish);
        library.Reload();

        library.Find(Guard, "en").ShouldNotBeNull().Lines.Count.ShouldBe(2);
        content.Log.MessagesAt(LogLevel.Warning).Count.ShouldBe(1);
    }

    [Fact]
    public void A_subtitle_file_with_no_cues_counts_as_none()
    {
        using var content = new CaptionContent();
        content.Write("Captions/en.txt", $"{Guard} = Guard shouts");
        content.Write("Sounds/vo/guard_hey.de.vtt", "WEBVTT\n");
        CaptionLibrary library = content.Library();

        library.Find(Guard, "de").ShouldNotBeNull().Language.ShouldBe("en");
    }

    [Fact]
    public void A_line_of_the_caption_file_that_cannot_be_read_is_warned_about_with_its_line()
    {
        using var content = new CaptionContent();
        content.Write("Captions/en.txt", $"{Door} = Door opens\nSounds/lift_hum.wav Lift hums");
        CaptionLibrary library = content.Library();

        library.Find(Door, "en").ShouldNotBeNull();

        content.Log.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem().ShouldStartWith(
            "Captions Captions/en.txt(2): this line is not a caption");
    }

    [Fact]
    public void A_file_is_read_once_until_the_library_is_told_to_read_again()
    {
        using var content = new CaptionContent();
        content.Write("Captions/en.txt", $"{Door} = Door opens");
        CaptionLibrary library = content.Library();
        SoundCaptions first = library.Find(Door, "en").ShouldNotBeNull();

        content.Write("Captions/en.txt", $"{Door} = Door creaks open");

        library.Find(Door, "en").ShouldBeSameAs(first);
        library.Reload();
        library.Find(Door, "en").ShouldNotBeNull().Lines[0].Text.ShouldBe("Door creaks open");
    }

    [Fact]
    public void The_library_says_whether_a_language_has_a_caption_file()
    {
        using var content = new CaptionContent();
        content.Write("Captions/en.txt", $"{Door} = Door opens");
        CaptionLibrary library = content.Library();

        library.HasCaptionFile("en").ShouldBeTrue();
        library.HasCaptionFile("de").ShouldBeFalse();
    }

    [Theory]
    [InlineData("English")]
    [InlineData("EN")]
    [InlineData("")]
    public void A_projects_language_that_is_not_a_tag_is_refused(string language)
    {
        using var content = new CaptionContent();

        Should.Throw<ArgumentException>(() => content.Library(language));
        Should.Throw<ArgumentException>(() => content.Library().ProjectLanguage = language);
    }

    [Theory]
    [InlineData("Sounds/vo/guard_hey.wav", "en", "Sounds/vo/guard_hey.en.vtt")]
    [InlineData("Sounds/vo/guard_hey.wav", "pt-br", "Sounds/vo/guard_hey.pt-br.vtt")]
    [InlineData("Sounds.old/guard", "de", "Sounds.old/guard.de.vtt")]
    public void A_subtitle_file_is_beside_its_sound_under_the_sounds_name_and_the_language(
        string sound, string language, string subtitles)
    {
        SubtitlePath.For(sound, language).ShouldBe(subtitles);

        SubtitlePath.TryParse(subtitles, out string? stem, out string? found).ShouldBeTrue();
        found.ShouldBe(language);
        sound.ShouldStartWith(stem.ShouldNotBeNull());
    }

    [Theory]
    [InlineData("Sounds/vo/guard_hey.vtt")]
    [InlineData("Sounds/vo/guard_hey.EN.vtt")]
    [InlineData("Sounds/vo/guard_hey.english.vtt")]
    [InlineData("Sounds/vo/.en.vtt")]
    [InlineData("Sounds/vo/guard_hey.en.txt")]
    public void A_subtitle_file_with_no_language_in_its_name_belongs_to_no_sound(string path)
    {
        SubtitlePath.TryParse(path, out _, out _).ShouldBeFalse();
    }
}
