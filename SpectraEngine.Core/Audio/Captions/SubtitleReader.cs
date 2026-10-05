using SpectraEngine.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpectraEngine.Core.Audio.Captions;

/// <summary>
/// Reads a voice file's subtitles from WebVTT, the web's subtitle format.
/// </summary>
/// <remarks>
/// <code>
/// WEBVTT
///
/// 00:00.000 --> 00:01.400
/// &lt;v Guard&gt;Hey! You there!
/// </code>
/// It reads a small part of the format: the <c>WEBVTT</c> line, cues apart
/// by blank lines, a cue's optional name, its times, its lines of text, a
/// voice span at the start of the text for the speaker, <c>NOTE</c> blocks,
/// and the entities for an ampersand, less than, greater than and a
/// non-breaking space. What else a file uses is listed in
/// <see cref="SubtitleFile.Unread"/> and the words are still read. A file
/// that is not WebVTT, a time that cannot be read and a cue that does not end
/// after it starts are refused. Any thread.
/// </remarks>
public static class SubtitleReader
{
    private const string Signature = "WEBVTT";
    private const string Arrow = "-->";

    /// <summary>Reads a subtitle file's bytes. A byte order mark at the start is skipped.</summary>
    /// <param name="originForErrors">The file's name, for the message of a refusal.</param>
    /// <exception cref="SubtitleFormatException">The file was refused.</exception>
    public static SubtitleFile Read(ReadOnlySpan<byte> utf8, string originForErrors = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(originForErrors);

        string text = Encoding.UTF8.GetString(CanonicalJson.StripBom(utf8));
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');

        var cues = new List<CaptionLine>();
        var cueLines = new List<int>();
        var unread = new List<SubtitleUnreadPart>();

        int at = ReadHeader(lines, originForErrors, unread);
        while (at < lines.Length)
        {
            if (IsBlank(lines[at]))
            {
                at++;
                continue;
            }

            int end = at;
            while (end < lines.Length && !IsBlank(lines[end]))
                end++;

            ReadBlock(lines, at, end, originForErrors, cues, cueLines, unread);
            at = end;
        }

        return new SubtitleFile(cues, cueLines, unread);
    }

    // Names a part once, at the first line that uses it.
    internal static void NoteUnread(List<SubtitleUnreadPart> unread, int line, string what)
    {
        for (int i = 0; i < unread.Count; i++)
        {
            if (unread[i].What == what)
                return;
        }

        unread.Add(new SubtitleUnreadPart(line, what));
    }

    // Returns the index of the first line after the header.
    private static int ReadHeader(string[] lines, string origin, List<SubtitleUnreadPart> unread)
    {
        if (!StartsWithWord(lines[0], Signature))
        {
            throw new SubtitleFormatException(
                origin, 1, "the first line is not WEBVTT, so this is not a WebVTT file");
        }

        // A cue may follow with no blank line between.
        int at = 1;
        while (at < lines.Length && !IsBlank(lines[at]) && !lines[at].Contains(Arrow, StringComparison.Ordinal))
        {
            NoteUnread(unread, at + 1, "header lines after WEBVTT");
            at++;
        }

        return at;
    }

    private static void ReadBlock(
        string[] lines,
        int first,
        int end,
        string origin,
        List<CaptionLine> cues,
        List<int> cueLines,
        List<SubtitleUnreadPart> unread)
    {
        int timing = first;
        if (!lines[first].Contains(Arrow, StringComparison.Ordinal))
        {
            if (IsSkippedBlock(lines[first], first + 1, unread))
                return;

            // The first line is the cue's name.
            timing = first + 1;
            if (timing >= end || !lines[timing].Contains(Arrow, StringComparison.Ordinal))
            {
                throw new SubtitleFormatException(
                    origin,
                    first + 1,
                    "this block has no line of times. A cue is an optional name, then a line like " +
                    "00:01.000 --> 00:02.500, then the words");
            }
        }

        ReadTimes(lines[timing], timing + 1, origin, unread, out double start, out double stop);

        string words = SubtitleText.Read(lines, timing + 1, end, unread, out string? speaker);
        if (words.Length == 0)
        {
            NoteUnread(unread, timing + 1, "a cue with no words");
            return;
        }

        cues.Add(new CaptionLine(start, stop, speaker, words));
        cueLines.Add(timing + 1);
    }

    private static bool IsSkippedBlock(string head, int number, List<SubtitleUnreadPart> unread)
    {
        if (StartsWithWord(head, "NOTE"))
            return true;

        if (StartsWithWord(head, "STYLE"))
        {
            NoteUnread(unread, number, "a STYLE block");
            return true;
        }

        if (StartsWithWord(head, "REGION"))
        {
            NoteUnread(unread, number, "a REGION block");
            return true;
        }

        return false;
    }

    private static void ReadTimes(
        string line, int number, string origin, List<SubtitleUnreadPart> unread, out double start, out double stop)
    {
        int arrow = line.IndexOf(Arrow, StringComparison.Ordinal);
        ReadOnlySpan<char> left = line.AsSpan(0, arrow);
        ReadOnlySpan<char> right = line.AsSpan(arrow + Arrow.Length);

        if (left.IsEmpty || !IsGap(left[^1]) || right.IsEmpty || !IsGap(right[0]))
        {
            throw new SubtitleFormatException(
                origin, number, "the arrow needs a space on each side, like 00:01.000 --> 00:02.500");
        }

        left = left.Trim();
        right = right.TrimStart();
        int cut = right.IndexOfAny(' ', '\t');
        ReadOnlySpan<char> ending = cut < 0 ? right : right[..cut];

        if (!TryReadTime(left, out start))
            throw new SubtitleFormatException(origin, number, NotATime(left));
        if (!TryReadTime(ending, out stop))
            throw new SubtitleFormatException(origin, number, NotATime(ending));

        if (stop <= start)
        {
            throw new SubtitleFormatException(
                origin, number, $"the cue starts at {left} and ends at {ending}. A cue has to end after it starts");
        }

        if (cut >= 0 && !right[cut..].Trim().IsEmpty)
            NoteUnread(unread, number, "cue settings after the times");
    }

    private static string NotATime(ReadOnlySpan<char> text) =>
        $"'{text}' is not a time. Write minutes, seconds and milliseconds, like 00:01.400, " +
        "or with hours, like 00:00:01.400";

    // mm:ss.ttt, or hh:mm:ss.ttt.
    private static bool TryReadTime(ReadOnlySpan<char> text, out double seconds)
    {
        seconds = 0d;

        int dot = text.LastIndexOf('.');
        int lastColon = text.LastIndexOf(':');
        if (dot < 0 || lastColon < 0 || lastColon > dot)
            return false;

        ReadOnlySpan<char> front = text[..lastColon];
        int firstColon = front.LastIndexOf(':');
        ReadOnlySpan<char> hours = firstColon < 0 ? "0".AsSpan() : front[..firstColon];

        if (!TryReadDigits(hours, 1, 4, out int hour)
            || !TryReadDigits(front[(firstColon + 1)..], 2, 2, out int minute)
            || !TryReadDigits(text[(lastColon + 1)..dot], 2, 2, out int second)
            || !TryReadDigits(text[(dot + 1)..], 3, 3, out int millisecond)
            || minute > 59
            || second > 59)
        {
            return false;
        }

        seconds = (hour * 3600d) + (minute * 60d) + second + (millisecond / 1000d);
        return true;
    }

    private static bool TryReadDigits(ReadOnlySpan<char> text, int shortest, int longest, out int value)
    {
        value = 0;
        if (text.Length < shortest || text.Length > longest || text.ContainsAnyExceptInRange('0', '9'))
            return false;

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool StartsWithWord(string line, string word) =>
        line.StartsWith(word, StringComparison.Ordinal) && (line.Length == word.Length || IsGap(line[word.Length]));

    private static bool IsBlank(string line) => line.AsSpan().Trim().IsEmpty;

    private static bool IsGap(char letter) => letter is ' ' or '\t';
}
