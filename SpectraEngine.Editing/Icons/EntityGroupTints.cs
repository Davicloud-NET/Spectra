using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Editing.Icons;

/// <summary>
/// The colour each entity group is drawn in: a mover's icon is blue wherever
/// it shows. The values are the theme's.
/// </summary>
// This assembly cannot read a theme, so the colours are repeated here. A test
// reads the shell's Tokens.axaml and fails when one differs.
public static class EntityGroupTints
{
    /// <summary>
    /// The colour of a group no row names, as 0xRRGGBB. The theme's
    /// <c>SpectraKindEntity</c>.
    /// </summary>
    public const uint UnknownRgb = 0xB9A5FF;

    private static readonly (string Group, uint Rgb)[] Rows =
    [
        ("Movers", 0x7FB4FF),
        ("Logic", 0xB9A5FF),
        ("Triggers", 0xF2C75C),
        ("Player", 0x6FD3B8),
        ("Sound", 0xE58BD8),
    ];

    private static readonly Vector3[] RowColors = Array.ConvertAll(Rows, row => Linear(row.Rgb));
    private static readonly Vector3 UnknownColor = Linear(UnknownRgb);

    /// <summary>
    /// Every group with a colour of its own, as 0xRRGGBB. The theme has a
    /// <c>SpectraGroup</c> token for each, named after the group.
    /// </summary>
    public static IReadOnlyList<(string Group, uint Rgb)> Table => Rows;

    /// <summary>Whether a group has a colour of its own.</summary>
    public static bool Has(string? group) => IndexOf(group) >= 0;

    /// <summary>
    /// A group's colour in linear light, which is what the 3D view's lines are
    /// given. A group no row names gets the plain entity colour.
    /// </summary>
    public static Vector3 For(string? group)
    {
        int index = IndexOf(group);
        return index >= 0 ? RowColors[index] : UnknownColor;
    }

    private static int IndexOf(string? group)
    {
        for (int i = 0; i < Rows.Length; i++)
        {
            if (string.Equals(Rows[i].Group, group, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static Vector3 Linear(uint rgb) => ColorSpace.SrgbToLinear(new Vector3(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f));
}
