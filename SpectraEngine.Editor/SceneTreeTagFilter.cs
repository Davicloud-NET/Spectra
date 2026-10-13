using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Editor;

/// <summary>
/// The scene tree's filter by tag: <c>tag:lava</c> finds every node with a
/// tag that has "lava" in it, in any case, and <c>tag:</c> alone finds every
/// node that has a tag at all.
/// </summary>
// It finds by a part of a tag, as the filter by name finds by a part of a
// name: the list narrows while the word is still being typed.
public static class SceneTreeTagFilter
{
    /// <summary>The prefix that makes a filter one by tag. Any case.</summary>
    public const string Prefix = "tag:";

    /// <summary>
    /// Whether a filter text asks for a tag, and the text to look for in the
    /// tags. That text is empty for the prefix alone.
    /// </summary>
    public static bool TryRead(string filter, out string wanted)
    {
        ArgumentNullException.ThrowIfNull(filter);

        bool isByTag = filter.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
        wanted = isByTag ? filter[Prefix.Length..].Trim() : string.Empty;
        return isByTag;
    }

    /// <summary>Whether one of the tags has the text in it. No text finds any tag.</summary>
    public static bool Matches(TagSet tags, string wanted)
    {
        ArgumentNullException.ThrowIfNull(tags);

        if (string.IsNullOrEmpty(wanted))
            return tags.Count > 0;

        foreach (string tag in tags)
        {
            if (tag.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
