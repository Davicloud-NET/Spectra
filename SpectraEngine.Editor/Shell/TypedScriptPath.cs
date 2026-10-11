using SpectraEngine.Core.Assets;
using System;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Reads a script's path as somebody typed it into the picker, for a file
/// the project does not list.
/// </summary>
public static class TypedScriptPath
{
    private const string Extension = ContentClassifier.ScriptExtension;

    /// <summary>
    /// The path a level would store for what was typed. False when it is not
    /// a script's path inside the Assets folder.
    /// </summary>
    public static bool TryRead(string? typed, [NotNullWhen(true)] out string? contentPath)
    {
        contentPath = null;

        string text = typed?.Trim() ?? string.Empty;
        if (text.Length <= Extension.Length || !text.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            return false;

        string normalized;
        try
        {
            normalized = ContentRoot.NormalizeRelativePath(text);
        }
        catch (ArgumentException)
        {
            // Rooted, or it leaves the Assets folder.
            return false;
        }

        // ".luau" alone in a folder is an extension with no name.
        if (normalized.EndsWith("/" + Extension, StringComparison.OrdinalIgnoreCase))
            return false;

        contentPath = normalized;
        return true;
    }
}
