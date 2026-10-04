using System;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>Angle snapping for a rotate drag. The increment is in degrees.</summary>
public sealed class AngleSnapSettings : SnapSettings
{
    /// <summary>The default rotation increment, in degrees.</summary>
    public const float DefaultIncrementDegrees = 15f;

    private static readonly float[] PresetIncrements = [5f, 15f, 45f, 90f];

    /// <summary>Creates settings at the <see cref="DefaultIncrementDegrees"/> increment.</summary>
    public AngleSnapSettings()
        : base(DefaultIncrementDegrees, PresetIncrements)
    {
    }

    /// <summary>The selectable increments in degrees, ascending.</summary>
    public static IReadOnlyList<float> Presets => PresetIncrements;

    /// <summary>
    /// Rounds an angle in radians to the nearest multiple of
    /// <see cref="SnapSettings.Increment"/> degrees. Returns radians.
    /// </summary>
    public float SnapRadians(float radians)
    {
        // Round in degrees so four 15 degree steps land on 60, not a float near it.
        const float toDegrees = 180f / MathF.PI;
        return SnapScalar(radians * toDegrees) / toDegrees;
    }
}
