using Spectra.Kitchen.Audio;
using System.Globalization;
using System.IO;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// One line about a sound file for the content browser's details strip: how
/// long it is, mono or stereo, its sample rate, and whether it has a loop
/// region or markers.
/// </summary>
// Read from the WAV's own header, not from a cook: selecting a file must not
// cook it.
public static class SoundDetails
{
    /// <summary>What the strip says about a file that is not a WAV the cook reads.</summary>
    public const string Unreadable = "This file cannot be read as a sound. The engine reads uncompressed WAV.";

    /// <summary>Describes the sound file at <paramref name="fullPath"/>. Reads the disk.</summary>
    public static string Describe(string fullPath)
    {
        if (!WaveHeader.TryReadFile(fullPath, out WaveHeader header))
            return Unreadable;

        // The cook reads this file only when the WAV has no markers of its own.
        bool hasMarkerFile = header.MarkerCount == 0 && File.Exists(MarkerLabelFile.PathFor(fullPath));

        return Describe(in header, hasMarkerFile);
    }

    /// <summary>Describes a sound from its header.</summary>
    /// <param name="hasMarkerFile">Whether a marker text file sits beside it.</param>
    public static string Describe(in WaveHeader header, bool hasMarkerFile = false) =>
        $"{Length(header.Seconds)}  {ChannelWord(header.Channels)}  " +
        $"{header.SampleRate.ToString(CultureInfo.InvariantCulture)} Hz  {Extras(in header, hasMarkerFile)}";

    private static string Length(double seconds)
    {
        // A click is a few thousandths long, and two places would call it nothing.
        if (seconds < 1d)
            return seconds.ToString("0.###", CultureInfo.InvariantCulture) + " s";

        if (seconds < 60d)
            return seconds.ToString("0.##", CultureInfo.InvariantCulture) + " s";

        int whole = (int)seconds;
        return string.Create(CultureInfo.InvariantCulture, $"{whole / 60} min {whole % 60:00} s");
    }

    private static string ChannelWord(int channels) => channels switch
    {
        1 => "mono",
        2 => "stereo",
        _ => channels.ToString(CultureInfo.InvariantCulture) + " channels",
    };

    private static string Extras(in WaveHeader header, bool hasMarkerFile)
    {
        string markers = header.MarkerCount switch
        {
            0 => hasMarkerFile ? "markers in a text file" : string.Empty,
            1 => "1 marker",
            _ => header.MarkerCount.ToString(CultureInfo.InvariantCulture) + " markers",
        };

        if (header.HasLoop)
            return markers.Length > 0 ? "loop region, " + markers : "loop region";

        return markers.Length > 0 ? markers : "no loop region or markers";
    }
}
