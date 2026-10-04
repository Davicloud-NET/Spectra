using Avalonia.Input;
using SpectraEngine.Core.Assets;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace SpectraEngine.Editor.Shell;

/// <summary>One asset, being dragged out of the content browser.</summary>
/// <param name="ContentPath">
/// The file's path relative to the content root, normalized:
/// forward slashes, no leading separator, no <c>.</c> segments.
/// </param>
// ContentPath is the engine's asset identity, never a filesystem path.
// A class because DataFormat.CreateInProcessFormat requires a reference type.
public sealed record ContentDragPayload(ContentKind Kind, string ContentPath, string Name)
{
    /// <summary>
    /// Turns a browsed file into a payload. False for a folder, a missing root,
    /// or a file outside the root.
    /// </summary>
    public static bool TryCreate(
        string? assetsRoot,
        string fullPath,
        ContentKind kind,
        [NotNullWhen(true)] out ContentDragPayload? payload)
    {
        payload = null;

        if (kind == ContentKind.Folder)
            return false;

        if (string.IsNullOrEmpty(assetsRoot) || string.IsNullOrEmpty(fullPath))
            return false;

        try
        {
            string relative = Path.GetRelativePath(assetsRoot, fullPath);

            // NormalizeRelativePath throws on ".." and on a rooted path, which is
            // what GetRelativePath returns across volumes. Both land in the catch.
            payload = new ContentDragPayload(
                kind, ContentRoot.NormalizeRelativePath(relative), Path.GetFileName(fullPath));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return false;
        }
    }
}

// Separate type so ContentDragPayload has no Avalonia statics and its tests
// run without a UI framework.
internal static class ContentDrag
{
    public static readonly DataFormat<ContentDragPayload> Format =
        DataFormat.CreateInProcessFormat<ContentDragPayload>("spectra-content-asset");
}
