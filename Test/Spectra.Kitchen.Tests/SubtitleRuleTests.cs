using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The cook of a voice file's subtitles: the WebVTT is packed as it is,
/// checked against the sound beside it, and refused when the engine could not
/// read it.
/// </summary>
public class SubtitleRuleTests
{
    // One second long.
    private const string Guard = "Sounds/vo/guard_hey.wav";
    private const string Subtitles = "Sounds/vo/guard_hey.en.vtt";

    private const string TwoCues =
        "WEBVTT\n\n00:00.000 --> 00:00.400\n<v Guard>Hey! You there!\n\n00:00.500 --> 00:00.900\n<v Guard>Stop.\n";

    [Fact]
    public void A_subtitle_file_is_packed_byte_for_byte_and_says_nothing_when_it_is_clean()
    {
        using var project = ProjectWithTheGuard();
        byte[] source = project.WriteAsset(
            Subtitles, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(TwoCues.Replace("\n", "\r\n"))]);

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        result.Diagnostics.ShouldBeEmpty();
        CookedAsset asset = result.Assets.Single(a => a.SourcePath == Subtitles);
        asset.Rule.ShouldBe(RuleKind.Subtitle);
        asset.Outputs.Single().Path.ShouldBe(Subtitles);

        var pack = project.Track(new PackSource(NullLogger.Instance, result.OutputPath!));
        pack.TryOpen(Subtitles, out ContentBlob? blob).ShouldBeTrue();
        using (blob)
            blob.Span.ToArray().ShouldBe(source);
    }

    [Theory]
    [InlineData("1\n00:00:00,000 --> 00:00:01,400\nHey!\n", 1, "the first line is not WEBVTT")]
    [InlineData("WEBVTT\n\n00:00,000 --> 00:01.400\nHey!\n", 3, "'00:00,000' is not a time")]
    [InlineData("WEBVTT\n\nfirst\n00:00.900 --> 00:00.100\nHey!\n", 4, "A cue has to end after it starts")]
    [InlineData("WEBVTT\n\nHey! You there!\n", 3, "this block has no line of times")]
    public void Subtitles_the_engine_cannot_read_are_refused_with_the_file_and_the_line(
        string text, int line, string says)
    {
        using var project = ProjectWithTheGuard();
        project.WriteAsset(Subtitles, text);

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeFalse();
        result.OutputPath.ShouldBeNull();
        CookDiagnostic refusal = result.Diagnostics.Single(d => d.IsError);
        refusal.Id.ToString().ShouldBe("SC4108");
        refusal.File.ShouldBe(Subtitles);
        refusal.Line.ShouldBe(line);
        refusal.Message.ShouldContain(says);
        refusal.ToString().ShouldStartWith($"{Subtitles}({line},1): error SC4108: The engine cannot read these subtitles");
    }

    [Fact]
    public void Parts_of_WebVTT_the_engine_does_not_read_are_warned_about_and_the_file_still_ships()
    {
        using var project = ProjectWithTheGuard();
        project.WriteAsset(
            Subtitles,
            "WEBVTT\n\nSTYLE\n::cue { color: yellow }\n\n00:00.000 --> 00:00.400 align:start\n<i>Hey!</i>\n");

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        result.Diagnostics.ShouldAllBe(d => d.Id.ToString() == "SC4109" && d.File == Subtitles);
        result.Diagnostics.Select(d => (d.Line, d.Message)).ShouldBe(
        [
            (3, "The engine does not read a STYLE block. The words around it are still shown."),
            (6, "The engine does not read cue settings after the times. The words around it are still shown."),
            (7, "The engine does not read the <i> tag. The words around it are still shown."),
        ]);
        result.Assets.Single(a => a.SourcePath == Subtitles).Outputs.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_cue_that_starts_after_its_sound_has_ended_is_warned_about_with_its_line()
    {
        // The sound is one second long. A cue that only runs past its end is fine.
        using var project = ProjectWithTheGuard();
        project.WriteAsset(
            Subtitles,
            "WEBVTT\n\n00:00.800 --> 00:01.500\nStill fine\n\n00:01.000 --> 00:01.500\nToo late\n\n" +
            "00:02.250 --> 00:03.000\nFar too late\n");

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        result.Diagnostics.ShouldAllBe(d => d.Id.ToString() == "SC4110");
        result.Diagnostics.Select(d => d.Line).ShouldBe([6, 9]);
        result.Diagnostics[1].Message.ShouldBe(
            $"This cue starts at 2.25 s and '{Guard}' is over after 1 s, so the line never shows.");
    }

    [Fact]
    public void Subtitles_for_a_sound_that_is_not_in_the_project_are_warned_about()
    {
        using var project = new TempProject();
        project.WriteAsset(Subtitles, TwoCues);

        CookResult result = Cook(project);

        CookDiagnostic warning = result.Diagnostics.ShouldHaveSingleItem();
        warning.Id.ToString().ShouldBe("SC4103");
        warning.Message.ShouldBe(
            $"'{Subtitles}' holds the subtitles of '{Guard}', and no such sound is in the project.");
    }

    [Fact]
    public void Subtitles_beside_a_sound_that_is_only_there_cooked_are_not_warned_about()
    {
        using var project = new TempProject();
        project.WriteAsset("Sounds/vo/guard_hey.saudio", TempProject.Bytes(64));
        project.WriteAsset(Subtitles, TwoCues);

        Cook(project).Diagnostics.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Sounds/vo/guard_hey.vtt")]
    [InlineData("Sounds/vo/guard_hey.EN.vtt")]
    [InlineData("Sounds/vo/guard_hey.english.vtt")]
    public void A_subtitle_file_with_no_language_in_its_name_is_warned_about(string path)
    {
        using var project = ProjectWithTheGuard();
        project.WriteAsset(path, TwoCues);

        CookResult result = Cook(project);

        CookDiagnostic warning = result.Diagnostics.ShouldHaveSingleItem();
        warning.Id.ToString().ShouldBe("SC4111");
        warning.Message.ShouldContain("has no language in its name, so the engine never reads it");
    }

    [Fact]
    public void A_subtitle_file_with_no_cues_is_warned_about()
    {
        using var project = ProjectWithTheGuard();
        project.WriteAsset(Subtitles, "WEBVTT\n\nNOTE Nothing recorded yet.\n");

        CookResult result = Cook(project);

        CookDiagnostic warning = result.Diagnostics.ShouldHaveSingleItem();
        warning.Id.ToString().ShouldBe("SC4112");
        warning.Message.ShouldBe(
            $"'{Subtitles}' has no cue with words in it, so the sound has no subtitles in this language.");
    }

    [Fact]
    public void Clean_subtitles_come_from_the_cache_until_they_or_their_sound_change()
    {
        using var project = ProjectWithTheGuard();
        project.WriteAsset(Subtitles, TwoCues);
        Cook(project, cache: true, label: "cold").Succeeded.ShouldBeTrue();

        Cook(project, cache: true, label: "warm")
            .Assets.Single(a => a.SourcePath == Subtitles).FromCache.ShouldBeTrue();

        // Half a second now, so the second cue starts after the end.
        project.WriteAsset(Guard, TempProject.Wav(frames: 24_000));
        CookResult shorter = Cook(project, cache: true, label: "shorter");

        shorter.Assets.Single(a => a.SourcePath == Subtitles).FromCache.ShouldBeFalse();
        shorter.Diagnostics.Single(d => d.Id.ToString() == "SC4110").Line.ShouldBe(6);
    }

    [Fact]
    public void The_rule_claims_subtitle_files_and_nothing_else()
    {
        SubtitleRule.Handles(Subtitles).ShouldBeTrue();
        SubtitleRule.Handles("Sounds/vo/GUARD_HEY.EN.VTT").ShouldBeTrue();
        SubtitleRule.Handles("Sounds/vo/guard_hey.vtt").ShouldBeTrue();
        SubtitleRule.Handles(Guard).ShouldBeFalse();
        SubtitleRule.Handles("Sounds/vo/guard_hey.srt").ShouldBeFalse();
        SubtitleRule.Handles("Captions/en.txt").ShouldBeFalse();

        new CookRuleSet("en").Resolve(Subtitles).ShouldBeOfType<SubtitleRule>();
    }

    private static TempProject ProjectWithTheGuard()
    {
        var project = new TempProject();
        project.WriteAsset(Guard, TempProject.Wav(frames: 48_000));
        return project;
    }

    private static CookResult Cook(TempProject project, bool cache = false, string label = "out") =>
        new CookSession(
                project.Layout,
                new CookSettings { UseCache = cache, OutputPath = Path.Combine(project.Root, label) })
            .Run();

    private static string Describe(CookResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static d => d.ToString()));
}
