using System;
using SpectraEngine.Core.Assets.Packs;

namespace Spectra.Kitchen.Maps;

/// <summary>
/// A map names one content path as assets of two kinds, such as a script
/// and a model.
/// </summary>
public sealed class ScmapAssetKindException : InvalidOperationException
{
    /// <summary>Creates the exception for a path and the two kinds it was given.</summary>
    public ScmapAssetKindException(string contentPath, PackEntryKind first, PackEntryKind second)
        : base(
            $"Asset '{contentPath}' was added as {first} and again as {second}. One path is one asset, so a " +
            "reader would have to choose which kind it is, and choosing silently is how a material gets " +
            "loaded as a model.")
    {
        ContentPath = contentPath;
        First = first;
        Second = second;
    }

    /// <summary>The normalised content path named twice.</summary>
    public string ContentPath { get; }

    /// <summary>The kind the path was added as first.</summary>
    public PackEntryKind First { get; }

    /// <summary>The kind it was added as after that.</summary>
    public PackEntryKind Second { get; }
}
