using SpectraEngine.Core.Audio.Captions;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A caption file is one sound caption on a line: the sound's path, an equals
/// sign and the words.
/// </summary>
public sealed class CaptionFileReaderTests
{
    [Fact]
    public void Each_line_gives_a_sound_its_words()
    {
        CaptionFile file = Read(
            """
            Sounds/door_open.wav = Door opens
            Sounds/lift_hum.wav = Lift hums
            """);

        file.Entries.ShouldBe(
        [
            new CaptionFileEntry("Sounds/door_open.wav", "Door opens", 1),
            new CaptionFileEntry("Sounds/lift_hum.wav", "Lift hums", 2),
        ]);
        file.Problems.ShouldBeEmpty();
        file.TryGetText("Sounds/lift_hum.wav", out string? words).ShouldBeTrue();
        words.ShouldBe("Lift hums");
    }

    [Fact]
    public void Comment_lines_and_blank_lines_are_skipped_and_still_counted()
    {
        CaptionFile file = Read(
            """
            // Captions/en.txt

              // The start room.
            Sounds/door_open.wav = Door opens
            """);

        file.Entries.ShouldHaveSingleItem().ShouldBe(new CaptionFileEntry("Sounds/door_open.wav", "Door opens", 4));
        file.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void A_byte_order_mark_is_skipped()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Sounds/door_open.wav = Door opens")];

        CaptionFile file = CaptionFileReader.Read(bytes);

        file.Entries.ShouldHaveSingleItem().Sound.ShouldBe("Sounds/door_open.wav");
        file.Problems.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(@"Sounds\door_open.wav")]
    [InlineData("/Sounds/door_open.wav")]
    [InlineData("./Sounds//door_open.wav")]
    [InlineData("  Sounds/door_open.wav  ")]
    public void A_path_is_normalized_the_way_every_asset_path_is(string written)
    {
        CaptionFile file = Read($"{written} = Door opens");

        file.Entries.ShouldHaveSingleItem().Sound.ShouldBe("Sounds/door_open.wav");
    }

    [Fact]
    public void A_sound_is_found_whatever_case_its_path_is_asked_in()
    {
        CaptionFile file = Read("Sounds/Door_Open.wav = Door opens");

        file.TryGetText("sounds/door_open.wav", out string? words).ShouldBeTrue();
        words.ShouldBe("Door opens");
    }

    [Fact]
    public void The_words_are_everything_after_the_first_equals_sign()
    {
        CaptionFile file = Read("Sounds/sum.wav =   Two and two = four  ");

        file.Entries.ShouldHaveSingleItem().Text.ShouldBe("Two and two = four");
    }

    [Fact]
    public void A_backslash_and_an_n_is_a_line_break()
    {
        CaptionFile file = Read(@"Sounds/radio.wav = Radio crackles \n A voice fades in");

        file.Entries.ShouldHaveSingleItem().Text.ShouldBe("Radio crackles\nA voice fades in");
    }

    [Fact]
    public void The_words_may_be_in_any_script()
    {
        CaptionFile file = Read("Sounds/door_open.wav = Tür öffnet sich, 扉が開く");

        file.Entries.ShouldHaveSingleItem().Text.ShouldBe("Tür öffnet sich, 扉が開く");
    }

    [Fact]
    public void A_line_with_no_equals_sign_is_a_problem_that_names_the_line()
    {
        CaptionFile file = Read(
            """
            Sounds/door_open.wav = Door opens
            Sounds/lift_hum.wav Lift hums
            """);

        file.Entries.ShouldHaveSingleItem().Sound.ShouldBe("Sounds/door_open.wav");
        CaptionFileProblem problem = file.Problems.ShouldHaveSingleItem();
        problem.Line.ShouldBe(2);
        problem.Kind.ShouldBe(CaptionFileProblemKind.NotACaption);
        problem.Message.ShouldContain("Sounds/door_open.wav = Door opens");
    }

    [Theory]
    [InlineData("= Door opens", "no sound path")]
    [InlineData("Sounds/door_open.wav =", "no words")]
    [InlineData(@"Sounds/door_open.wav = \n", "no words")]
    [InlineData("../door_open.wav = Door opens", "not a path inside the project's content")]
    [InlineData("C:/door_open.wav = Door opens", "not a path inside the project's content")]
    public void A_line_that_is_not_a_path_and_words_is_a_problem(string line, string says)
    {
        CaptionFile file = Read(line);

        file.Entries.ShouldBeEmpty();
        CaptionFileProblem problem = file.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(CaptionFileProblemKind.NotACaption);
        problem.Message.ShouldContain(says);
    }

    [Fact]
    public void A_sound_named_twice_keeps_the_later_words_and_is_a_problem()
    {
        CaptionFile file = Read(
            """
            Sounds/door_open.wav = Door opens
            Sounds/lift_hum.wav = Lift hums
            sounds/DOOR_OPEN.wav = Door creaks open
            """);

        file.Entries.Select(entry => entry.Text).ShouldBe(["Door creaks open", "Lift hums"]);
        file.Entries[0].Line.ShouldBe(3);

        CaptionFileProblem problem = file.Problems.ShouldHaveSingleItem();
        problem.Line.ShouldBe(3);
        problem.Kind.ShouldBe(CaptionFileProblemKind.Repeated);
        problem.Message.ShouldContain("line 1");
    }

    [Fact]
    public void An_empty_file_has_no_captions_and_no_problems()
    {
        CaptionFile file = Read("");

        file.Entries.ShouldBeEmpty();
        file.Problems.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Captions/en.txt", "en")]
    [InlineData("Captions/pt-br.txt", "pt-br")]
    [InlineData("captions/de.TXT", "de")]
    public void A_caption_file_is_named_after_its_language(string path, string language)
    {
        CaptionFile.TryGetLanguage(path, out string? found).ShouldBeTrue();

        found.ShouldBe(language);
    }

    [Fact]
    public void A_languages_caption_file_is_in_the_captions_folder()
    {
        CaptionFile.PathFor("pt-br").ShouldBe("Captions/pt-br.txt");
    }

    [Theory]
    [InlineData("Captions/English.txt")]
    [InlineData("Captions/EN.txt")]
    [InlineData("Captions/old/en.txt")]
    [InlineData("Captions/en.md")]
    [InlineData("Sounds/en.txt")]
    public void A_file_that_is_not_a_language_in_the_captions_folder_is_no_caption_file(string path)
    {
        CaptionFile.TryGetLanguage(path, out _).ShouldBeFalse();
    }

    private static CaptionFile Read(string text) => CaptionFileReader.Read(Encoding.UTF8.GetBytes(text));
}
