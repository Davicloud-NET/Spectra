using System.Collections.Generic;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// Size snapping for a resize drag. The increment is a step of world size:
/// one notch grows a brush by one unit whether it started at 0.4 or at 10.
/// </summary>
// The step applies to the size change since the grab, not to the absolute
// size, so fractional sizes stay reachable. Never quantise the scale factor:
// the world step would then depend on the object's size.
public sealed class ResizeSnapSettings : SnapSettings
{
    /// <summary>The default resize step, in world units.</summary>
    public const float DefaultIncrement = 1f;

    // Same rungs as GridSnapSettings, separate instance so a UI can link or split them.
    private static readonly float[] PresetIncrements = [0.25f, 0.5f, 1f, 2f, 4f];

    /// <summary>Creates settings at the <see cref="DefaultIncrement"/> resize step.</summary>
    public ResizeSnapSettings()
        : base(DefaultIncrement, PresetIncrements)
    {
    }

    /// <summary>The selectable resize steps in world units, ascending.</summary>
    public static IReadOnlyList<float> Presets => PresetIncrements;
}
