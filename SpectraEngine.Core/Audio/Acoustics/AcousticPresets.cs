using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace SpectraEngine.Core.Audio.Acoustics;

/// <summary>
/// The presets a material file can name with <c>acoustic = wood</c>.
/// </summary>
// Tuning data, not measurements: a first guess for someone to tune by ear.
// The overall loss is the mass law at 500 Hz for a usual wall of each, cut to
// a quarter so a sound behind it stays audible. The high end keeps the real
// rise to 5 kHz. How a loss splits into surface and metre is a choice.
public static class AcousticPresets
{
    /// <summary>
    /// What a material with no <c>acoustic</c> line gets: halfway between
    /// <see cref="Wood"/> and <see cref="Concrete"/>.
    /// </summary>
    public static AcousticPreset Generic { get; } = new("generic", 8.5f, 10.5f, 21f, 26f);

    /// <summary>Curtains, cloth and padding. Takes a little off the top and nearly nothing else.</summary>
    public static AcousticPreset Fabric { get; } = new("fabric", 1f, 4f, 2.5f, 10f);

    /// <summary>A light inside wall: plasterboard on a frame.</summary>
    public static AcousticPreset Plaster { get; } = new("plaster", 6.5f, 6f, 16f, 15f);

    /// <summary>Doors, planks and crates.</summary>
    public static AcousticPreset Wood { get; } = new("wood", 7f, 7.5f, 17f, 19f);

    /// <summary>A window pane. Quieter behind it, and nearly as bright.</summary>
    public static AcousticPreset Glass { get; } = new("glass", 7.5f, 5f, 19f, 12.5f);

    /// <summary>Sheet metal: doors, lockers, ducts, containers. As loud as through glass, and dull.</summary>
    public static AcousticPreset Metal { get; } = new("metal", 7.5f, 12f, 19f, 30f);

    /// <summary>A brick wall.</summary>
    public static AcousticPreset Brick { get; } = new("brick", 9.5f, 13.5f, 24f, 33.5f);

    /// <summary>A poured wall, a floor slab, a bunker.</summary>
    public static AcousticPreset Concrete { get; } = new("concrete", 10f, 13.5f, 25f, 33.5f);

    /// <summary>Cliffs and cave walls.</summary>
    public static AcousticPreset Rock { get; } = new("rock", 10.5f, 13.5f, 25.5f, 33.5f);

    /// <summary>Every preset, <see cref="Generic"/> first and then from the one that passes most.</summary>
    public static IReadOnlyList<AcousticPreset> All { get; } =
        [Generic, Fabric, Plaster, Wood, Glass, Metal, Brick, Concrete, Rock];

    /// <summary>The names of <see cref="All"/> in one line, for a message.</summary>
    public static string NameList { get; } = string.Join(", ", All.Select(static preset => preset.Name));

    /// <summary>Finds a preset by name. Case does not matter.</summary>
    public static bool TryFind(ReadOnlySpan<char> name, [NotNullWhen(true)] out AcousticPreset? preset)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (!name.Equals(All[i].Name, StringComparison.OrdinalIgnoreCase)) continue;

            preset = All[i];
            return true;
        }

        preset = null;
        return false;
    }
}
