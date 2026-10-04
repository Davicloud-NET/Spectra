using SpectraEngine.Core.Input;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// Snapping shared by every manipulator: on or off, the step size, the preset
/// ladder, and the modifier that inverts the setting for one gesture.
/// Render thread only.
/// </summary>
// The increment is always an absolute quantity of the thing edited (units,
// degrees, size), never a multiplier, so it means the same at every object size.
public abstract class SnapSettings
{
    private readonly float[] _increments;
    private float _increment;

    /// <summary>
    /// Creates settings starting at <paramref name="defaultIncrement"/> with the
    /// ascending ladder <paramref name="increments"/>, which is copied.
    /// </summary>
    protected SnapSettings(float defaultIncrement, params float[] increments)
    {
        ArgumentNullException.ThrowIfNull(increments);
        if (increments.Length == 0)
            throw new ArgumentException("A snap ladder needs at least one increment.", nameof(increments));

        _increments = (float[])increments.Clone();
        Increment = defaultIncrement;
    }

    /// <summary>
    /// Whether snapping applies by default. <see cref="ToggleModifier"/> inverts
    /// this per gesture.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The step size, in the subclass's unit. Must be positive; the setter
    /// throws otherwise.
    /// </summary>
    public float Increment
    {
        get => _increment;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _increment = value;
        }
    }

    /// <summary>
    /// The modifier that inverts <see cref="Enabled"/> while held.
    /// <see cref="KeyModifiers.None"/> disables the override.
    /// </summary>
    // Alt: Shift and Control are taken by the selection modifiers.
    public KeyModifiers ToggleModifier { get; set; } = KeyModifiers.Alt;

    /// <summary>The selectable increments, ascending. <see cref="CyclePreset"/> walks this ladder.</summary>
    public IReadOnlyList<float> Increments => _increments;

    /// <summary>
    /// Whether snapping applies with these modifiers held: <see cref="Enabled"/>,
    /// inverted while <see cref="ToggleModifier"/> is down.
    /// </summary>
    public bool IsActiveWith(KeyModifiers held)
    {
        bool overridden = ToggleModifier != KeyModifiers.None && (held & ToggleModifier) == ToggleModifier;
        return Enabled ^ overridden;
    }

    /// <summary>
    /// Rounds one value to the nearest multiple of <see cref="Increment"/>.
    /// Halves round away from zero.
    /// </summary>
    public float SnapScalar(float value) =>
        MathF.Round(value / _increment, MidpointRounding.AwayFromZero) * _increment;

    /// <summary>
    /// Steps <see cref="Increment"/> along <see cref="Increments"/> by
    /// <paramref name="direction"/> rungs (negative for finer, positive for
    /// coarser) and returns the new value. An increment that is not a preset
    /// starts from its nearest rung, and the ends clamp.
    /// </summary>
    public float CyclePreset(int direction)
    {
        int index = NearestIncrementIndex(_increment) + direction;
        Increment = _increments[Math.Clamp(index, 0, _increments.Length - 1)];
        return _increment;
    }

    /// <summary>Selects <see cref="Increments"/>[<paramref name="index"/>] as the current increment.</summary>
    public void SelectPreset(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _increments.Length);
        Increment = _increments[index];
    }

    // Nearest by ratio, since the ladders are roughly geometric: 3 is closer
    // to 4 than to 2, though it is one away from both.
    private int NearestIncrementIndex(float increment)
    {
        int best = 0;
        float bestRatio = float.PositiveInfinity;
        for (int i = 0; i < _increments.Length; i++)
        {
            float ratio = increment > _increments[i]
                ? increment / _increments[i]
                : _increments[i] / increment;
            if (ratio < bestRatio)
            {
                bestRatio = ratio;
                best = i;
            }
        }
        return best;
    }
}
