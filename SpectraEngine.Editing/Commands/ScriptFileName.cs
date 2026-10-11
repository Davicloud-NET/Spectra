using System;
using System.Globalization;
using System.Text;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Names the file a new script gets: after the node the script goes under,
/// with a number when that name is taken.
/// </summary>
// Pure. Whether a name is taken is asked of the caller, who knows the disk.
public static class ScriptFileName
{
    /// <summary>The extension a script file has, with its dot.</summary>
    public const string Extension = ".luau";

    /// <summary>The folder under the content root that new scripts go into.</summary>
    public const string Folder = "Scripts";

    /// <summary>What a script is called when its parent's name gives nothing to go on.</summary>
    public const string Fallback = "Script";

    // Long enough for any name somebody would read, short enough to stay
    // clear of the path limit under a deep project folder.
    private const int MaxStemLength = 48;

    // Windows keeps these for devices, with any extension and in any case.
    private static readonly string[] DeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// The file's name without its extension, from a node's name. Letters,
    /// digits, <c>-</c> and <c>_</c> are kept and every run of anything else
    /// becomes one <c>_</c>, so the name is legal on every system.
    /// </summary>
    public static string StemFor(string? parentName)
    {
        var stem = new StringBuilder();

        foreach (char c in parentName ?? string.Empty)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
                stem.Append(c);
            else if (stem.Length > 0 && stem[^1] != '_')
                stem.Append('_');

            if (stem.Length >= MaxStemLength)
                break;
        }

        string trimmed = stem.ToString().Trim('_', '-');
        if (trimmed.Length == 0)
            return Fallback;

        return IsDeviceName(trimmed) ? trimmed + "_" : trimmed;
    }

    /// <summary>
    /// The first of <c>Stem.luau</c>, <c>Stem2.luau</c>, <c>Stem3.luau</c> and so
    /// on that is free. Null when every one up to <paramref name="limit"/> is taken.
    /// </summary>
    /// <param name="isTaken">Whether a file name is in use.</param>
    public static string? FirstFree(string stem, Func<string, bool> isTaken, int limit = 1000)
    {
        ArgumentException.ThrowIfNullOrEmpty(stem);
        ArgumentNullException.ThrowIfNull(isTaken);

        // "Door1" and a 2 would read as Door12.
        string numbered = char.IsDigit(stem[^1]) ? stem + "_" : stem;

        for (int number = 1; number <= limit; number++)
        {
            string name = number == 1
                ? stem + Extension
                : numbered + number.ToString(CultureInfo.InvariantCulture) + Extension;

            if (!isTaken(name))
                return name;
        }

        return null;
    }

    private static bool IsDeviceName(string stem)
    {
        foreach (string device in DeviceNames)
        {
            if (string.Equals(stem, device, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
