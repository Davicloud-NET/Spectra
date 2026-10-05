using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Rules;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio.Captions;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The cook of a language's caption file: the text is packed as it is, and
/// what does not fit the project is warned about.
/// </summary>
public class CaptionFileRuleTests
{
    private const string English = "Captions/en.txt";
    private const string German = "Captions/de.txt";
    private const string Door = "Sounds/door_open.wav";
    private const string Lift = "Sounds/lift_hum.wav";
    private const string Alarm = "Sounds/alarm.wav";

    [Fact]
    public void A_caption_file_is_packed_byte_for_byte()
    {
        using var project = ProjectWithSounds(Door, Lift);
        string text = $"// Doors.\r\n{Door} = Door opens\r\n\r\n{Lift} = Aufzug summt, 扉\r\n";
        byte[] source = project.WriteAsset(English, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)]);

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        CookedAsset asset = result.Assets.Single(a => a.SourcePath == English);
        asset.Rule.ShouldBe(RuleKind.CaptionFile);
        asset.Outputs.Single().Path.ShouldBe(English);

        var pack = project.Track(new PackSource(NullLogger.Instance, result.OutputPath!));
        pack.TryOpen(English, out ContentBlob? blob).ShouldBeTrue();
        using (blob)
            blob.Span.ToArray().ShouldBe(source);
    }

    [Fact]
    public void A_clean_file_in_the_projects_language_says_how_many_sounds_it_captions()
    {
        using var project = ProjectWithSounds(Door, Lift);
        project.WriteAsset(English, $"{Door} = Door opens\n{Lift} = Lift hums\n");

        CookResult result = Cook(project);

        CookDiagnostic said = result.Diagnostics.ShouldHaveSingleItem();
        said.Id.ToString().ShouldBe("SC4107");
        said.Severity.ShouldBe(CookDiagnosticSeverity.Info);
        said.File.ShouldBe(English);
        said.Message.ShouldBe("'en' is the project's language. 2 sounds have a caption here.");
    }

    [Fact]
    public void A_line_that_is_not_a_path_and_words_is_warned_about_with_its_line()
    {
        using var project = ProjectWithSounds(Door);
        project.WriteAsset(English, $"{Door} = Door opens\n\nSounds/lift_hum.wav Lift hums\n");

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        CookDiagnostic warning = result.Diagnostics.Single(d => d.Id.ToString() == "SC4101");
        warning.Severity.ShouldBe(CookDiagnosticSeverity.Warning);
        warning.File.ShouldBe(English);
        warning.Line.ShouldBe(3);
        warning.Message.ShouldStartWith("This line is not a caption. Write the sound's path, an equals sign");
    }

    [Fact]
    public void A_sound_named_twice_in_one_file_is_warned_about_with_its_line()
    {
        using var project = ProjectWithSounds(Door);
        project.WriteAsset(English, $"{Door} = Door opens\n{Door} = Door creaks open\n");

        CookResult result = Cook(project);

        CookDiagnostic warning = result.Diagnostics.Single(d => d.Id.ToString() == "SC4102");
        warning.Line.ShouldBe(2);
        warning.Message.ShouldBe($"'{Door}' has a caption on line 1 already. The one on this line is used.");
    }

    [Fact]
    public void A_caption_for_a_sound_that_is_not_in_the_project_is_warned_about()
    {
        // One sound is authored, one is only there cooked, and one is nowhere.
        using var project = ProjectWithSounds(Door);
        project.WriteAsset("Sounds/lift_hum.saudio", TempProject.Bytes(64));
        project.WriteAsset(English, $"{Door} = Door opens\n{Lift} = Lift hums\n{Alarm} = Alarm blares\n");

        CookResult result = Cook(project);

        CookDiagnostic warning = result.Diagnostics.Single(d => d.Id.ToString() == "SC4103");
        warning.Line.ShouldBe(3);
        warning.Message.ShouldBe($"'{Alarm}' has a caption here and no such sound is in the project.");
    }

    [Fact]
    public void A_sound_with_a_caption_line_and_a_subtitle_file_in_one_language_is_warned_about()
    {
        using var project = ProjectWithSounds(Door, Lift);
        project.WriteAsset("Sounds/door_open.en.vtt", "WEBVTT\n\n00:00.000 --> 00:00.050\nDoor opens\n");
        project.WriteAsset("Sounds/lift_hum.de.vtt", "WEBVTT\n\n00:00.000 --> 00:00.050\nAufzug summt\n");
        project.WriteAsset(English, $"{Lift} = Lift hums\n{Door} = Door opens\n");

        CookResult result = Cook(project);

        CookDiagnostic warning = result.Diagnostics.Single(d => d.Id.ToString() == "SC4104");
        warning.Line.ShouldBe(2);
        warning.Message.ShouldBe(
            $"'{Door}' has a caption here and subtitles in 'Sounds/door_open.en.vtt'. The subtitles are " +
            "shown and this line is not.");
    }

    [Theory]
    [InlineData("Captions/English.txt")]
    [InlineData("Captions/EN.txt")]
    [InlineData("Captions/notes.txt")]
    public void A_text_file_in_the_captions_folder_that_is_no_language_is_warned_about_and_still_packed(string path)
    {
        using var project = ProjectWithSounds(Door);
        project.WriteAsset(path, $"{Door} = Door opens\n");

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        CookDiagnostic warning = result.Diagnostics.ShouldHaveSingleItem();
        warning.Id.ToString().ShouldBe("SC4105");
        warning.Message.ShouldContain("is not named after a language, so the engine never reads it");
        result.Assets.Single(a => a.SourcePath == path).Outputs.Single().Path.ShouldBe(path);
    }

    [Fact]
    public void A_language_that_lacks_captions_the_projects_language_has_is_told_how_many_and_which()
    {
        using var project = ProjectWithSounds(Door, Lift, Alarm);
        project.WriteAsset(English, $"{Door} = Door opens\n{Lift} = Lift hums\n{Alarm} = Alarm blares\n");
        project.WriteAsset(German, $"{Lift} = Aufzug summt\n");

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        CookDiagnostic warning = result.Diagnostics.Single(d => d.Id.ToString() == "SC4106");
        warning.Severity.ShouldBe(CookDiagnosticSeverity.Warning);
        warning.File.ShouldBe(German);
        warning.Message.ShouldBe(
            $"'de' has no caption for 2 of the 3 sounds that have one in 'en', the project's language: " +
            $"{Door}, {Alarm}. They show in 'en'. Subtitle files are not compared.");
    }

    [Fact]
    public void A_sound_with_subtitles_in_the_language_is_not_counted_as_missing()
    {
        using var project = ProjectWithSounds(Door, Lift);
        project.WriteAsset(English, $"{Door} = Door opens\n{Lift} = Lift hums\n");
        project.WriteAsset(German, $"{Lift} = Aufzug summt\n");
        project.WriteAsset("Sounds/door_open.de.vtt", "WEBVTT\n\n00:00.000 --> 00:00.050\nTür öffnet sich\n");

        CookResult result = Cook(project);

        result.Diagnostics.ShouldNotContain(d => d.Id.ToString() == "SC4106");
        result.Diagnostics.Single(d => d.File == German).Message.ShouldBe(
            "'de' has a caption for every sound that has one in 'en', the project's language (2 of 2). " +
            "Subtitle files are not compared.");
    }

    [Fact]
    public void More_missing_sounds_than_a_line_holds_are_counted_and_not_all_named()
    {
        string[] sounds = [.. Enumerable.Range(0, 8).Select(i => $"Sounds/step_{i}.wav")];
        using var project = ProjectWithSounds(sounds);
        project.WriteAsset(English, string.Join('\n', sounds.Select(sound => $"{sound} = Step")));
        project.WriteAsset(German, "// Nothing yet.\n");

        CookResult result = Cook(project);

        result.Diagnostics.Single(d => d.Id.ToString() == "SC4106").Message.ShouldContain(
            "no caption for 8 of the 8 sounds that have one in 'en', the project's language: " +
            "Sounds/step_0.wav, Sounds/step_1.wav, Sounds/step_2.wav, Sounds/step_3.wav, Sounds/step_4.wav " +
            "and 3 more.");
    }

    [Fact]
    public void The_language_every_other_is_compared_with_is_the_one_the_project_names()
    {
        using var project = ProjectWithSounds(Door, Lift);
        project.Layout.Project.Language = "de";
        project.WriteAsset(English, $"{Door} = Door opens\n");
        project.WriteAsset(German, $"{Door} = Tür öffnet sich\n{Lift} = Aufzug summt\n");

        CookResult result = Cook(project);

        result.Diagnostics.Single(d => d.File == German).Message.ShouldBe(
            "'de' is the project's language. 2 sounds have a caption here.");
        result.Diagnostics.Single(d => d.File == English).Message.ShouldStartWith(
            $"'en' has no caption for 1 of the 2 sounds that have one in 'de', the project's language: {Lift}.");
    }

    [Fact]
    public void A_language_with_nothing_to_be_compared_with_says_so()
    {
        using var project = ProjectWithSounds(Door);
        project.WriteAsset(German, $"{Door} = Tür öffnet sich\n");

        CookResult result = Cook(project);

        CookDiagnostic said = result.Diagnostics.ShouldHaveSingleItem();
        said.Severity.ShouldBe(CookDiagnosticSeverity.Info);
        said.Message.ShouldBe(
            "The project's language is 'en' and there is no 'Captions/en.txt', so 'de' has nothing to be " +
            "compared with.");
    }

    [Fact]
    public void A_missing_translation_fails_a_strict_cook()
    {
        using var project = ProjectWithSounds(Door, Lift);
        project.WriteAsset(English, $"{Door} = Door opens\n{Lift} = Lift hums\n");
        project.WriteAsset(German, $"{Lift} = Aufzug summt\n");

        CookResult result = Cook(project, strict: true);

        result.Succeeded.ShouldBeFalse();
        result.Diagnostics.Single(d => d.IsError).Id.ToString().ShouldBe("SC4106");
    }

    [Fact]
    public void A_caption_file_is_cooked_every_time_so_what_it_says_is_never_stale()
    {
        using var project = ProjectWithSounds(Door, Lift);
        project.WriteAsset(English, $"{Door} = Door opens\n{Lift} = Lift hums\n");
        project.WriteAsset(German, $"{Door} = Tür öffnet sich\n{Lift} = Aufzug summt\n");
        Cook(project, cache: true, label: "cold").Diagnostics.ShouldNotContain(d => d.Id.ToString() == "SC4106");

        // Nothing the cache could see has changed for the English file.
        project.Layout.Project.Language = "de";
        project.WriteAsset(German, $"{Door} = Tür öffnet sich\n{Lift} = Aufzug summt\n{Alarm} = Alarm\n");
        project.WriteAsset(Alarm, TempProject.Wav(frames: 4_800));
        CookResult warm = Cook(project, cache: true, label: "warm");

        warm.Assets.Single(a => a.SourcePath == English).FromCache.ShouldBeFalse();
        warm.Assets.Single(a => a.SourcePath == Door).FromCache.ShouldBeTrue();
        warm.Diagnostics.Single(d => d.Id.ToString() == "SC4106").File.ShouldBe(English);
    }

    [Fact]
    public void The_library_reads_both_kinds_of_caption_from_a_pack()
    {
        using var project = ProjectWithSounds(Door, "Sounds/vo/guard_hey.wav");
        project.WriteAsset(English, $"{Door} = Door opens\n");
        project.WriteAsset(
            "Sounds/vo/guard_hey.en.vtt", "WEBVTT\n\n00:00.000 --> 00:00.050\n<v Guard>Hey! You there!\n");
        CookResult result = Cook(project);
        result.Succeeded.ShouldBeTrue(Describe(result));

        var pack = project.Track(new PackSource(NullLogger.Instance, result.OutputPath!));
        var content = new ContentSourceStack();
        content.Mount(pack);
        var library = new CaptionLibrary(content, "en", new CapturingLogger());

        SoundCaptions door = library.Find(Door, "en").ShouldNotBeNull();
        SoundCaptions guard = library.Find("Sounds/vo/guard_hey.wav", "de").ShouldNotBeNull();

        door.Kind.ShouldBe(CaptionKind.Sound);
        door.Lines.ShouldHaveSingleItem().Text.ShouldBe("Door opens");
        guard.Kind.ShouldBe(CaptionKind.Voice);
        guard.Language.ShouldBe("en");
        guard.Lines.ShouldHaveSingleItem().ShouldBe(new CaptionLine(0d, 0.05d, "Guard", "Hey! You there!"));
    }

    [Fact]
    public void The_rule_claims_text_files_in_the_captions_folder_and_nothing_else()
    {
        CaptionFileRule.Handles(English).ShouldBeTrue();
        CaptionFileRule.Handles("captions/PT-BR.TXT").ShouldBeTrue();
        CaptionFileRule.Handles("Captions/notes.txt").ShouldBeTrue();
        CaptionFileRule.Handles("Captions/old/en.txt").ShouldBeFalse();
        CaptionFileRule.Handles("Captions/en.md").ShouldBeFalse();
        CaptionFileRule.Handles("Sounds/labelled.markers.txt").ShouldBeFalse();
        CaptionFileRule.Handles("Captions.txt").ShouldBeFalse();

        new CookRuleSet("en").Resolve(English).ShouldBeOfType<CaptionFileRule>();
    }

    [Fact]
    public void Every_caption_warning_is_one_a_strict_cook_refuses_and_the_summary_is_a_note()
    {
        CookDiagnosticId[] warnings =
        [
            CookDiagnosticCodes.CaptionLineUnreadable,
            CookDiagnosticCodes.CaptionSoundRepeated,
            CookDiagnosticCodes.CaptionSoundMissing,
            CookDiagnosticCodes.CaptionHiddenBySubtitles,
            CookDiagnosticCodes.CaptionFileNotALanguage,
            CookDiagnosticCodes.CaptionLanguageIncomplete,
            CookDiagnosticCodes.SubtitlePartNotRead,
            CookDiagnosticCodes.SubtitleStartsAfterSound,
            CookDiagnosticCodes.SubtitleNameHasNoLanguage,
            CookDiagnosticCodes.SubtitleHasNoCues,
        ];

        warnings.ShouldAllBe(id => CookGate.Verdict(id) == CookGateVerdict.WarningUnlessStrict);
        CookGate.Verdict(CookDiagnosticCodes.CaptionLanguageSummary).ShouldBe(CookGateVerdict.Note);
        CookGate.Verdict(CookDiagnosticCodes.SubtitleUnreadable).ShouldBe(CookGateVerdict.Fatal);

        // A range of their own in the audio band: every thousand is taken.
        warnings.ShouldAllBe(id => id.Number > 4100 && id.Number < 4200);
        CookDiagnosticCodes.DescribeBand(CookDiagnosticCodes.SubtitleUnreadable.Band).ShouldBe("audio and captions");
    }

    private static TempProject ProjectWithSounds(params string[] sounds)
    {
        var project = new TempProject();
        foreach (string sound in sounds)
            project.WriteAsset(sound, TempProject.Wav(frames: 4_800));

        return project;
    }

    private static CookResult Cook(TempProject project, bool cache = false, string label = "out", bool strict = false) =>
        new CookSession(
                project.Layout,
                new CookSettings
                {
                    UseCache = cache,
                    Strict = strict,
                    OutputPath = Path.Combine(project.Root, label),
                })
            .Run();

    private static string Describe(CookResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static d => d.ToString()));
}
