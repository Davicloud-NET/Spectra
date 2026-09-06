using System;
using System.Collections.Generic;
using System.IO;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Where a recent project is, shortened so the part that identifies it survives.
/// </summary>
/// <remarks>
/// <para>
/// <b>A path trimmed from the RIGHT loses exactly the folder that names the
/// project.</b> The start page listed several projects called Demo, each with
/// its path ellipsized at the end, so every row read
/// <c>D:\Users\David\Projects\Sp...</c> and told the reader nothing about which
/// Demo it was. The identifying end is the last segment; the machine-specific
/// start is the part worth dropping.
/// </para>
/// <para>
/// <b>Same-named rows are lengthened until they differ.</b> Two projects called
/// Demo under different parents are the case this exists for, so the shortening
/// grows for that group only, one segment at a time, until either they are
/// distinct or there is nothing left to add. Everything else keeps the short
/// form, because widening every row to disambiguate two of them is how a column
/// stops fitting.
/// </para>
/// </remarks>
public static class RecentLocation
{
    /// <summary>How many trailing folders are kept by default.</summary>
    public const int DefaultSegments = 3;

    /// <summary>What marks a path that was cut.</summary>
    public const string Cut = "\u2026";

    /// <summary>
    /// The last <paramref name="keepSegments"/> folders of a path, marked as cut
    /// when anything was dropped.
    /// </summary>
    public static string Shorten(string? fullPath, int keepSegments)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return string.Empty;
        if (keepSegments < 1) keepSegments = 1;

        // Both separators, because a path may have been typed, pasted from a
        // URL-ish source, or written by another OS; two spellings of one folder
        // would otherwise look like two projects.
        string[] segments = fullPath.Trim().TrimEnd('\\', '/')
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0) return string.Empty;
        if (segments.Length <= keepSegments) return string.Join(Path.DirectorySeparatorChar, segments);

        string kept = string.Join(Path.DirectorySeparatorChar, segments[^keepSegments..]);
        return Cut + Path.DirectorySeparatorChar + kept;
    }

    /// <summary>
    /// One display location per recent project, lengthened where two share a
    /// name and would otherwise read identically.
    /// </summary>
    public static IReadOnlyList<string> Locations(
        IReadOnlyList<RecentProject> recents, int keepSegments = DefaultSegments)
    {
        ArgumentNullException.ThrowIfNull(recents);

        var result = new string[recents.Count];
        for (int i = 0; i < recents.Count; i++)
            result[i] = Shorten(recents[i].Path, keepSegments);

        // Group by NAME, because that is what the reader is trying to tell
        // apart: two rows both saying "Demo" is the failure, and two rows with
        // different names need nothing however alike their paths look.
        var byName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < recents.Count; i++)
        {
            string name = recents[i].Name ?? string.Empty;
            if (!byName.TryGetValue(name, out List<int>? rows)) byName[name] = rows = [];
            rows.Add(i);
        }

        foreach (List<int> rows in byName.Values)
        {
            if (rows.Count < 2) continue;

            for (int depth = keepSegments + 1; depth <= MaxSegments(recents, rows); depth++)
            {
                foreach (int row in rows)
                    result[row] = Shorten(recents[row].Path, depth);

                if (AllDistinct(result, rows)) break;
            }
        }

        return result;
    }

    private static int MaxSegments(IReadOnlyList<RecentProject> recents, List<int> rows)
    {
        int max = 0;
        foreach (int row in rows)
        {
            string path = recents[row].Path ?? string.Empty;
            int count = path.TrimEnd('\\', '/').Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Length;
            if (count > max) max = count;
        }

        return max;
    }

    private static bool AllDistinct(string[] result, List<int> rows)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (int row in rows)
        {
            if (!seen.Add(result[row])) return false;
        }

        return true;
    }
}
