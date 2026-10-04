namespace SpectraEngine.Editor.Tests;

/// <summary>
/// A shell brush states its translucency in its colour's alpha, never in <c>Opacity</c>.
/// </summary>
// A BrushTransition interpolates Color and Opacity separately and the renderer
// multiplies them, so an animated fill overshoots its target and falls back.
// Scans the sources: this project references no Avalonia.
public sealed class BrushOpacityConventionTests
{
    [Fact]
    public void No_theme_brush_carries_an_Opacity_property()
    {
        var offenders = new List<string>();

        foreach (string file in Directory.EnumerateFiles(
                     Path.Combine(SourceRoot(), "SpectraEngine.Editor"), "*.axaml",
                     SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!line.Contains("SolidColorBrush", StringComparison.Ordinal)) continue;
                if (!line.Contains("Opacity=", StringComparison.Ordinal)) continue;

                offenders.Add($"{Path.GetFileName(file)}({i + 1}): {line.Trim()}");
            }
        }

        offenders.ShouldBeEmpty(
            "a brush must state its alpha in its colour; Opacity on the brush is interpolated " +
            "separately from the colour and multiplied with it, so an animated fill overshoots " +
            "its target and falls back");
    }

    [Fact]
    public void No_transitioned_fill_rests_on_Transparent()
    {
        // Transparent is #00000000, so a fade to translucent white passes through grey.
        string controls = Path.Combine(SourceRoot(), "SpectraEngine.Editor", "Theme", "Controls.axaml");
        string text = File.ReadAllText(controls);

        var offenders = new List<string>();
        foreach (System.Text.RegularExpressions.Match style in
                 System.Text.RegularExpressions.Regex.Matches(
                     text, """<Style Selector="([^"]+)"\s*>(.*?)</Style>""",
                     System.Text.RegularExpressions.RegexOptions.Singleline))
        {
            string body = style.Groups[2].Value;
            if (!body.Contains("BrushTransition", StringComparison.Ordinal)) continue;
            if (!body.Contains("Property=\"Background\"", StringComparison.Ordinal)) continue;
            if (!body.Contains("Value=\"Transparent\"", StringComparison.Ordinal)) continue;

            offenders.Add(style.Groups[1].Value.Trim());
        }

        offenders.ShouldBeEmpty(
            "a style that animates Background must rest on the same hue at zero alpha " +
            "(#00FFFFFF), not on Transparent, which is transparent BLACK");
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("no solution file above the test binary");
    }
}
