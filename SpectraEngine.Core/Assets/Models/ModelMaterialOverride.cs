using System;
using System.IO;

namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// Maps a material name from a model file to the content path of the
/// <c>.spectramat</c> that overrides it.
/// </summary>
// The engine (at load) and the model cook both use this, so a cooked reference
// and a loose override name the same file.
public static class ModelMaterialOverride
{
    /// <summary>Folder under the content root searched for overrides.</summary>
    public const string Folder = "Materials";

    /// <summary>
    /// The content path an override for <paramref name="materialName"/> would
    /// have, or null when the name cannot address a file. Does not check that
    /// the file exists.
    /// </summary>
    public static string? PathFor(string materialName)
    {
        if (string.IsNullOrWhiteSpace(materialName)) return null;
        if (materialName.AsSpan().IndexOfAny('/', '\\') >= 0) return null;
        if (materialName.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;

        return $"{Folder}/{materialName}{MaterialParser.FileExtension}";
    }
}
