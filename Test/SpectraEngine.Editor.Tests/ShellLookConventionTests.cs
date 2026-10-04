using System.Globalization;
using System.Text.RegularExpressions;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// Rules of the shell's look that can be checked from its sources: no
/// gradients, one grid for the icons, and a ribbon that gives the keyboard back.
/// </summary>
// Checked against the sources: this project has no Avalonia.
public sealed class ShellLookConventionTests
{
    // The colour picker's ramps. Not palette values, and nothing transitions them.
    private static readonly string[] Ramps =
    [
        "SpectraHueRamp",
        "SpectraSaturationRamp",
        "SpectraValueRamp",
    ];

    [Fact]
    public void The_only_gradients_are_the_colour_picker_ramps()
    {
        // The look is flat fills: depth comes from tone steps and outlines.
        // A gradient on a control was tried and read as dated, and how Avalonia
        // animates between two gradients is undocumented.
        var offenders = new List<string>();

        foreach (string file in Directory.EnumerateFiles(
                     Path.Combine(SourceRoot(), "SpectraEngine.Editor"), "*.axaml",
                     SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(file), "Tokens.axaml", StringComparison.Ordinal))
                continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("GradientBrush", StringComparison.Ordinal)) continue;
                offenders.Add($"{Path.GetFileName(file)}({i + 1}): {lines[i].Trim()}");
            }
        }

        offenders.ShouldBeEmpty("no gradient outside Theme/Tokens.axaml");

        string tokens = File.ReadAllText(
            Path.Combine(SourceRoot(), "SpectraEngine.Editor", "Theme", "Tokens.axaml"));

        Regex.Matches(tokens, @"<\w*GradientBrush x:Key=""([A-Za-z0-9]+)""")
             .Select(m => m.Groups[1].Value)
             .OrderBy(k => k, StringComparer.Ordinal)
             .ShouldBe(Ramps.OrderBy(k => k, StringComparer.Ordinal),
                       "the colour picker's three ramps are the only gradients in the theme");
    }

    [Fact]
    public void Every_glyph_the_markup_asks_for_is_a_file_and_every_file_is_asked_for()
    {
        // Theme/Icons.axaml is generated from Assets/Icons/*.svg, so the svg
        // files are checked. A missing StaticResource only throws at load.
        var files = new HashSet<string>(
            Directory.EnumerateFiles(IconFolder(), "*.svg").Select(Path.GetFileNameWithoutExtension)!,
            StringComparer.Ordinal);

        files.Count.ShouldBeGreaterThan(30, "the icon set should be a folder of svg files");

        var named = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<string>();

        // .cs too: some icons (IconEmpty) are only named from code.
        IEnumerable<string> sources = Directory
            .EnumerateFiles(Path.Combine(SourceRoot(), "SpectraEngine.Editor"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".axaml", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal));

        foreach (string file in sources)
        {
            if (string.Equals(Path.GetFileName(file), "Icons.axaml", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"(?:StaticResource |"")([A-Za-z0-9]+)"))
            {
                string key = m.Groups[1].Value;
                if (!key.StartsWith("Icon", StringComparison.Ordinal) && key != "CaretDown")
                {
                    continue;
                }

                named.Add(key);
                if (!files.Contains(key))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
        }

        named.ShouldNotBeEmpty("the markup should name icons");
        missing.ShouldBeEmpty("every icon the markup names must be a file in Assets/Icons");
        files.Except(named).ShouldBeEmpty("every file in Assets/Icons should be named by some markup");
    }

    [Fact]
    public void No_glyph_asks_the_build_for_a_transform()
    {
        // A file whose width and height differ from its viewBox becomes a
        // geometry with a Transform. Avalonia throws InvalidCastException from
        // StreamGeometry.Clone when a Path with a Stretch other than None draws
        // one, and several glyphs are drawn that way. Size comes from the Path
        // styles instead.
        var offenders = new List<string>();
        int examined = 0;

        foreach (string file in Directory.EnumerateFiles(IconFolder(), "*.svg"))
        {
            examined++;
            (double width, double height, double[] view) = Box(File.ReadAllText(file));

            if (view[0] != 0 || view[1] != 0 || width != view[2] || height != view[3])
            {
                offenders.Add($"{Path.GetFileName(file)}: {width}x{height} over a viewBox of {string.Join(' ', view)}");
            }
        }

        examined.ShouldBeGreaterThan(30, "the icon files should have been read, not silently skipped");
        offenders.ShouldBeEmpty("an icon's width and height must equal its viewBox, starting at 0 0");
    }

    [Fact]
    public void Each_icon_set_shares_one_box()
    {
        // Path.icon draws at 16 and Path.icon-lg at 24, both unscaled, so a
        // glyph on another box comes out the wrong size with nothing failing.
        var offenders = new List<string>();
        int small = 0, large = 0;

        foreach (string file in Directory.EnumerateFiles(IconFolder(), "Icon*.svg"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            bool isLarge = name.StartsWith("IconLg", StringComparison.Ordinal);
            double box = isLarge ? 24 : 16;
            if (isLarge) large++; else small++;

            (_, _, double[] view) = Box(File.ReadAllText(file));
            if (view[2] != box || view[3] != box)
            {
                offenders.Add($"{name}: viewBox {string.Join(' ', view)}, expected {box}");
            }
        }

        small.ShouldBeGreaterThan(25, "the small set should have been read, not silently skipped");
        large.ShouldBeGreaterThan(8, "the large set should have been read, not silently skipped");
        offenders.ShouldBeEmpty("Icon* is drawn on a 16 box and IconLg* on a 24 box");
    }

    [Fact]
    public void Every_ribbon_route_hands_the_keyboard_back_to_the_engine()
    {
        // The tool keys only work while the viewport has the keyboard, so every
        // way a ribbon gesture ends has to give it back.
        string window = File.ReadAllText(
            Path.Combine(SourceRoot(), "SpectraEngine.Editor", "MainWindow.axaml.cs"));

        foreach (string handler in new[]
                 {
                     // The pin click only forwards to SetRibbonExpanded.
                     "OnShellVerb", "OnRibbonTabClicked", "SetRibbonExpanded", "WireEntitySplit",
                 })
        {
            Body(window, handler).ShouldContain(
                "ReturnKeyboardToEngine",
                customMessage: $"{handler} must hand the keyboard back, or the tool keys stay dead after it");
        }
    }

    // One method's text, from its signature to the next private member.
    private static string Body(string source, string method)
    {
        // "void X(", so the declaration is found and not the first call.
        int start = source.IndexOf($"void {method}(", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1, $"{method} should exist");

        // "\n", not Environment.NewLine: a checkout can be LF on Windows.
        int end = source.IndexOf("\n    private ", start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = source.Length;
        }

        return source[start..end];
    }

    // The svg element's width, height and viewBox.
    private static (double Width, double Height, double[] View) Box(string svg)
    {
        string tag = Regex.Match(svg, @"<svg\b[^>]*>", RegexOptions.Singleline).Value;

        double[] view = Regex.Match(tag, @"viewBox\s*=\s*""([^""]*)""").Groups[1].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v, CultureInfo.InvariantCulture))
            .ToArray();
        view.Length.ShouldBe(4, tag);

        double Length(string name) => double.Parse(
            Regex.Match(tag, $@"\b{name}\s*=\s*""([0-9.]+)").Groups[1].Value, CultureInfo.InvariantCulture);

        return (Length("width"), Length("height"), view);
    }

    private static string IconFolder() =>
        Path.Combine(SourceRoot(), "SpectraEngine.Editor", "Assets", "Icons");

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
