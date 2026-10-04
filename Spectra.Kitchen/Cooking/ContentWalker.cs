using SpectraEngine.Core.Assets;
using System;
using System.Collections.Generic;
using System.IO;

namespace Spectra.Kitchen.Cooking;

/// <summary>One authored file the cook found, by both of its names.</summary>
/// <param name="ContentPath">Normalised content-relative path, the asset's identity.</param>
/// <param name="FullPath">Where it is on this machine.</param>
public readonly record struct ContentFile(string ContentPath, string FullPath);

/// <summary>
/// Finds the authored content under a project's content root.
/// </summary>
public static class ContentWalker
{
    /// <summary>
    /// Every file under <paramref name="contentRoot"/>, as content-relative paths
    /// in ordinal order. An absent root gives an empty list.
    /// </summary>
    public static IReadOnlyList<ContentFile> Walk(string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        string root = Path.GetFullPath(contentRoot);
        if (!Directory.Exists(root)) return [];

        var found = new List<ContentFile>();
        foreach (string full in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, full);

            string content;
            try
            {
                content = ContentRoot.NormalizeRelativePath(relative);
            }
            catch (ArgumentException)
            {
                // One odd name should not stop the cook. Verify catches the gap.
                continue;
            }

            found.Add(new ContentFile(content, full));
        }

        // EnumerateFiles has no documented order, and the manifest and
        // diagnostics follow this one.
        found.Sort(static (a, b) => string.CompareOrdinal(a.ContentPath, b.ContentPath));
        return found;
    }
}
