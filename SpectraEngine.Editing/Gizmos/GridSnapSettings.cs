using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// World-grid snapping for a translate drag: the grid step and what a snapped
/// drag quantises (<see cref="Mode"/>).
/// </summary>
public sealed class GridSnapSettings : SnapSettings
{
    /// <summary>The default grid step, in world units.</summary>
    public const float DefaultIncrement = 1f;

    /// <summary>
    /// What a snapped drag quantises: the displacement (default) or the
    /// reference node's absolute destination.
    /// </summary>
    // Delta is what Studio and Blender do: a part at 3.7 moved one notch
    // lands at 4.7. AbsoluteGrid is Hammer's model.
    public TranslateSnapMode Mode { get; set; } = TranslateSnapMode.Delta;

    private static readonly float[] PresetIncrements = [0.25f, 0.5f, 1f, 2f, 4f];

    /// <summary>Creates settings at the <see cref="DefaultIncrement"/> grid step.</summary>
    public GridSnapSettings()
        : base(DefaultIncrement, PresetIncrements)
    {
    }

    /// <summary>The selectable grid steps, ascending.</summary>
    public static IReadOnlyList<float> Presets => PresetIncrements;

    /// <summary>
    /// Rounds only the components <paramref name="axisMask"/> marks non-zero.
    /// A constrained drag must not quantise axes it never moved.
    /// </summary>
    public Vector3 SnapMasked(Vector3 value, Vector3 axisMask) => new(
        axisMask.X != 0f ? SnapScalar(value.X) : value.X,
        axisMask.Y != 0f ? SnapScalar(value.Y) : value.Y,
        axisMask.Z != 0f ? SnapScalar(value.Z) : value.Z);
}
