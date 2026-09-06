using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// Every text colour against the surface it is read on, in WCAG contrast.
/// </summary>
/// <remarks>
/// <para>
/// <b>Arithmetic here rather than a judgement on a screenshot.</b> The palette
/// moved off near-black once already and every ratio inside the chrome moved
/// with it; the one thing that cannot be checked by looking is whether a colour
/// somebody nudged by two per cent is still legible. Muted text carries the
/// inspector's section headings and the status bar's readouts, so it is the one
/// that matters most and the one most likely to drift down.
/// </para>
/// <para>
/// <b>The FILE is the input</b>, parsed as text, because this project
/// references no Avalonia and structurally cannot evaluate a resource
/// dictionary. That is the same reason the brush-opacity convention test reads
/// the sources rather than the brushes.
/// </para>
/// </remarks>
public sealed class ThemeContrastTests
{
    /// <summary>The floor for body-sized text, which is AA at 13px and below.</summary>
    private const double BodyMinimum = 4.5;

    /// <summary>
    /// What muted text has to clear, which is above the standard.
    /// </summary>
    /// <remarks>
    /// It was 4.3:1 once, which is below AA, and it carries the section
    /// headings: raised deliberately rather than left at the line.
    /// </remarks>
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

            // An eight-digit value is AARRGGBB: the alpha is dropped, because a
            // contrast ratio is about the colour a reader sees and every text
            // token here is opaque.
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

    // WCAG 2.x relative luminance: the sRGB transfer function, then the
    // luminance weights. Not a perceptual model and not meant to be; it is the
    // number the standard names and the one a reviewer can check.
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
    [InlineData("SpectraTextBodyColor", "SpectraBgPanelColor")]
    [InlineData("SpectraTextBodyColor", "SpectraBgAppColor")]
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
        // It sits under the section headings and every status readout, and it
        // was 4.3:1 before the palette move: the floor is above the standard
        // deliberately, so a two per cent nudge cannot take it under.
        Ratio("SpectraTextMutedColor", background).ShouldBeGreaterThanOrEqualTo(
            MutedMinimum, $"muted on {background}");
    }

    [Fact]
    public void The_danger_colour_is_readable_as_TEXT_and_the_accent_is_not()
    {
        // The reason the palette has both. The brand red is the SELECTION
        // colour and fails as body text at 13px, which is what a separate
        // danger colour exists for; a shell that used the accent for error
        // text would be unreadable exactly where it matters most.
        Ratio("SpectraTextDangerColor", "SpectraBgPanelColor")
            .ShouldBeGreaterThanOrEqualTo(BodyMinimum);

        Ratio("SpectraAccentColor", "SpectraBgPanelColor")
            .ShouldBeLessThan(BodyMinimum);
    }

    [Fact]
    public void Amber_state_text_is_readable_on_both_grounds()
    {
        // Warning and lit-mode share this hue, and both are read as words.
        Ratio("SpectraModeColor", "SpectraBgPanelColor").ShouldBeGreaterThanOrEqualTo(BodyMinimum);
        Ratio("SpectraModeColor", "SpectraBgAppColor").ShouldBeGreaterThanOrEqualTo(BodyMinimum);
    }

    [Fact]
    public void The_elevation_scale_climbs_rather_than_wandering()
    {
        Dictionary<string, (double R, double G, double B)> colors = Colors();

        // Anything you press is lighter than the panel it sits on, and anything
        // you type into is darker: that direction IS the affordance, so a step
        // that went the wrong way would make a button read as a field.
        double window = Luminance(colors["SpectraBgWindowColor"]);
        double app = Luminance(colors["SpectraBgAppColor"]);
        double panel = Luminance(colors["SpectraBgPanelColor"]);
        double raised = Luminance(colors["SpectraBgRaisedColor"]);
        double control = Luminance(colors["SpectraBgControlColor"]);
        double input = Luminance(colors["SpectraBgInputColor"]);

        app.ShouldBeGreaterThan(window);
        panel.ShouldBeGreaterThan(app);
        raised.ShouldBeGreaterThan(panel);
        control.ShouldBeGreaterThan(raised);

        input.ShouldBeLessThan(panel);
    }
}
