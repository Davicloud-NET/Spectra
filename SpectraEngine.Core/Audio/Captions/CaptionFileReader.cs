using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace SpectraEngine.Core.Audio.Captions;

/// <summary>
/// Reads a caption file: one sound caption on a line, as the sound's content
/// path, an equals sign and the words.
/// </summary>
/// <remarks>
/// <code>
/// // Captions/en.txt
/// Sounds/door_open.wav = Door opens
/// Sounds/lift_hum.wav = Lift hums
/// </code>
/// UTF-8. A line that starts with <c>//</c> is a comment, and blank lines are
/// fine. The words are everything after the first equals sign, and
/// <c>\n</c> in them is a line break. Nothing in a file is fatal: a line that
/// cannot be read becomes a problem and the rest is still read. Any thread.
/// </remarks>
public static class CaptionFileReader
{
    private const string Comment = "//";
    private const string LineBreak = "\\n";

    /// <summary>Reads a caption file's bytes. A byte order mark at the start is skipped.</summary>
    public static CaptionFile Read(ReadOnlySpan<byte> utf8)
    {
        string text = Encoding.UTF8.GetString(CanonicalJson.StripBom(utf8));

        var entries = new List<CaptionFileEntry>();
        var bySound = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<CaptionFileProblem>();

        int number = 0;
        foreach (ReadOnlySpan<char> rawLine in text.AsSpan().EnumerateLines())
        {
            number++;
            ReadOnlySpan<char> line = rawLine.Trim();
            if (line.IsEmpty || line.StartsWith(Comment, StringComparison.Ordinal))
                continue;

            if (!TryReadLine(line, out string sound, out string words, out string refusal))
            {
                problems.Add(new CaptionFileProblem(number, CaptionFileProblemKind.NotACaption, refusal));
                continue;
            }

            var entry = new CaptionFileEntry(sound, words, number);
            if (!bySound.TryGetValue(sound, out int at))
            {
                bySound.Add(sound, entries.Count);
                entries.Add(entry);
                continue;
            }

            problems.Add(new CaptionFileProblem(
                number,
                CaptionFileProblemKind.Repeated,
                $"'{sound}' has a caption on line {entries[at].Line} already. The one on this line is used"));
            entries[at] = entry;
        }

        return new CaptionFile(entries, bySound, problems);
    }

    private static bool TryReadLine(ReadOnlySpan<char> line, out string sound, out string words, out string refusal)
    {
        sound = words = "";

        int equals = line.IndexOf('=');
        if (equals < 0)
        {
            refusal = "this line is not a caption. Write the sound's path, an equals sign and the words, " +
                "like Sounds/door_open.wav = Door opens";
            return false;
        }

        ReadOnlySpan<char> path = line[..equals].Trim();
        if (path.IsEmpty)
        {
            refusal = "there is no sound path before the equals sign";
            return false;
        }

        words = WithLineBreaks(line[(equals + 1)..].Trim());
        if (words.Length == 0)
        {
            refusal = "there are no words after the equals sign";
            return false;
        }

        try
        {
            sound = ContentRoot.NormalizeRelativePath(path.ToString());
        }
        catch (ArgumentException)
        {
            refusal = $"'{path}' is not a path inside the project's content";
            return false;
        }

        refusal = "";
        return true;
    }

    // Each part is trimmed, so "one \n two" has no space at its line break.
    private static string WithLineBreaks(ReadOnlySpan<char> text)
    {
        if (text.IndexOf(LineBreak, StringComparison.Ordinal) < 0)
            return text.ToString();

        var words = new StringBuilder(text.Length);
        while (true)
        {
            int cut = text.IndexOf(LineBreak, StringComparison.Ordinal);
            ReadOnlySpan<char> part = (cut < 0 ? text : text[..cut]).Trim();

            if (!part.IsEmpty)
            {
                if (words.Length > 0) words.Append('\n');
                words.Append(part);
            }

            if (cut < 0)
                return words.ToString();

            text = text[(cut + LineBreak.Length)..];
        }
    }
}
