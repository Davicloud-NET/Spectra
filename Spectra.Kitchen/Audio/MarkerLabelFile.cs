using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Spectra.Kitchen.Audio;

/// <summary>
/// Reads markers from a text file beside a sound, for audio editors that
/// cannot write cue points into the WAV. A line is a time in seconds, a tab and
/// the name. The layout Audacity exports a label track in has an end time
/// between the two, and is read as well.
/// </summary>
public static class MarkerLabelFile
{
    /// <summary>What replaces a sound's extension to name its label file.</summary>
    public const string Suffix = ".markers.txt";

    // No WAV is this long. The cap keeps a wild time from overflowing the
    // frame conversion later.
    private const double MaxFrames = uint.MaxValue;

    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>
    /// The label file for the sound at <paramref name="soundPath"/>:
    /// <c>Sounds/door.wav</c> gives <c>Sounds/door.markers.txt</c>.
    /// </summary>
    public static string PathFor(string soundPath)
    {
        ArgumentNullException.ThrowIfNull(soundPath);
        return Path.ChangeExtension(soundPath, null) + Suffix;
    }

    /// <summary>
    /// Parses a label file. A region label gives a marker at its start. A line
    /// that cannot be read is skipped and its one-based number is added to
    /// <paramref name="unreadableLines"/>.
    /// </summary>
    /// <param name="sampleRate">The rate the returned frames are counted at.</param>
    public static SourceMarker[] Read(ReadOnlySpan<byte> file, int sampleRate, List<int> unreadableLines)
    {
        ArgumentNullException.ThrowIfNull(unreadableLines);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        string text = Encoding.UTF8.GetString(file);
        var markers = new List<SourceMarker>();

        int number = 0;
        foreach (ReadOnlySpan<char> raw in text.AsSpan().EnumerateLines())
        {
            number++;

            // A byte order mark would otherwise sit in front of the first time.
            ReadOnlySpan<char> line = number == 1 ? raw.TrimStart(ByteOrderMark) : raw;

            // A line starting with a backslash holds a label's frequency range.
            if (line.IsWhiteSpace() || line[0] == '\\') continue;

            if (TryReadLine(line, sampleRate, out SourceMarker marker)) markers.Add(marker);
            else unreadableLines.Add(number);
        }

        return markers.ToArray();
    }

    private static bool TryReadLine(ReadOnlySpan<char> line, int sampleRate, out SourceMarker marker)
    {
        marker = default;

        int firstTab = line.IndexOf('\t');
        if (firstTab < 0) return false;

        if (!TryReadSeconds(line[..firstTab], out double seconds) || seconds < 0) return false;

        ReadOnlySpan<char> rest = line[(firstTab + 1)..];
        int secondTab = rest.IndexOf('\t');
        ReadOnlySpan<char> second = secondTab < 0 ? rest : rest[..secondTab];

        ReadOnlySpan<char> label;
        if (second.IsWhiteSpace() || TryReadSeconds(second, out _))
        {
            // An end time, which is not used. The name is whatever follows it.
            label = secondTab < 0 ? default : rest[(secondTab + 1)..];
        }
        else if (secondTab < 0)
        {
            // Written by hand: a time and a name.
            label = second;
        }
        else
        {
            // Three columns with words in the middle. Either could be the name.
            return false;
        }

        double frames = Math.Round(seconds * sampleRate, MidpointRounding.AwayFromZero);
        marker = new SourceMarker((long)Math.Min(frames, MaxFrames), label.Trim().ToString());
        return true;
    }

    private static bool TryReadSeconds(ReadOnlySpan<char> text, out double seconds) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) &&
        double.IsFinite(seconds);
}
