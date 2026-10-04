namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// The priority bands a mount stack is ordered by. Higher wins.
/// </summary>
// Spaced by a hundred so packs can be ordered inside a band as an offset from its floor.
public static class PackMountBand
{
    /// <summary>Base packs: what the game shipped with.</summary>
    public const int Base = 0;

    /// <summary>Patch packs, ordered among themselves by <see cref="PackHeader.PackSequence"/>.</summary>
    public const int Patch = 100;

    /// <summary>Mod packs, ordered among themselves by the user's list.</summary>
    public const int Mod = 200;

    /// <summary>Loose files, above every pack so an edited file shadows the cooked one.</summary>
    public const int Loose = 1000;
}
