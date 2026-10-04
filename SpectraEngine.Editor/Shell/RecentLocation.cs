using System;
using System.Collections.Generic;
using System.IO;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Shortens a recent project's path from the left, so the folders that
/// identify it survive. Same-named projects get more segments until they differ.
/// </summary>
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

        // Both separators: the path may come from another OS.
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

        // Only rows sharing a name need lengthening.
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
