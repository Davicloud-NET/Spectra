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

    private const string LoneBracket = "a < that opens no tag, so it shows as written";

    // What an entity's name is made of. "this & that;" has a gap in it, so it is plain words.
    private static readonly SearchValues<char> EntityLetters =
        SearchValues.Create("#0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

    // Reads lines[first..end). Returns the words with the authored line
    // breaks, or nothing when the cue has none.
    public static string Read(
        string[] lines, int first, int end, List<SubtitleUnreadPart> unread, out string? speaker)
    {
        var cue = new Cue(unread);
        var words = new StringBuilder();

        for (int i = first; i < end; i++)
        {
            cue.Line.Clear();
            cue.Number = i + 1;
            ReadLine(lines[i], cue);

            ReadOnlySpan<char> read = cue.Line.ToString().AsSpan().Trim();
            if (read.IsEmpty)
                continue;

            if (words.Length > 0) words.Append('\n');
            words.Append(read);
        }

        speaker = cue.Speaker;
        return words.ToString();
    }

    private static void ReadLine(ReadOnlySpan<char> line, Cue cue)
    {
        // Where the next > is, or the line's length when there is none. Kept,
        // so a line of nothing but < is searched once and not once for each.
        int nextClose = -1;

        int at = 0;
        while (at < line.Length)
        {
            ReadOnlySpan<char> rest = line[at..];
            if (rest[0] == '<')
            {
                if (nextClose < at)
                {
                    int found = rest.IndexOf('>');
                    nextClose = found < 0 ? line.Length : at + found;
                }

                int close = nextClose - at;
                if (nextClose < line.Length && close > 1 && OpensTag(rest[1]))
                {
                    ReadTag(rest[1..close], cue);
                    at = nextClose + 1;
                    continue;
                }

                // Shown, so no word is lost: "a < b" and a tag with its > left out.
                cue.Note(LoneBracket);
            }

            at += rest[0] == '&' ? ReadEntity(rest, cue.Line, cue) : AppendLetter(rest[0], cue.Line);
            cue.HasWords |= !char.IsWhiteSpace(cue.Line[^1]);
        }
    }

    // A name, a closing slash, or the digits of a time.
    private static bool OpensTag(char letter) => char.IsAsciiLetterOrDigit(letter) || letter == '/';

    private static int AppendLetter(char letter, StringBuilder into)
    {
        into.Append(letter);
        return 1;
    }

    private static void ReadTag(ReadOnlySpan<char> tag, Cue cue)
    {
        if (tag is "/v")
            return;

        if (!IsVoice(tag))
        {
            cue.Note(NameOf(tag));
            return;
        }

        if (cue.HasWords || cue.Speaker is not null)
        {
            cue.Note("a voice span that is not at the start of the words");
            return;
        }

        // <v.loud Guard>: the name is what follows the first gap.
        int gap = tag.IndexOfAny(' ', '\t');
        string name = gap < 0 ? "" : ReadEntities(tag[(gap + 1)..].Trim());
        cue.Speaker = name.Length == 0 ? null : name;
    }

    private static bool IsVoice(ReadOnlySpan<char> tag) =>
        tag.Length > 0 && tag[0] == 'v' && (tag.Length == 1 || tag[1] is ' ' or '\t' or '.');

    private static string NameOf(ReadOnlySpan<char> tag)
    {
        if (char.IsAsciiDigit(tag[0]))
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
            at += text[at] == '&' ? ReadEntity(text[at..], read, null) : AppendLetter(text[at], read);

        return read.ToString();
    }

    // Appends what the entity at the start of text stands for and returns
    // how many letters it took. One the engine does not read stays as written.
    private static int ReadEntity(ReadOnlySpan<char> text, StringBuilder into, Cue? cue)
    {
        // No further than an entity can reach, or a line of & is searched to
        // its end once for each.
        int end = text[..Math.Min(text.Length, LongestEntity + 1)].IndexOf(';');
        if (end < 2)
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

        if (cue is not null && !name.ContainsAnyExcept(EntityLetters))
            cue.Note($"the entity &{name};");

        return AppendLetter('&', into);
    }

    // One cue as it is read.
    private sealed class Cue(List<SubtitleUnreadPart> unread)
    {
        // The line being read, with its tags taken out.
        public StringBuilder Line { get; } = new();

        // That line's number in the file, counted from one.
        public int Number { get; set; }

        public string? Speaker { get; set; }

        // Whether any word has been read yet, on this line or an earlier one.
        public bool HasWords { get; set; }

        public void Note(string what) => SubtitleReader.NoteUnread(unread, Number, what);
    }
}
