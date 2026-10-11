using System;
using System.IO;
using System.Linq;
using System.Numerics;
using SpectraEngine.Editing.Icons;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The icon files as the 3D view draws them: every file has a glyph, and a
/// glyph is strokes inside a unit box.
/// </summary>
public sealed class ViewportGlyphTests
{
    private const string Svg = """
        <?xml version="1.0" encoding="utf-8"?>
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16">
          <path d="{0}" />
        </svg>
        """;

    private static ViewportGlyph Read(string data) =>
        ViewportGlyphFile.Read(Svg.Replace("{0}", data, StringComparison.Ordinal), ViewportGlyphs.Flatness)
            .ShouldNotBeNull();

    [Fact]
    public void Every_icon_file_in_the_folder_is_embedded()
    {
        // A file the glob missed would be in the shell and not in the 3D view.
        string[] onDisk = Directory
            .EnumerateFiles(Path.Combine(SourceRoot(), "SpectraEngine.Editing", "Assets", "Icons"), "*.svg")
            .Select(file => Path.GetFileNameWithoutExtension(file))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        onDisk.Length.ShouldBeGreaterThan(20);
        WorldIconFiles.Keys.ShouldBe(onDisk);
    }

    [Fact]
    public void Every_icon_file_has_a_glyph_with_strokes_inside_the_unit_box()
    {
        ViewportGlyphs.Count.ShouldBe(WorldIconFiles.Keys.Count);

        foreach (string key in WorldIconFiles.Keys)
        {
            ViewportGlyphs.TryGet(key, out ViewportGlyph? glyph).ShouldBeTrue(key);
            ViewportGlyph drawn = glyph.ShouldNotBeNull();

            drawn.StrokeCount.ShouldBeGreaterThan(0, key);
            drawn.SegmentCount.ShouldBeGreaterThan(0, key);

            foreach (Vector2 point in drawn.Points)
            {
                point.X.ShouldBeInRange(0f, 1f, key);
                point.Y.ShouldBeInRange(0f, 1f, key);
            }
        }
    }

    [Fact]
    public void A_key_no_file_has_gets_no_glyph()
    {
        ViewportGlyphs.TryGet("IconEntityNoSuchThing", out ViewportGlyph? glyph).ShouldBeFalse();
        glyph.ShouldBeNull();
        WorldIconFiles.Has("IconEntityNoSuchThing").ShouldBeFalse();
    }

    [Fact]
    public void A_glyph_is_its_files_paths_scaled_to_the_unit_box()
    {
        ViewportGlyph glyph = Read("M 4 4 h 8 v 8");

        glyph.StrokeCount.ShouldBe(1);
        glyph.SegmentCount.ShouldBe(2);
        glyph.Stroke(0).ToArray().ShouldBe(
            [new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.25f), new Vector2(0.75f, 0.75f)]);
    }

    [Fact]
    public void Every_path_in_a_file_is_a_stroke_of_the_glyph()
    {
        ViewportGlyph? glyph = ViewportGlyphFile.Read(
            """
            <svg viewBox="0 0 16 16">
              <path id="first" d="M 0 0 h 16" />
              <path d="M 0 16 h 16" />
            </svg>
            """,
            ViewportGlyphs.Flatness);

        glyph.ShouldNotBeNull().StrokeCount.ShouldBe(2);
        glyph.Stroke(1).ToArray().ShouldBe([new Vector2(0f, 1f), new Vector2(1f, 1f)]);
    }

    [Fact]
    public void A_box_that_is_not_square_keeps_its_shape_and_is_centred()
    {
        ViewportGlyph? glyph = ViewportGlyphFile.Read(
            """<svg viewBox="0 0 16 8"><path d="M 0 0 h 16 v 8" /></svg>""", ViewportGlyphs.Flatness);

        glyph.ShouldNotBeNull().Stroke(0).ToArray().ShouldBe(
            [new Vector2(0f, 0.25f), new Vector2(1f, 0.25f), new Vector2(1f, 0.75f)]);
    }

    [Fact]
    public void A_dot_is_widened_to_a_dash_as_long_as_the_stroke_is_wide()
    {
        // The icon set draws a dot as a line a hair long. Hairlines would lose it.
        ViewportGlyph glyph = Read("M 8 12 v 0.007");

        Vector2[] dash = glyph.Stroke(0).ToArray();
        dash.Length.ShouldBe(2);
        Vector2.Distance(dash[0], dash[1]).ShouldBe(1.25f / 16f, tolerance: 1e-4f);
        ((dash[0] + dash[1]) * 0.5f).X.ShouldBe(0.5f, tolerance: 1e-3f);
        ((dash[0] + dash[1]) * 0.5f).Y.ShouldBe(0.75f, tolerance: 1e-3f);
    }

    [Fact]
    public void A_short_stroke_that_is_no_dot_keeps_its_length()
    {
        ViewportGlyph glyph = Read("M 8 12 v 1");

        Vector2[] stroke = glyph.Stroke(0).ToArray();
        Vector2.Distance(stroke[0], stroke[1]).ShouldBe(1f / 16f, tolerance: 1e-5f);
    }

    [Theory]
    [InlineData("""<svg viewBox="0 0 16 16"><path d="M 1 1 Q 2" /></svg>""")]
    [InlineData("""<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="4" /></svg>""")]
    [InlineData("""<svg><path d="M 1 1 h 4" /></svg>""")]
    [InlineData("""<svg viewBox="0 0 0 16"><path d="M 1 1 h 4" /></svg>""")]
    [InlineData("not an icon")]
    public void Text_that_is_no_icon_file_gives_no_glyph_and_does_not_throw(string svg)
    {
        ViewportGlyphFile.Read(svg, ViewportGlyphs.Flatness).ShouldBeNull();
    }

    [Fact]
    public void The_flatness_is_a_third_of_a_pixel_at_the_size_an_icon_is_drawn()
    {
        float pixelsAcross = LightOverlay.IconPixels * 2f * ViewportGlyphs.Reach;

        (ViewportGlyphs.Flatness * pixelsAcross).ShouldBeLessThan(0.35f);
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any() || dir.EnumerateFiles("*.sln").Any())
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not find the solution root above the test binary");
    }
}
