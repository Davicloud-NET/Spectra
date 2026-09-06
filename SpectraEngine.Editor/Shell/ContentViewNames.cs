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
/// <param name="Label">The folder's own name, or "Assets" for the root.</param>
/// <param name="FullPath">Where clicking it goes.</param>
public readonly record struct BreadcrumbSegment(string Label, string FullPath);

/// <summary>
/// The words the content view's settings are written as.
/// </summary>
/// <remarks>
/// <b>Hand-written both ways, and an unknown word falls back rather than
/// failing the file.</b> Reflecting over an enum is what trimming removes, and a
/// settings file written by a newer shell must lose the setting it cannot read
/// rather than every setting beside it.
/// </remarks>
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
