using System.Globalization;
using System.Text.RegularExpressions;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// Guards the rule that a gradient brush can never reach a
/// <c>BrushTransition</c>.
/// </summary>
// How Avalonia interpolates between two gradients is undocumented, so gradients
// stay on static surfaces and only solid fills transition. Checked against the
// sources: this project has no Avalonia.
public sealed class RibbonDepthConventionTests
{
    // The only styles allowed to name a gradient. None transitions a brush.
    private static readonly string[] AllowedConsumers =
    [
        "Border.sheen",
        "Border.ribbonrule",
        "Border.ribbonbody",
    ];

    [Fact]
    public void Every_gradient_in_the_shell_is_declared_in_the_token_file()
    {
        // An inline gradient would be outside what the other tests check.
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

        offenders.ShouldBeEmpty(
            "every gradient belongs in Theme/Tokens.axaml, so the set of styles that can reach " +
            "one stays small enough to read");
    }

    [Fact]
    public void A_gradient_is_named_only_by_a_static_decorative_surface()
    {
        IReadOnlyList<string> keys = GradientKeys();
        keys.ShouldNotBeEmpty("the depth vocabulary should exist");

        var offenders = new List<string>();

        foreach ((string selector, string body, string file, int line) in StyleBlocks())
        {
            if (!keys.Any(k => body.Contains($"StaticResource {k}", StringComparison.Ordinal)))
                continue;

            if (AllowedConsumers.Any(c => selector.Contains(c, StringComparison.Ordinal)))
                continue;

            offenders.Add($"{file}({line}): {selector}");
        }

        offenders.ShouldBeEmpty(
            "a gradient may only be assigned by " + string.Join(", ", AllowedConsumers) +
            ": those three carry no BrushTransition, which is what keeps a gradient off an " +
            "interpolated property by construction rather than by review");
    }

    [Fact]
    public void No_style_that_names_a_gradient_transitions_a_brush()
    {
        IReadOnlyList<string> keys = GradientKeys();
        var offenders = new List<string>();

        foreach ((string selector, string body, string file, int line) in StyleBlocks())
        {
            bool namesGradient =
                keys.Any(k => body.Contains($"StaticResource {k}", StringComparison.Ordinal));
            bool isConsumer =
                AllowedConsumers.Any(c => selector.Contains(c, StringComparison.Ordinal));

            if (!namesGradient && !isConsumer) continue;
            if (!body.Contains("<BrushTransition", StringComparison.Ordinal)) continue;

            offenders.Add($"{file}({line}): {selector}");
        }

        offenders.ShouldBeEmpty(
            "a style that assigns a gradient, or that targets one of the surfaces gradients are " +
            "assigned to, must not declare a BrushTransition");
    }

    [Fact]
    public void Every_glyph_the_markup_asks_for_is_a_file_and_every_file_is_asked_for()
    {
        // Theme/Icons.axaml is generated from Assets/Icons/*.svg, so the svg
        // files are checked. A missing StaticResource only throws at load.
        var files = new HashSet<string>(
            Directory.EnumerateFiles(IconFolder(), "*.svg").Select(Path.GetFileNameWithoutExtension)!,
            StringComparer.Ordinal);

        files.Count.ShouldBeGreaterThan(40, "the icon set should be a folder of svg files");

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
    public void A_large_glyph_is_authored_on_its_own_thirty_two_box()
    {
        // Path.icon-lg has no transform, so a glyph drawn on the 16 grid would
        // render at a quarter of the area. The count guards against the loop
        // matching no files and passing.
        var offenders = new List<string>();
        int examined = 0;

        foreach (string file in Directory.EnumerateFiles(IconFolder(), "IconLg*.svg"))
        {
            examined++;
            string name = Path.GetFileNameWithoutExtension(file);
            double max = Regex.Matches(Regex.Match(File.ReadAllText(file), @"\bd\s*=\s*""([^""]*)""").Groups[1].Value,
                                       @"-?\d+(\.\d+)?")
                              .Select(n => double.Parse(n.Value, CultureInfo.InvariantCulture))
                              .DefaultIfEmpty(0)
                              .Max();

            // Never past 16: still drawn on the small grid.
            if (max <= 16.0)
            {
                offenders.Add($"{name}: widest coordinate {max}");
            }
        }

        examined.ShouldBeGreaterThan(8, "the large set should have been read, not silently skipped");
        offenders.ShouldBeEmpty("a large glyph is authored to fill a 32 box, ink 3.5 to 28.5");
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

    private static string IconFolder() =>
        Path.Combine(SourceRoot(), "SpectraEngine.Editor", "Assets", "Icons");

    private static IReadOnlyList<string> GradientKeys()
    {
        string tokens = File.ReadAllText(
            Path.Combine(SourceRoot(), "SpectraEngine.Editor", "Theme", "Tokens.axaml"));

        return Regex.Matches(tokens, @"<LinearGradientBrush x:Key=""([A-Za-z0-9]+)""")
                    .Select(m => m.Groups[1].Value)
                    .ToList();
    }

    // Every <Style Selector="..."> block in the theme. A text scrape, not a XAML parse.
    private static List<(string Selector, string Body, string File, int Line)> StyleBlocks()
    {
        var blocks = new List<(string, string, string, int)>();

        foreach (string file in Directory.EnumerateFiles(
                     Path.Combine(SourceRoot(), "SpectraEngine.Editor"), "*.axaml",
                     SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            string name = Path.GetFileName(file);

            for (int i = 0; i < lines.Length; i++)
            {
                Match open = Regex.Match(lines[i], @"<Style Selector=""(.*?)""\s*>?\s*$");
                if (!open.Success) continue;

                var body = new System.Text.StringBuilder();
                int j = i;
                for (; j < lines.Length; j++)
                {
                    body.AppendLine(lines[j]);
                    if (lines[j].Contains("</Style>", StringComparison.Ordinal)) break;
                }

                blocks.Add((open.Groups[1].Value, body.ToString(), name, i + 1));
                i = j;
            }
        }

        return blocks;
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
