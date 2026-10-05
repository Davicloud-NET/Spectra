using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The console commands for captions: which show, the language they are
/// looked up in, and which of a level's sounds have none.
/// </summary>
public sealed class CaptionConsoleCommandTests : IDisposable
{
    private readonly CaptionContent _content = new();
    private readonly SpectraConsole _console = new();
    private readonly CaptionFeed _feed;

    public CaptionConsoleCommandTests()
    {
        _content.Write("Captions/en.txt", "Sounds/door_open.wav = Door opens");
        _feed = new CaptionFeed(_content.Library());
        CaptionConsoleCommands.Register(_console.Commands, _feed);
    }

    public void Dispose() => _content.Dispose();

    [Fact]
    public void Captions_alone_prints_the_setting()
    {
        Run("captions").ShouldBe(["captions: voice. Speech shows. Other sounds do not."]);
    }

    [Theory]
    [InlineData("captions off", CaptionMode.Off, "captions: off. No captions show.")]
    [InlineData("captions voice", CaptionMode.Voice, "captions: voice. Speech shows. Other sounds do not.")]
    [InlineData("captions all", CaptionMode.All, "captions: all. Speech and other sounds show.")]
    [InlineData("CAPTIONS All", CaptionMode.All, "captions: all. Speech and other sounds show.")]
    public void Captions_sets_which_captions_show_and_says_so(string line, CaptionMode mode, string reply)
    {
        _feed.Mode = mode == CaptionMode.Voice ? CaptionMode.Off : CaptionMode.Voice;

        Run(line).ShouldBe([reply]);

        _feed.Mode.ShouldBe(mode);
    }

    [Theory]
    [InlineData("captions loud", "captions: 'loud' is not a setting. Give off, voice or all.")]
    [InlineData("captions on", "captions: 'on' is not a setting. Give off, voice or all.")]
    [InlineData("captions \"\"", "captions: '' is not a setting. Give off, voice or all.")]
    [InlineData("captions off all", "captions: give one of off, voice or all.")]
    public void Captions_refuses_what_is_not_a_setting(string line, string refusal)
    {
        ConsoleLine reply = RunLines(line).ShouldHaveSingleItem();

        reply.Severity.ShouldBe(LogLevel.Error);
        reply.Text.ShouldBe(refusal);
        _feed.Mode.ShouldBe(CaptionMode.Voice);
    }

    [Fact]
    public void Caption_language_alone_prints_the_language()
    {
        Run("caption_language").ShouldBe(["caption_language: en, the project's language."]);
    }

    [Theory]
    [InlineData("caption_language de")]
    [InlineData("caption_language DE")]
    public void Caption_language_sets_the_language_and_says_what_it_falls_back_to(string line)
    {
        _content.Write("Captions/de.txt", "Sounds/door_open.wav = Tür öffnet sich");

        Run(line).ShouldBe(
        [
            "caption_language: de. A sound with no caption in de gets the one in en, the project's language.",
        ]);

        _feed.Language.ShouldBe("de");
        Run("caption_language").ShouldHaveSingleItem().ShouldStartWith("caption_language: de.");
    }

    [Fact]
    public void Caption_language_warns_about_a_language_with_no_caption_file()
    {
        IReadOnlyList<ConsoleLine> lines = RunLines("caption_language pt-br");

        lines.Select(line => line.Text).ShouldBe(
        [
            "caption_language: pt-br. A sound with no caption in pt-br gets the one in en, the project's language.",
            "caption_language: the project has no Captions/pt-br.txt.",
        ]);
        lines[0].Severity.ShouldBe(LogLevel.Information);
        lines[1].Severity.ShouldBe(LogLevel.Warning);
        _feed.Language.ShouldBe("pt-br");
    }

    [Theory]
    [InlineData(
        "caption_language German",
        "caption_language: 'German' is not a language. Give a short tag such as en, de or pt-br.")]
    [InlineData(
        "caption_language en_us",
        "caption_language: 'en_us' is not a language. Give a short tag such as en, de or pt-br.")]
    [InlineData("caption_language de fr", "caption_language: give one language, such as en or de.")]
    public void Caption_language_refuses_what_is_not_a_language(string line, string refusal)
    {
        ConsoleLine reply = RunLines(line).ShouldHaveSingleItem();

        reply.Severity.ShouldBe(LogLevel.Error);
        reply.Text.ShouldBe(refusal);
        _feed.Language.ShouldBe("en");
    }

    [Fact]
    public void Captions_missing_lists_the_levels_sounds_with_no_caption_in_the_language()
    {
        _content.Write("Captions/de.txt", "Sounds/lift_hum.wav = Aufzug summt");
        _content.Write("Sounds/vo/guard_hey.de.vtt", "WEBVTT\n\n00:00.000 --> 00:01.000\nHe! Sie da!");
        Scene level = Level("Sounds/lift_hum.wav", "Sounds/alarm.wav", "Sounds/vo/guard_hey.wav", "Sounds/door_open.wav");
        _feed.Language = "de";

        Run("captions_missing", level).ShouldBe(
        [
            "captions_missing: no caption in de for 2 of 4 sounds the level plays.",
            "Sounds/alarm.wav  no caption",
            "Sounds/door_open.wav  none in de, shows the one in en",
        ]);
    }

    [Fact]
    public void Captions_missing_says_so_when_every_sound_has_one()
    {
        Scene level = Level("Sounds/door_open.wav");

        Run("captions_missing", level).ShouldBe(
        [
            "captions_missing: none. Every sound the level plays has a caption in en (1 sound).",
        ]);
    }

    [Fact]
    public void Captions_missing_counts_a_sound_once_and_a_setting_left_at_its_default()
    {
        // Two speakers on one file, and a door whose sound is its class's default.
        Scene level = Level(@"sounds\ALARM.wav", "Sounds/alarm.wav");
        EntityRuntime.Place(level.Root, "door", "test_door");

        Run("captions_missing", level).ShouldBe(
        [
            "captions_missing: no caption in en for 2 of 2 sounds the level plays.",
            "Sounds/creak.wav  no caption",
            "sounds/ALARM.wav  no caption",
        ]);
    }

    [Fact]
    public void Captions_missing_passes_over_what_plays_no_sound()
    {
        Scene level = Level();
        EntityRuntime.Place(level.Root, "silent", "test_speaker");
        EntityRuntime.Place(level.Root, "stranger", "class_from_another_game");
        level.Root.CreateChild("crate");

        Run("captions_missing", level).ShouldBe(
        [
            "captions_missing: no entity in the level is set to play a sound.",
        ]);
    }

    [Fact]
    public void Captions_missing_reads_the_files_again_while_the_level_is_not_running()
    {
        Scene level = Level("Sounds/alarm.wav");
        Run("captions_missing", level)[0].ShouldStartWith("captions_missing: no caption in en for 1 of 1 sound");

        _content.Write("Captions/en.txt", "Sounds/alarm.wav = Alarm blares");

        Run("captions_missing", level)[0].ShouldStartWith("captions_missing: none.");
    }

    [Theory]
    [InlineData("captions_missing de", "captions_missing: it takes nothing after its name. caption_language sets the language it checks.")]
    public void Captions_missing_refuses_an_argument(string line, string refusal)
    {
        ConsoleLine reply = RunLines(line, Level("Sounds/alarm.wav")).ShouldHaveSingleItem();

        reply.Severity.ShouldBe(LogLevel.Error);
        reply.Text.ShouldBe(refusal);
    }

    [Fact]
    public void Captions_missing_with_no_level_open_says_so()
    {
        ConsoleLine reply = RunLines("captions_missing").ShouldHaveSingleItem();

        reply.Severity.ShouldBe(LogLevel.Error);
        reply.Text.ShouldBe("captions_missing: no level is open.");
    }

    [Fact]
    public void Help_lists_the_three_commands()
    {
        string[] help = Run("help");

        help.ShouldContain(line => line.StartsWith("captions  Sets which captions show"));
        help.ShouldContain(line => line.StartsWith("caption_language  Sets the language"));
        help.ShouldContain(line => line.StartsWith("captions_missing  Lists the sounds"));
        Run("help captions")[0].ShouldBe("captions [off | voice | all]");
        Run("help caption_language")[0].ShouldBe("caption_language [language]");
    }

    private string[] Run(string line, Scene? level = null) => [.. RunLines(line, level).Select(reply => reply.Text)];

    private IReadOnlyList<ConsoleLine> RunLines(string line, Scene? level = null)
    {
        _console.Execute(line, new ConsoleFrame(level, null));
        return _console.Output.Drain();
    }

    // A level with one speaker for each sound, and the classes it uses.
    private static Scene Level(params string[] sounds)
    {
        KeyvalueDescriptor Sound(string name, string standard) => new(
            name, "", "", standard,
            KeyvalueType.AssetSound, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u, KeyvalueDescriptor.NoChoices);

        var level = new Scene("Level")
        {
            EntitySchemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
            [
                new EntitySchema("test_door", keyvalues: [Sound("opensound", "Sounds/creak.wav")]),
                new EntitySchema("test_speaker", keyvalues: [Sound("sound", "")]),
            ])),
        };

        for (int i = 0; i < sounds.Length; i++)
        {
            SceneNode speaker = EntityRuntime.Place(level.Root, $"speaker{i}", "test_speaker");
            speaker.Entity.ShouldNotBeNull().SetValue("sound", sounds[i]);
        }

        return level;
    }
}
