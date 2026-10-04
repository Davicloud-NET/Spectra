using System;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// Whole-pack properties, as the <see cref="PackHeader.Flags"/> word.
/// </summary>
[Flags]
public enum PackFlags : uint
{
    /// <summary>No flags. Not a legal v1 header, which requires
    /// <see cref="EntriesSortedByAssetId"/>.</summary>
    None = 0,

    /// <summary>
    /// The entry table is sorted ascending by <see cref="PackEntry.AssetId"/>
    /// compared as an unsigned 128-bit value. Required in v1, so a reader can
    /// refuse an unsorted table instead of binary-searching it.
    /// </summary>
    EntriesSortedByAssetId = 1u << 0,

    /// <summary>
    /// A patch pack: mounted above the base band and ordered among its peers by
    /// <see cref="PackHeader.PackSequence"/>.
    /// </summary>
    IsPatchPack = 1u << 1,

    /// <summary>
    /// A mod pack: mounted above the patch band and ordered by the user's list.
    /// </summary>
    IsModPack = 1u << 2,

    /// <summary>
    /// A name table is present. Emitted by default, so logs and inspect output
    /// can show paths instead of ids.
    /// </summary>
    NameTablePresent = 1u << 3,
}
