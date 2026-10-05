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
/// whose cues cannot be told apart or timed is refused. Any thread.
/// </remarks>
public static class SubtitleReader
{
    /// <summary>The most parts <see cref="SubtitleFile.Unread"/> lists for one file.</summary>
    public const int MaxUnreadParts = 64;

    private const string Signature = "WEBVTT";
    private const string Arrow = "-->";

    /// <summary>Reads a subtitle file's bytes. A byte order mark at the start is skipped.</summary>
    /// <param name="originForErrors">The file's name, for the message of a refusal.</param>
    /// <exception cref="SubtitleFormatException">The file was refused.</exception>
    public static SubtitleFile Read(ReadOnlySpan<byte> utf8, string originForErrors = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(originForErrors);

        string text = Encoding.UTF8.GetString(CanonicalJson.StripBom(utf8));
        var file = new Reading(text.ReplaceLineEndings("\n").Split('\n'), originForErrors);

        int at = ReadHeader(file);
        while (at < file.Lines.Length)
        {
            if (IsBlank(file.Lines[at]))
            {
                at++;
                continue;
            }

            int end = at;
            while (end < file.Lines.Length && !IsBlank(file.Lines[end]))
                end++;

            ReadBlock(file, at, end);
            at = end;
        }

        return new SubtitleFile(file.Cues, file.CueLines, file.Unread);
    }

    // Names a part once, at the first line that uses it. Past the most a
    // file lists a part is dropped, so the search stays short.
    internal static void NoteUnread(List<SubtitleUnreadPart> unread, int line, string what)
    {
        for (int i = 0; i < unread.Count; i++)
        {
            if (unread[i].What == what)
                return;
        }

        if (unread.Count < MaxUnreadParts)
            unread.Add(new SubtitleUnreadPart(line, what));
    }

    // Returns the index of the first line after the header.
    private static int ReadHeader(Reading file)
    {
        string[] lines = file.Lines;
        if (!StartsWithWord(lines[0], Signature))
        {
            throw new SubtitleFormatException(
                file.Origin, 1, "the first line is not WEBVTT, so this is not a WebVTT file");
        }

        // A cue may follow with no blank line between.
        int at = 1;
        while (at < lines.Length && !IsBlank(lines[at]) && !lines[at].Contains(Arrow, StringComparison.Ordinal))
        {
            NoteUnread(file.Unread, at + 1, "header lines after WEBVTT");
            at++;
        }

        return at;
    }

    // A block is the lines from first up to end, with no blank line among them.
    private static void ReadBlock(Reading file, int first, int end)
    {
        string[] lines = file.Lines;
        int timing = first;
        if (!lines[first].Contains(Arrow, StringComparison.Ordinal))
        {
            if (IsSkippedBlock(lines[first], first + 1, file.Unread))
                return;

            // The first line is the cue's name.
            timing = first + 1;
            if (timing >= end || !lines[timing].Contains(Arrow, StringComparison.Ordinal))
            {
                throw new SubtitleFormatException(
                    file.Origin,
                    first + 1,
                    "this block has no line of times. A cue is an optional name, then a line like " +
                    "00:01.000 --> 00:02.500, then the words");
            }
        }

        ReadTimes(file, timing, out double start, out double stop);

        string words = SubtitleText.Read(lines, timing + 1, end, file.Unread, out string? speaker);
        if (words.Length == 0)
        {
            NoteUnread(file.Unread, timing + 1, "a cue with no words");
            return;
        }

        file.Cues.Add(new CaptionLine(start, stop, speaker, words));
        file.CueLines.Add(timing + 1);
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

    private static void ReadTimes(Reading file, int at, out double start, out double stop)
    {
        string line = file.Lines[at];
        string origin = file.Origin;
        int number = at + 1;

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
            NoteUnread(file.Unread, number, "cue settings after the times");
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

    // One file as it is read: its lines, and what has been made of them.
    private sealed class Reading(string[] lines, string origin)
    {
        public string[] Lines { get; } = lines;

        // The file's name, for the message of a refusal.
        public string Origin { get; } = origin;

        public List<CaptionLine> Cues { get; } = [];

        // The line of the file each cue's times are on, counted from one.
        public List<int> CueLines { get; } = [];

        public List<SubtitleUnreadPart> Unread { get; } = [];
    }
}
