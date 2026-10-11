using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

/// <summary>The words the content browser's kind filter is shown as, in the order it shows them.</summary>
// Hand-written, not enum reflection: trimming removes enum names.
public static class ContentFilterNames
{
    /// <summary>Every filter and its word, in chip order.</summary>
    public static IReadOnlyList<(ContentFilter Filter, string Word)> All { get; } =
    [
        (ContentFilter.All, "All"),
        (ContentFilter.Textures, "Textures"),
        (ContentFilter.Materials, "Materials"),
        (ContentFilter.Models, "Models"),
        (ContentFilter.Sounds, "Sounds"),
        (ContentFilter.Scripts, "Scripts"),
    ];

    /// <summary>The word for a filter.</summary>
    public static string WordFor(ContentFilter filter)
    {
        foreach ((ContentFilter candidate, string word) in All)
        {
            if (candidate == filter)
                return word;
        }

        return All[0].Word;
    }

    /// <summary>The filter a word names. A word that names none means everything.</summary>
    public static ContentFilter Parse(string? word)
    {
        foreach ((ContentFilter filter, string candidate) in All)
        {
            if (string.Equals(candidate, word, StringComparison.Ordinal))
                return filter;
        }

        return ContentFilter.All;
    }
}
