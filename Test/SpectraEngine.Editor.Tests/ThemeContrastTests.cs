using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// Every text colour against the surface it is read on, in WCAG contrast.
/// </summary>
// Tokens.axaml is parsed as text: this project has no Avalonia.
public sealed class ThemeContrastTests
{
    // WCAG AA for text at 12px.
    private const double BodyMinimum = 4.5;

    // Above AA on purpose, on the two surfaces most muted text sits on.
    private const double MutedMinimum = 4.8;

    private static Dictionary<string, (double R, double G, double B)> Colors()
    {
        string path = Path.Combine(RepoRoot(), "SpectraEngine.Editor", "Theme", "Tokens.axaml");
        File.Exists(path).ShouldBeTrue(path);

        var found = new Dictionary<string, (double, double, double)>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(
            File.ReadAllText(path),
            @"<Color\s+x:Key=""(?<key>[A-Za-z0-9]+)"">\s*#(?<hex>[0-9A-Fa-f]{6,8})\s*</Color>"))
        {
            string hex = match.Groups["hex"].Value;

            // AARRGGBB: drop the alpha, every text token is opaque.
            if (hex.Length == 8) hex = hex[2..];

            found[match.Groups["key"].Value] = (
                int.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
                int.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
                int.Parse(hex[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
        }

        return found;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Spectra.slnx")))
            dir = dir.Parent;

        dir.ShouldNotBeNull("no Spectra.slnx above the test binary");
        return dir!.FullName;
    }

    // WCAG 2.x relative luminance.
    private static double Luminance((double R, double G, double B) color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    private static double Linear(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static double Contrast(
        (double R, double G, double B) a, (double R, double G, double B) b)
    {
        double first = Luminance(a);
        double second = Luminance(b);

        (double lighter, double darker) = first >= second ? (first, second) : (second, first);
        return (lighter + 0.05) / (darker + 0.05);
    }

    // How much of a colour with an alpha covers what is under it.
    private static double Alpha(string key)
    {
        string tokens = File.ReadAllText(
            Path.Combine(RepoRoot(), "SpectraEngine.Editor", "Theme", "Tokens.axaml"));

        Match match = Regex.Match(tokens, $@"<Color\s+x:Key=""{key}"">\s*#(?<alpha>[0-9A-Fa-f]{{2}})[0-9A-Fa-f]{{6}}\s*</Color>");
        match.Success.ShouldBeTrue(key);

        return int.Parse(match.Groups["alpha"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
    }

    // A colour seen through a wash that covers a share of it.
    private static (double R, double G, double B) Under(
        (double R, double G, double B) color, (double R, double G, double B) wash, double share) => (
        (wash.R * share) + (color.R * (1 - share)),
        (wash.G * share) + (color.G * (1 - share)),
        (wash.B * share) + (color.B * (1 - share)));

    private static double Ratio(string text, string background)
    {
        Dictionary<string, (double R, double G, double B)> colors = Colors();

        colors.ContainsKey(text).ShouldBeTrue(text);
        colors.ContainsKey(background).ShouldBeTrue(background);

        return Contrast(colors[text], colors[background]);
    }

    [Theory]
    [InlineData("SpectraTextEmphasisColor", "SpectraBgPanelColor")]
    [InlineData("SpectraTextEmphasisColor", "SpectraBgAppColor")]
    [InlineData("SpectraTextEmphasisColor", "SpectraBgRibbonColor")]
    [InlineData("SpectraTextEmphasisColor", "SpectraBgRaisedColor")]
    [InlineData("SpectraTextBodyColor", "SpectraBgPanelColor")]
    [InlineData("SpectraTextBodyColor", "SpectraBgAppColor")]
    [InlineData("SpectraTextBodyColor", "SpectraBgRibbonColor")]
    [InlineData("SpectraTextBodyColor", "SpectraBgRaisedColor")]
    [InlineData("SpectraTextBodyColor", "SpectraBgControlColor")]
    public void Body_text_clears_the_standard_on_every_surface_it_is_read_on(
        string text, string background)
    {
        Ratio(text, background).ShouldBeGreaterThanOrEqualTo(
            BodyMinimum, $"{text} on {background}");
    }

    [Theory]
    [InlineData("SpectraBgPanelColor")]
    [InlineData("SpectraBgAppColor")]
    public void Muted_text_clears_the_higher_floor_it_was_raised_to(string background)
    {
        Ratio("SpectraTextMutedColor", background).ShouldBeGreaterThanOrEqualTo(
            MutedMinimum, $"muted on {background}");
    }

    [Theory]
    [InlineData("SpectraBgRibbonColor")]
    [InlineData("SpectraBgRaisedColor")]
    public void Muted_text_still_clears_the_standard_on_the_lighter_surfaces(string background)
    {
        // Ribbon group captions and header bands are muted text on these.
        Ratio("SpectraTextMutedColor", background).ShouldBeGreaterThanOrEqualTo(
            BodyMinimum, $"muted on {background}");
    }

    [Fact]
    public void The_danger_colour_is_readable_as_text()
    {
        Ratio("SpectraTextDangerColor", "SpectraBgPanelColor")
            .ShouldBeGreaterThanOrEqualTo(BodyMinimum);
        Ratio("SpectraTextDangerColor", "SpectraBgAppColor")
            .ShouldBeGreaterThanOrEqualTo(BodyMinimum);
    }

    [Fact]
    public void White_is_readable_on_the_fills_it_is_put_on_and_not_on_the_bright_accent()
    {
        // The bright accent failing under white text is why AccentRest exists.
        Ratio("SpectraTextOnAccentColor", "SpectraAccentRestColor")
            .ShouldBeGreaterThanOrEqualTo(BodyMinimum);
        Ratio("SpectraTextOnAccentColor", "SpectraPlayFillColor")
            .ShouldBeGreaterThanOrEqualTo(BodyMinimum);

        Ratio("SpectraTextOnAccentColor", "SpectraAccentColor")
            .ShouldBeLessThan(BodyMinimum);
    }

    [Fact]
    public void Amber_state_text_is_readable_on_both_grounds()
    {
        Ratio("SpectraModeColor", "SpectraBgPanelColor").ShouldBeGreaterThanOrEqualTo(BodyMinimum);
        Ratio("SpectraModeColor", "SpectraBgAppColor").ShouldBeGreaterThanOrEqualTo(BodyMinimum);
    }

    [Theory]
    // A card: its name, its class line, the activator's name, a port, the
    // note and a state label, a state value, a port its class lacks.
    [InlineData("SpectraTextEmphasisColor", "SpectraLogicCardHeadColor")]
    [InlineData("SpectraTextMutedColor", "SpectraLogicCardHeadColor")]
    [InlineData("SpectraTextBodyColor", "SpectraLogicCardHeadColor")]
    [InlineData("SpectraTextBodyColor", "SpectraLogicCardColor")]
    [InlineData("SpectraTextMutedColor", "SpectraLogicCardColor")]
    [InlineData("SpectraTextEmphasisColor", "SpectraLogicCardColor")]
    [InlineData("SpectraTextDangerColor", "SpectraLogicCardColor")]
    // The card of a name nothing has: the name, what is wrong, its port.
    [InlineData("SpectraTextDangerColor", "SpectraLogicStubHeadColor")]
    [InlineData("SpectraLogicStubTextColor", "SpectraLogicStubHeadColor")]
    [InlineData("SpectraTextBodyColor", "SpectraLogicStubColor")]
    // The empty state on the ground. A label's own surface is the panel.
    [InlineData("SpectraTextBodyColor", "SpectraLogicGroundColor")]
    // The event strip: a line, its tick, a failure.
    [InlineData("SpectraTextBodyColor", "SpectraBgInputColor")]
    [InlineData("SpectraTextMutedColor", "SpectraBgInputColor")]
    [InlineData("SpectraTextDangerColor", "SpectraBgInputColor")]
    // The Playing pill on the toolbar.
    [InlineData("SpectraSuccessColor", "SpectraBgPanelColor")]
    public void The_Logic_view_s_text_clears_the_standard_on_the_surface_it_is_drawn_on(
        string text, string background)
    {
        Ratio(text, background).ShouldBeGreaterThanOrEqualTo(
            BodyMinimum, $"{text} on {background}");
    }

    [Fact]
    public void The_wash_over_what_a_filter_leaves_out_is_the_ground_itself()
    {
        // It stands in for an opacity, so it has to be the colour under it.
        Dictionary<string, (double R, double G, double B)> colors = Colors();

        colors["SpectraLogicDimWashColor"].ShouldBe(colors["SpectraLogicGroundColor"]);
    }

    [Theory]
    // A card the filter leaves out: its name, its class line, a port, the note.
    [InlineData("SpectraTextEmphasisColor", "SpectraLogicCardHeadColor")]
    [InlineData("SpectraTextMutedColor", "SpectraLogicCardHeadColor")]
    [InlineData("SpectraTextBodyColor", "SpectraLogicCardColor")]
    [InlineData("SpectraTextMutedColor", "SpectraLogicCardColor")]
    // The card of a name nothing has, and a label.
    [InlineData("SpectraTextDangerColor", "SpectraLogicStubHeadColor")]
    [InlineData("SpectraLogicStubTextColor", "SpectraLogicStubHeadColor")]
    [InlineData("SpectraTextBodyColor", "SpectraBgPanelColor")]
    public void What_a_filter_leaves_out_is_as_readable_as_the_shell_s_dimmed_text(
        string text, string background)
    {
        Dictionary<string, (double R, double G, double B)> colors = Colors();
        (double R, double G, double B) ground = colors["SpectraLogicGroundColor"];
        double wash = Alpha("SpectraLogicDimWashColor");

        wash.ShouldBeGreaterThan(0.2, "a wash this thin dims nothing");
        Contrast(Under(colors[text], ground, wash), Under(colors[background], ground, wash))
            .ShouldBeGreaterThanOrEqualTo(
                Ratio("SpectraTextDisabledColor", "SpectraBgPanelColor"), $"{text} on {background}");
    }

    [Fact]
    public void No_token_is_defined_twice()
    {
        // A second definition of a key throws when the application starts,
        // and nothing before that says so.
        string tokens = File.ReadAllText(
            Path.Combine(RepoRoot(), "SpectraEngine.Editor", "Theme", "Tokens.axaml"));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var twice = new List<string>();

        foreach (Match match in Regex.Matches(tokens, @"x:Key=""(?<key>[A-Za-z0-9]+)"""))
        {
            if (!seen.Add(match.Groups["key"].Value))
                twice.Add(match.Groups["key"].Value);
        }

        seen.Count.ShouldBeGreaterThan(100, "the token file should have been read, not skipped");
        twice.ShouldBeEmpty();
    }

    [Fact]
    public void The_elevation_scale_climbs_rather_than_wandering()
    {
        Dictionary<string, (double R, double G, double B)> colors = Colors();

        double window = Luminance(colors["SpectraBgWindowColor"]);
        double panel = Luminance(colors["SpectraBgPanelColor"]);
        double ribbon = Luminance(colors["SpectraBgRibbonColor"]);
        double raised = Luminance(colors["SpectraBgRaisedColor"]);
        double control = Luminance(colors["SpectraBgControlColor"]);
        double hover = Luminance(colors["SpectraBgControlHoverColor"]);
        double pressed = Luminance(colors["SpectraBgControlActiveColor"]);
        double input = Luminance(colors["SpectraBgInputColor"]);

        // The ground is darkest, so the gaps between panels read as gaps.
        panel.ShouldBeGreaterThan(window);
        ribbon.ShouldBeGreaterThan(panel);
        raised.ShouldBeGreaterThan(ribbon);

        // A key is lighter than both surfaces it sits on, lighter still under
        // the pointer, and darker than either when pressed.
        control.ShouldBeGreaterThan(ribbon);
        hover.ShouldBeGreaterThan(control);
        pressed.ShouldBeLessThan(panel);

        // A well is darker than anything it sits in.
        input.ShouldBeLessThan(panel);
    }

    [Fact]
    public void A_key_is_outlined_dark_and_a_well_light()
    {
        // A resting control with an outline lighter than its fill is what a
        // hovered one looks like, so every button looked lit.
        Dictionary<string, (double R, double G, double B)> colors = Colors();

        Luminance(colors["SpectraBorderControlColor"])
            .ShouldBeLessThan(Luminance(colors["SpectraBgControlColor"]));
        Luminance(colors["SpectraBorderControlColor"])
            .ShouldBeLessThan(Luminance(colors["SpectraBgPanelColor"]));

        Luminance(colors["SpectraBorderInputColor"])
            .ShouldBeGreaterThan(Luminance(colors["SpectraBgInputColor"]));

        // The group divider is the opposite of the tray seams beside it.
        Luminance(colors["SpectraRibbonRuleColor"])
            .ShouldBeGreaterThan(Luminance(colors["SpectraBgRibbonColor"]));
    }
}
