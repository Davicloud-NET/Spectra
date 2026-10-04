using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>Which kinds the content browser lists.</summary>
public enum ContentFilter
{
    All,
    Textures,
    Materials,
    Models,
}

/// <summary>Tiles or dense rows.</summary>
public enum ContentViewMode
{
    /// <summary>Tiles with previews. Capped, because it does not virtualise.</summary>
    Grid,

    /// <summary>One row per file. Virtualises, so it has no cap.</summary>
    List,
}

/// <summary>One clickable step of the path from the assets root to here.</summary>
public readonly record struct BreadcrumbSegment(string Label, string FullPath);

/// <summary>The words the content view's settings are written as.</summary>
// Hand-written, not enum reflection (trimming). An unknown word falls back to
// the grid so a newer shell's settings file still loads.
public static class ContentViewNames
{
    public static string NameOf(ContentViewMode mode) => mode switch
    {
        ContentViewMode.List => "list",
        _ => "grid",
    };

    public static bool TryParse(string? word, out ContentViewMode mode)
    {
        mode = ContentViewMode.Grid;

        if (string.IsNullOrWhiteSpace(word)) return false;

        if (string.Equals(word, "list", StringComparison.OrdinalIgnoreCase))
        {
            mode = ContentViewMode.List;
            return true;
        }

        return string.Equals(word, "grid", StringComparison.OrdinalIgnoreCase);
    }
}
