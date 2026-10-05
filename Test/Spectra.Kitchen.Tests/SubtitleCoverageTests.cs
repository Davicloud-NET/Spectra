using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The cook says which sounds have subtitles in the project's language and
/// none in another language the project has.
/// </summary>
public class SubtitleCoverageTests
{
    private const string Cue = "WEBVTT\n\n00:00.000 --> 00:00.050\nWords\n";

    [Fact]
    public void A_language_that_lacks_subtitles_the_projects_language_has_is_told_how_many_and_which_files()
    {
        using var project = ProjectWithVoices("guard_hey", "guard_stop", "radio");
        project.WriteAsset("Sounds/vo/guard_hey.en.vtt", Cue);
        project.WriteAsset("Sounds/vo/guard_stop.en.vtt", Cue);
        project.WriteAsset("Sounds/vo/radio.en.vtt", Cue);
        project.WriteAsset("Sounds/vo/radio.de.vtt", Cue);

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        CookDiagnostic warning = result.Diagnostics.ShouldHaveSingleItem();
        warning.Id.ToString().ShouldBe("SC4114");
        warning.Severity.ShouldBe(CookDiagnosticSeverity.Warning);
        warning.Message.ShouldBe(
            "'de' has no subtitles for 2 of the 3 sounds that have them in 'en', the project's language. " +
            "There is no Sounds/vo/guard_hey.de.vtt, Sounds/vo/guard_stop.de.vtt. Their lines show in 'en'.");
    }

    [Fact]
    public void A_language_with_a_caption_file_and_not_one_subtitle_file_is_told_too()
    {
        using var project = ProjectWithVoices("guard_hey");
        project.WriteAsset("Sounds/vo/guard_hey.en.vtt", Cue);
        project.WriteAsset("Captions/de.txt", "// Nothing yet.\n");

        CookResult result = Cook(project);

        result.Diagnostics.Single(d => d.Id.ToString() == "SC4114").Message.ShouldBe(
            "'de' has no subtitles for 1 of the 1 sound that has them in 'en', the project's language. " +
            "There is no Sounds/vo/guard_hey.de.vtt. Its lines show in 'en'.");
    }

    [Fact]
    public void A_language_with_every_subtitle_file_says_nothing()
    {
        using var project = ProjectWithVoices("guard_hey", "radio");
        project.WriteAsset("Sounds/vo/guard_hey.en.vtt", Cue);
        project.WriteAsset("Sounds/vo/guard_hey.de.vtt", Cue);
        project.WriteAsset("Sounds/vo/radio.en.vtt", Cue);
        project.WriteAsset("Sounds/vo/radio.de.vtt", Cue);

        Cook(project).Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void A_subtitle_file_counts_for_its_sound_however_its_name_is_cased()
    {
        SubtitleCoverage.Check(Content("Sounds/Radio.en.vtt", "Sounds/radio.de.vtt"), "en").ShouldBeEmpty();
    }

    [Fact]
    public void The_language_every_other_is_compared_with_is_the_one_the_project_names()
    {
        using var project = ProjectWithVoices("guard_hey", "radio");
        project.Layout.Project.Language = "de";
        project.WriteAsset("Sounds/vo/guard_hey.en.vtt", Cue);
        project.WriteAsset("Sounds/vo/guard_hey.de.vtt", Cue);
        project.WriteAsset("Sounds/vo/radio.de.vtt", Cue);

        Cook(project).Diagnostics.ShouldHaveSingleItem().Message.ShouldStartWith(
            "'en' has no subtitles for 1 of the 2 sounds that have them in 'de', the project's language. " +
            "There is no Sounds/vo/radio.en.vtt.");
    }

    [Fact]
    public void Missing_subtitles_fail_a_strict_cook()
    {
        using var project = ProjectWithVoices("guard_hey");
        project.WriteAsset("Sounds/vo/guard_hey.en.vtt", Cue);
        project.WriteAsset("Sounds/vo/other.de.vtt", Cue);
        project.WriteAsset("Sounds/vo/other.wav", TempProject.Wav(frames: 4_800));

        CookResult result = Cook(project, strict: true);

        result.Succeeded.ShouldBeFalse();
        result.Diagnostics.Single(d => d.IsError).Id.ToString().ShouldBe("SC4114");
    }

    [Fact]
    public void Missing_subtitles_are_said_again_when_every_file_comes_from_the_cache()
    {
        using var project = ProjectWithVoices("guard_hey", "radio");
        project.WriteAsset("Sounds/vo/guard_hey.en.vtt", Cue);
        project.WriteAsset("Sounds/vo/radio.de.vtt", Cue);
        Cook(project, cache: true, label: "cold").Diagnostics.ShouldHaveSingleItem();

        CookResult warm = Cook(project, cache: true, label: "warm");

        warm.Assets.ShouldAllBe(asset => asset.FromCache);
        warm.Diagnostics.ShouldHaveSingleItem().Id.ToString().ShouldBe("SC4114");
    }

    [Fact]
    public void Each_language_is_told_once_in_the_order_of_the_languages()
    {
        IReadOnlyList<CookDiagnostic> said = SubtitleCoverage.Check(
            Content("Captions/pt-br.txt", "Sounds/a.en.vtt", "Sounds/a.fr.vtt", "Sounds/b.en.vtt", "Sounds/b.de.vtt"),
            "en");

        said.Select(d => d.Message[..d.Message.IndexOf(" has", StringComparison.Ordinal)])
            .ShouldBe(["'de'", "'fr'", "'pt-br'"]);
        said[2].Message.ShouldContain("2 of the 2 sounds");
        said[2].Message.ShouldContain("There is no Sounds/a.pt-br.vtt, Sounds/b.pt-br.vtt.");
    }

    [Fact]
    public void More_missing_files_than_a_line_holds_are_counted_and_not_all_named()
    {
        string[] english = [.. Enumerable.Range(0, 8).Select(i => $"Sounds/line_{i}.en.vtt")];

        CookDiagnostic said = SubtitleCoverage.Check(Content([.. english, "Captions/de.txt"]), "en")
            .ShouldHaveSingleItem();

        said.Message.ShouldBe(
            "'de' has no subtitles for 8 of the 8 sounds that have them in 'en', the project's language. " +
            "There is no Sounds/line_0.de.vtt, Sounds/line_1.de.vtt, Sounds/line_2.de.vtt, " +
            "Sounds/line_3.de.vtt, Sounds/line_4.de.vtt and 3 more. Their lines show in 'en'.");
    }

    [Theory]
    [InlineData("Sounds/a.de.vtt Captions/de.txt")]
    [InlineData("Sounds/a.vtt Sounds/a.EN.vtt Captions/notes.txt Captions/de.txt")]
    public void With_no_subtitles_in_the_projects_language_there_is_nothing_to_compare(string paths)
    {
        SubtitleCoverage.Check(Content(paths.Split(' ')), "en").ShouldBeEmpty();
    }

    private static IReadOnlyList<ContentFile> Content(params string[] paths) =>
        [.. paths.Order(StringComparer.Ordinal).Select(path => new ContentFile(path, path))];

    private static TempProject ProjectWithVoices(params string[] names)
    {
        var project = new TempProject();
        foreach (string name in names)
            project.WriteAsset($"Sounds/vo/{name}.wav", TempProject.Wav(frames: 4_800));

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
