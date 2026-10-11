using Avalonia;
using Avalonia.Media;
using Avalonia.VisualTree;

using SpectraEngine.Core.Scene;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Ribbon;

using AvPath = Avalonia.Controls.Shapes.Path;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// A ribbon button that inserts an entity or a light wears the icon the scene
/// tree and the 3D view show for it, whatever size the button is.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class RibbonIconTests(RibbonSession session)
{
    // The ribbon's large box against the box the icon files are drawn on.
    private const double LargeScale = 24.0 / 16.0;

    private static AvPath GlyphOf(RibbonProbe probe, string id) =>
        probe.ByTag(id).GetVisualDescendants().OfType<AvPath>()
            .First(path => path.Classes.Contains("icon") || path.Classes.Contains("icon-lg"));

    private static void ShouldBeTheSmallGlyphScaledUp(AvPath glyph, IconLook look)
    {
        var small = EditorIcons.Glyph(look).ShouldBeOfType<PathGeometry>();
        var large = glyph.Data.ShouldBeOfType<PathGeometry>();

        glyph.Classes.ShouldContain("icon-lg");

        // The small glyph's own figures, not a second file's.
        large.Figures.ShouldBeSameAs(small.Figures);
        large.Transform.ShouldNotBeNull().Value.ShouldBe(Matrix.CreateScale(LargeScale, LargeScale));
        large.Bounds.Width.ShouldBe(small.Bounds.Width * LargeScale, tolerance: 1e-3);
        large.Bounds.Height.ShouldBe(small.Bounds.Height * LargeScale, tolerance: 1e-3);
    }

    [Fact]
    public void The_large_light_button_draws_the_point_lights_icon_scaled_up()
    {
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(RibbonLayout.DefaultTabId);

            ShouldBeTheSmallGlyphScaledUp(GlyphOf(probe, "insert.light"), EditorIcons.ForLight(LightKind.Point));
        });
    }

    [Fact]
    public void The_large_entity_button_draws_the_plain_entity_icon_scaled_up()
    {
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(RibbonLayout.DefaultTabId);

            ShouldBeTheSmallGlyphScaledUp(GlyphOf(probe, "insert.entity"), EditorIcons.PlainEntity);
        });
    }

    [Fact]
    public void The_light_panel_button_wears_the_icon_of_the_light_it_inserts()
    {
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(RibbonLayout.DefaultTabId);

            // It inserts a panel light, and that light's row shows this glyph.
            GlyphOf(probe, "insert.panel").Data
                .ShouldBeSameAs(EditorIcons.Glyph(EditorIcons.ForLight(LightKind.Rect)));
        });
    }

    [Fact]
    public void A_large_glyph_leaves_the_small_one_as_it_was()
    {
        session.On(() =>
        {
            Geometry small = EditorIcons.Glyph(EditorIcons.PlainEntity).ShouldNotBeNull();
            Rect before = small.Bounds;

            EditorIcons.LargeGlyph(EditorIcons.PlainEntity).ShouldNotBeNull().ShouldNotBeSameAs(small);

            small.Bounds.ShouldBe(before);
            small.Transform.ShouldBeNull();
        });
    }
}
