using SpectraEngine.Core.Assets.Sources;
using System;
using System.IO;

namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// Where a model's bytes live: the cooked <c>.smodel</c> beside it when a
/// mounted source has one, otherwise the authored file.
/// </summary>
// Maps and nodes keep naming the source path (Models/x.gltf) after a cook.
// AssetManager and scook verify must both resolve through here.
// An authored model is still read from disk: the native importer opens the
// file itself, so it cannot come out of a pack.
public static class ModelContentPath
{
    /// <summary>Whether <paramref name="contentPath"/> already names a cooked model.</summary>
    public static bool IsCooked(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);
        return contentPath.EndsWith(SmodelFormat.FileExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The cooked path for an authored one: same path, <c>.smodel</c> extension.</summary>
    public static string CookedPathFor(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);

        return IsCooked(contentPath)
            ? contentPath
            : ContentRoot.NormalizeRelativePath(Path.ChangeExtension(contentPath, SmodelFormat.FileExtension));
    }

    /// <summary>
    /// The path to ask <paramref name="source"/> for when a caller wants the
    /// model at <paramref name="contentPath"/>. Falls back to the authored path.
    /// </summary>
    public static string Resolve(IContentSource source, string contentPath)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (IsCooked(contentPath)) return contentPath;

        string cooked = CookedPathFor(contentPath);
        return source.Exists(cooked) ? cooked : contentPath;
    }
}
