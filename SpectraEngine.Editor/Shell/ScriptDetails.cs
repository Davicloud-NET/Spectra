using System;
using System.Globalization;
using System.IO;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// One line about a script file for the content browser's details strip: how
/// many lines it has.
/// </summary>
public static class ScriptDetails
{
    // A script somebody wrote is a few kilobytes. Past this the file is
    // something else, and counting its lines would read all of it.
    private const long MaxBytes = 4 * 1024 * 1024;

    /// <summary>Describes the script file at <paramref name="fullPath"/>. Reads the disk.</summary>
    public static string Describe(string fullPath)
    {
        try
        {
            if (new FileInfo(fullPath).Length > MaxBytes)
                return string.Empty;

            int lines = 0;
            using var reader = new StreamReader(fullPath);
            while (reader.ReadLine() is not null)
                lines++;

            return Describe(lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Open in an editor that holds a lock, or gone since the listing.
            return string.Empty;
        }
    }

    /// <summary>Describes a script of this many lines.</summary>
    public static string Describe(int lines) => lines switch
    {
        0 => "empty",
        1 => "1 line",
        _ => lines.ToString(CultureInfo.InvariantCulture) + " lines",
    };
}
