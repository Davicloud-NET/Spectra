using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace SpectraEngine.Core.Audio.Captions;

// The words of one WebVTT cue: its lines with the tags taken out and the
// entities read. A voice span at the start gives the speaker.
internal static class SubtitleText
{
    private const int LongestEntity = 8;

    // What an entity's name is made of. "this & that;" has a gap in it, so it is plain words.
    private static readonly SearchValues<char> EntityLetters =
        SearchValues.Create("#0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

    // Reads lines[first..end). Returns the words with the authored line
    // breaks, or nothing when the cue has none.
    public static string Read(
        string[] lines, int first, int end, List<SubtitleUnreadPart> unread, out string? speaker)
    {
        speaker = null;
        var words = new StringBuilder();
        var line = new StringBuilder();

        for (int i = first; i < end; i++)
        {
            line.Clear();
            ReadLine(lines[i], i + 1, hasWords: words.Length > 0, line, unread, ref speaker);

            ReadOnlySpan<char> read = line.ToString().AsSpan().Trim();
            if (read.IsEmpty)
                continue;

            if (words.Length > 0) words.Append('\n');
            words.Append(read);
        }

        return words.ToString();
    }

    private static void ReadLine(
        ReadOnlySpan<char> line,
        int number,
        bool hasWords,
        StringBuilder into,
        List<SubtitleUnreadPart> unread,
        ref string? speaker)
    {
        int at = 0;
        while (at < line.Length)
        {
            ReadOnlySpan<char> rest = line[at..];
            int close = rest[0] == '<' ? rest.IndexOf('>') : -1;

            if (close > 0)
            {
                ReadTag(rest[1..close], number, hasWords, unread, ref speaker);
                at += close + 1;
                continue;
            }

            at += rest[0] == '&' ? ReadEntity(rest, number, into, unread) : AppendLetter(rest[0], into);
            hasWords |= !char.IsWhiteSpace(into[^1]);
        }
    }

    private static int AppendLetter(char letter, StringBuilder into)
    {
        into.Append(letter);
        return 1;
    }

    private static void ReadTag(
        ReadOnlySpan<char> tag, int number, bool hasWords, List<SubtitleUnreadPart> unread, ref string? speaker)
    {
        if (tag is "/v")
            return;

        if (!IsVoice(tag))
        {
            SubtitleReader.NoteUnread(unread, number, NameOf(tag));
            return;
        }

        if (hasWords || speaker is not null)
        {
            SubtitleReader.NoteUnread(unread, number, "a voice span that is not at the start of the words");
            return;
        }

        // <v.loud Guard>: the name is what follows the first gap.
        int gap = tag.IndexOfAny(' ', '\t');
        string name = gap < 0 ? "" : ReadEntities(tag[(gap + 1)..].Trim());
        speaker = name.Length == 0 ? null : name;
    }

    private static bool IsVoice(ReadOnlySpan<char> tag) =>
        tag.Length > 0 && tag[0] == 'v' && (tag.Length == 1 || tag[1] is ' ' or '\t' or '.');

    private static string NameOf(ReadOnlySpan<char> tag)
    {
        if (tag.Length > 0 && char.IsAsciiDigit(tag[0]))
            return "a time inside the words";

        ReadOnlySpan<char> name = tag.TrimStart('/');
        int length = 0;
        while (length < name.Length && char.IsAsciiLetter(name[length]))
            length++;

        return length == 0 ? "a tag" : $"the <{name[..length]}> tag";
    }

    private static string ReadEntities(ReadOnlySpan<char> text)
    {
        if (text.IndexOf('&') < 0)
            return text.ToString();

        var read = new StringBuilder(text.Length);
        int at = 0;
        while (at < text.Length)
            at += text[at] == '&' ? ReadEntity(text[at..], 0, read, null) : AppendLetter(text[at], read);

        return read.ToString();
    }

    // Appends what the entity at the start of text stands for and returns
    // how many letters it took. One the engine does not read stays as written.
    private static int ReadEntity(
        ReadOnlySpan<char> text, int number, StringBuilder into, List<SubtitleUnreadPart>? unread)
    {
        int end = text.IndexOf(';');
        if (end < 2 || end > LongestEntity)
            return AppendLetter('&', into);

        ReadOnlySpan<char> name = text[1..end];
        char? letter = name switch
        {
            "amp" => '&',
            "lt" => '<',
            "gt" => '>',
            "nbsp" => ' ',
            _ => null,
        };

        if (letter is { } known)
        {
            into.Append(known);
            return end + 1;
        }

        if (unread is not null && !name.ContainsAnyExcept(EntityLetters))
            SubtitleReader.NoteUnread(unread, number, $"the entity &{name};");

        return AppendLetter('&', into);
    }
}
