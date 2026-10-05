using SpectraEngine.Core.Audio.Captions;
using System.Diagnostics;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A voice file's subtitles are read from a small part of WebVTT: the header,
/// cues with their times and words, a speaker, and notes.
/// </summary>
public sealed class SubtitleReaderTests
{
    private const string File = "Sounds/vo/guard_hey.en.vtt";

    [Fact]
    public void Two_cues_give_two_lines_with_their_times_and_their_speaker()
    {
        SubtitleFile file = Read(
            """
            WEBVTT

            00:00.000 --> 00:01.400
            <v Guard>Hey! You there!

            00:01.900 --> 00:03.600
            <v Guard>Stop right where you are.
            """);

        file.Lines.ShouldBe(
        [
            new CaptionLine(0d, 1.4d, "Guard", "Hey! You there!"),
            new CaptionLine(1.9d, 3.6d, "Guard", "Stop right where you are."),
        ]);
        file.SourceLines.ShouldBe([3, 6]);
        file.Unread.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("00:01.500 --> 00:02.250", 1.5, 2.25)]
    [InlineData("00:00:01.500 --> 00:00:02.250", 1.5, 2.25)]
    [InlineData("01:02:03.004 --> 01:02:04.000", 3723.004, 3724.0)]
    [InlineData("59:59.999 --> 100:00:00.000", 3599.999, 360000.0)]
    [InlineData("00:01.500\t-->\t00:02.250", 1.5, 2.25)]
    public void A_time_is_read_with_hours_and_without(string times, double start, double end)
    {
        CaptionLine line = Read($"WEBVTT\n\n{times}\nWords").Lines.ShouldHaveSingleItem();

        line.Start.ShouldBe(start, 1e-9);
        line.End.ShouldBe(end, 1e-9);
    }

    [Fact]
    public void A_cue_may_have_a_name_before_its_times()
    {
        SubtitleFile file = Read(
            """
            WEBVTT

            greeting
            00:00.000 --> 00:01.400
            Hey! You there!
            """);

        file.Lines.ShouldHaveSingleItem().ShouldBe(new CaptionLine(0d, 1.4d, null, "Hey! You there!"));
        file.SourceLines.ShouldBe([4]);
        file.Unread.ShouldBeEmpty();
    }

    [Fact]
    public void Several_lines_of_words_keep_their_line_breaks()
    {
        SubtitleFile file = Read(
            """
            WEBVTT

            00:00.000 --> 00:04.000
            <v Guard>Stop right where you are.
              Put the crate down.
            Slowly.
            """);

        file.Lines.ShouldHaveSingleItem().Text.ShouldBe("Stop right where you are.\nPut the crate down.\nSlowly.");
    }

    [Theory]
    [InlineData("<v Guard>Hey!", "Guard")]
    [InlineData("<v Guard>Hey!</v>", "Guard")]
    [InlineData("<v Guard> Hey! </v>", "Guard")]
    [InlineData("<v.loud Guard Captain>Hey!", "Guard Captain")]
    [InlineData("<v Tom &amp; Jerry>Hey!", "Tom & Jerry")]
    [InlineData("<v>Hey!", null)]
    public void A_voice_span_at_the_start_names_the_speaker_and_is_taken_out(string words, string? speaker)
    {
        SubtitleFile file = Read($"WEBVTT\n\n00:00.000 --> 00:01.000\n{words}");

        file.Lines.ShouldHaveSingleItem().ShouldBe(new CaptionLine(0d, 1d, speaker, "Hey!"));
        file.Unread.ShouldBeEmpty();
    }

    [Fact]
    public void A_note_is_skipped_wherever_it_stands()
    {
        SubtitleFile file = Read(
            """
            WEBVTT

            NOTE Recorded on the second day.

            00:00.000 --> 00:01.400
            Hey! You there!

            NOTE
            The next line was cut.
            00:09.000 --> 00:10.000 is where it was.

            00:01.900 --> 00:03.600
            Stop right where you are.
            """);

        file.Lines.Select(line => line.Text).ShouldBe(["Hey! You there!", "Stop right where you are."]);
        file.Unread.ShouldBeEmpty();
    }

    [Fact]
    public void The_four_entities_are_read()
    {
        SubtitleFile file = Read(
            """
            WEBVTT

            00:00.000 --> 00:02.000
            Keys &amp; coins, &lt;both&gt;, 5&nbsp;m away. Q&A; this & that.
            """);

        file.Lines.ShouldHaveSingleItem().Text.ShouldBe("Keys & coins, <both>, 5 m away. Q&A; this & that.");
    }

    [Fact]
    public void What_the_engine_does_not_read_is_listed_and_the_words_are_shown_without_it()
    {
        SubtitleFile file = Read(
            """
            WEBVTT
            Kind: captions

            STYLE
            ::cue { color: yellow }

            REGION
            id:top

            00:00.000 --> 00:01.400 align:start position:10%
            <v Guard>Hey! <i>You</i> there!

            00:01.900 --> 00:03.600
            <c.warn>Stop</c> <00:02.500>right &lrm;here. <v Thief>Never!
            """);

        file.Lines.ShouldBe(
        [
            new CaptionLine(0d, 1.4d, "Guard", "Hey! You there!"),
            new CaptionLine(1.9d, 3.6d, null, "Stop right &lrm;here. Never!"),
        ]);
        file.Unread.ShouldBe(
        [
            new SubtitleUnreadPart(2, "header lines after WEBVTT"),
            new SubtitleUnreadPart(4, "a STYLE block"),
            new SubtitleUnreadPart(7, "a REGION block"),
            new SubtitleUnreadPart(10, "cue settings after the times"),
            new SubtitleUnreadPart(11, "the <i> tag"),
            new SubtitleUnreadPart(14, "the <c> tag"),
            new SubtitleUnreadPart(14, "a time inside the words"),
            new SubtitleUnreadPart(14, "the entity &lrm;"),
            new SubtitleUnreadPart(14, "a voice span that is not at the start of the words"),
        ]);
    }

    [Fact]
    public void A_part_that_is_not_read_is_listed_once_at_its_first_line()
    {
        SubtitleFile file = Read(
            """
            WEBVTT

            00:00.000 --> 00:01.000
            <i>One</i>

            00:01.000 --> 00:02.000
            <i>Two</i>
            """);

        file.Unread.ShouldHaveSingleItem().ShouldBe(new SubtitleUnreadPart(4, "the <i> tag"));
    }

    [Fact]
    public void A_cue_with_no_words_is_left_out_and_listed()
    {
        SubtitleFile file = Read(
            """
            WEBVTT

            00:00.000 --> 00:01.000

            00:01.000 --> 00:02.000
            <i></i>

            00:02.000 --> 00:03.000
            Words
            """);

        file.Lines.ShouldHaveSingleItem().Text.ShouldBe("Words");
        file.Unread.ShouldContain(new SubtitleUnreadPart(3, "a cue with no words"));
    }

    [Theory]
    [InlineData("<v Guard Hey!")]
    [InlineData("5 < 7, and 9 > 7")]
    [InlineData("<> and <")]
    public void A_bracket_that_opens_no_tag_is_shown_as_written_and_listed(string words)
    {
        SubtitleFile file = Read($"WEBVTT\n\n00:00.000 --> 00:01.000\n{words}");

        CaptionLine line = file.Lines.ShouldHaveSingleItem();
        line.Text.ShouldBe(words);
        line.Speaker.ShouldBeNull();
        file.Unread.ShouldHaveSingleItem().ShouldBe(
            new SubtitleUnreadPart(4, "a < that opens no tag, so it shows as written"));
    }

    [Fact]
    public void A_bracket_that_opens_no_tag_does_not_hide_a_tag_after_it()
    {
        SubtitleFile file = Read("WEBVTT\n\n00:00.000 --> 00:01.000\n1 < 2, <i>always</i>");

        file.Lines.ShouldHaveSingleItem().Text.ShouldBe("1 < 2, always");
        file.Unread.Select(part => part.What).ShouldBe(
            ["a < that opens no tag, so it shows as written", "the <i> tag"]);
    }

    [Fact]
    public void A_megabyte_of_brackets_and_of_ampersands_on_a_line_is_read_in_one_pass()
    {
        // Searching the rest of the line again for each would take minutes.
        const int Count = 1_000_000;
        string text = $"WEBVTT\n\n00:00.000 --> 00:01.000\n{new string('<', Count)}\n{new string('&', Count)}";
        var watch = Stopwatch.StartNew();

        SubtitleFile file = Read(text);

        watch.Stop();
        file.Lines.ShouldHaveSingleItem().Text.Length.ShouldBe(Count + 1 + Count);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_file_lists_no_more_parts_it_does_not_read_than_the_limit()
    {
        string tags = string.Concat(
            Enumerable.Range(0, 300).Select(i => $"<{(char)('a' + (i / 26))}{(char)('a' + (i % 26))}x>"));

        SubtitleFile file = Read($"WEBVTT\n\n00:00.000 --> 00:01.000\n{tags}Words");

        file.Lines.ShouldHaveSingleItem().Text.ShouldBe("Words");
        file.Unread.Count.ShouldBe(SubtitleReader.MaxUnreadParts);
    }

    [Fact]
    public void A_byte_order_mark_and_windows_line_endings_are_read()
    {
        byte[] bytes =
        [
            0xEF, 0xBB, 0xBF,
            .. Encoding.UTF8.GetBytes("WEBVTT\r\n\r\n00:00.000 --> 00:01.000\r\nOne\r\nTwo\r\n"),
        ];

        SubtitleFile file = SubtitleReader.Read(bytes, File);

        file.Lines.ShouldHaveSingleItem().Text.ShouldBe("One\nTwo");
    }

    [Fact]
    public void A_cue_right_under_the_header_is_read()
    {
        SubtitleFile file = Read("WEBVTT - made by hand\n00:00.000 --> 00:01.000\nWords");

        file.Lines.ShouldHaveSingleItem().Text.ShouldBe("Words");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1\n00:00:00,000 --> 00:00:01,400\nHey!")]
    [InlineData("WEBVTTX\n\n00:00.000 --> 00:01.000\nHey!")]
    [InlineData("\nWEBVTT\n\n00:00.000 --> 00:01.000\nHey!")]
    public void A_file_that_is_not_WebVTT_is_refused_at_its_first_line(string text)
    {
        SubtitleFormatException refusal = Should.Throw<SubtitleFormatException>(() => Read(text));

        refusal.Line.ShouldBe(1);
        refusal.Message.ShouldStartWith($"{File}(1): ");
        refusal.Reason.ShouldContain("not a WebVTT file");
    }

    [Theory]
    [InlineData("00:00,000 --> 00:01.400", "'00:00,000' is not a time")]
    [InlineData("00:00.000 --> 00:01.4", "'00:01.4' is not a time")]
    [InlineData("0:00.000 --> 00:01.400", "'0:00.000' is not a time")]
    [InlineData("00:61.000 --> 00:62.000", "'00:61.000' is not a time")]
    [InlineData("1.000 --> 2.000", "'1.000' is not a time")]
    [InlineData("00:00.000 --> soon", "'soon' is not a time")]
    [InlineData("00:00.000-->00:01.400", "the arrow needs a space on each side")]
    [InlineData("--> 00:01.400", "the arrow needs a space on each side")]
    public void A_line_of_times_that_cannot_be_read_is_refused_and_named(string times, string says)
    {
        SubtitleFormatException refusal = Should.Throw<SubtitleFormatException>(
            () => Read($"WEBVTT\n\n00:00.000 --> 00:01.000\nFine\n\n{times}\nHey!"));

        refusal.Line.ShouldBe(6);
        refusal.Message.ShouldStartWith($"{File}(6): ");
        refusal.Reason.ShouldContain(says);
    }

    [Theory]
    [InlineData("00:02.000 --> 00:01.000")]
    [InlineData("00:02.000 --> 00:02.000")]
    public void A_cue_that_does_not_end_after_it_starts_is_refused_and_named(string times)
    {
        SubtitleFormatException refusal = Should.Throw<SubtitleFormatException>(
            () => Read($"WEBVTT\n\nname\n{times}\nHey!"));

        refusal.Line.ShouldBe(4);
        refusal.Reason.ShouldContain("A cue has to end after it starts");
    }

    [Fact]
    public void A_block_with_no_line_of_times_is_refused_and_named()
    {
        SubtitleFormatException refusal = Should.Throw<SubtitleFormatException>(
            () => Read("WEBVTT\n\n00:00.000 --> 00:01.000\nFine\n\nHey! You there!\nStop."));

        refusal.Line.ShouldBe(6);
        refusal.Reason.ShouldContain("no line of times");
    }

    private static SubtitleFile Read(string text) => SubtitleReader.Read(Encoding.UTF8.GetBytes(text), File);
}
