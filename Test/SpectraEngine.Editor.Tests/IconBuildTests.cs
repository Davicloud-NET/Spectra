using SpectraEngine.Editing.Icons;
using System.Numerics;
using System.Text.RegularExpressions;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The shell's build and the 3D view read the same icon files. What the build
/// writes for a file has to draw the strokes the 3D view draws from it.
/// </summary>
// The build joins a file's paths into one figure string. The 3D view reads
// each path on its own, as SVG says.
public sealed class IconBuildTests
{
    private const string TwoStrokes = """
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16">
          <path d="M2 2h4" />
          <path d="{0}" />
        </svg>
        """;

    private static string Icon(string secondPath) => TwoStrokes.Replace("{0}", secondPath, StringComparison.Ordinal);

    // The glyph the 3D view makes of an icon file.
    private static ViewportGlyph Glyph(string svg) =>
        ViewportGlyphFile.Read(svg, ViewportGlyphs.Flatness).ShouldNotBeNull();

    // The glyph of what the build wrote: one path, as the shell draws it.
    private static ViewportGlyph Built(string figures) =>
        Glyph($"""<svg viewBox="0 0 16 16"><path d="{figures}" /></svg>""");

    private static void ShouldDrawTheSame(ViewportGlyph built, ViewportGlyph read, string key)
    {
        built.StrokeCount.ShouldBe(read.StrokeCount, key);
        for (int i = 0; i < read.StrokeCount; i++)
            built.Stroke(i).ToArray().ShouldBe(read.Stroke(i).ToArray(), $"{key}, stroke {i}");
    }

    [Fact]
    public void Every_world_icon_is_built_into_the_strokes_the_3d_view_reads_from_its_file()
    {
        Dictionary<string, string> built = IconBuild.ReadFigures(File.ReadAllText(
            Path.Combine(IconFiles.SourceRoot(), "SpectraEngine.Editor", "Theme", "Icons.axaml")));
        string[] files = Directory.GetFiles(IconFiles.WorldFolder, "*.svg");

        files.Length.ShouldBeGreaterThan(20, "the world icons should have been read, not skipped");
        foreach (string file in files)
        {
            string key = Path.GetFileNameWithoutExtension(file);
            built.ShouldContainKey(key);

            ShouldDrawTheSame(Built(built[key]), Glyph(File.ReadAllText(file)), key);
        }
    }

    [Theory]
    [InlineData("m3 9h4")]
    [InlineData("m 3,9 4,0 0,2")]
    [InlineData("m.5.5.5.5")]
    public async Task A_later_path_that_opens_with_a_relative_move_is_built_to_start_from_the_origin(string secondPath)
    {
        string svg = Icon(secondPath);

        IconBuild build = await IconBuild.RunAsync(("IconFixture", svg));

        build.ExitCode.ShouldBe(0, build.Log);
        ShouldDrawTheSame(Built(build.Figures["IconFixture"]), Glyph(svg), secondPath);
    }

    [Fact]
    public async Task A_path_that_draws_before_it_moves_stops_the_build_and_names_the_file()
    {
        // The 3D view gives such a file no glyph. Joined to the path before
        // it, the shell would draw it as more of that path.
        IconBuild build = await IconBuild.RunAsync(("IconFixture", Icon("h4")), ("IconFine", Icon("M3 9h4")));

        build.ExitCode.ShouldNotBe(0, build.Log);
        Regex.IsMatch(build.Log, @"error\s*:.*IconFixture").ShouldBeTrue(build.Log);
        build.Figures.ShouldNotContainKey("IconFixture");
        build.Figures.ShouldContainKey("IconFine");
        ViewportGlyphFile.Read(Icon("h4"), ViewportGlyphs.Flatness).ShouldBeNull();
    }

    [Fact]
    public async Task An_icon_that_is_a_file_in_two_folders_stops_the_build()
    {
        IconBuild build = await IconBuild.RunAsync(
            ("Shell/IconTwin", Icon("M3 9h4")), ("World/IconTwin", Icon("M3 9h4")));

        build.ExitCode.ShouldNotBe(0, build.Log);
        Regex.IsMatch(build.Log, @"error\s*:.*IconTwin").ShouldBeTrue(build.Log);
    }

    [Fact]
    public void Joined_as_written_a_relative_move_would_start_from_where_the_path_before_it_ended()
    {
        // Why the build rewrites the move, and that the comparison can fail.
        ViewportGlyph apart = Glyph(Icon("m3 9h4"));
        ViewportGlyph joined = Built("M2 2h4 m3 9h4");

        apart.Stroke(1).ToArray().ShouldBe([new Vector2(3f, 9f) / 16f, new Vector2(7f, 9f) / 16f]);
        joined.Stroke(1).ToArray().ShouldNotBe(apart.Stroke(1).ToArray());
    }
}
