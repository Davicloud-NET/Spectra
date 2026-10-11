using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Editing.Icons;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The colour the 3D view is handed for an entity group is the theme's, in
/// linear light. The view's target encodes it back to what the shell shows.
/// </summary>
// Another test holds the table's values to the theme's tokens, and a pixel
// test holds a line given a linear value to the colour it was made from.
public sealed class EntityGroupTintTests
{
    private static Vector3 Linear(uint rgb) => ColorSpace.SrgbToLinear(Written(rgb));

    private static Vector3 Written(uint rgb) =>
        new Vector3((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF) / 255f;

    [Fact]
    public void A_groups_colour_is_its_rows_value_in_linear_light()
    {
        EntityGroupTints.Table.Count.ShouldBeGreaterThan(3);

        foreach ((string group, uint rgb) in EntityGroupTints.Table)
            EntityGroupTints.For(group).ShouldBe(Linear(rgb), group);
    }

    [Fact]
    public void A_groups_colour_encodes_back_to_the_bytes_the_theme_writes()
    {
        foreach ((string group, uint rgb) in EntityGroupTints.Table)
        {
            Vector3 shown = ColorSpace.LinearToSrgb(EntityGroupTints.For(group)) * 255f;

            uint bytes = ((uint)MathF.Round(shown.X) << 16) | ((uint)MathF.Round(shown.Y) << 8) | (uint)MathF.Round(shown.Z);
            bytes.ShouldBe(rgb, group);
        }
    }

    [Fact]
    public void A_group_no_row_names_gets_the_plain_entity_colour_in_linear_light()
    {
        Vector3 plain = Linear(EntityGroupTints.UnknownRgb);

        EntityGroupTints.For(null).ShouldBe(plain);
        EntityGroupTints.For("").ShouldBe(plain);
        EntityGroupTints.For("NoSuchGroup").ShouldBe(plain);
    }
}
