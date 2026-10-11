using System.Globalization;
using System.Text.RegularExpressions;

namespace SpectraEngine.Bsp.Tests;

/// <summary>Reads what the CFrame probe printed in Roblox Studio: each case's name and its numbers.</summary>
internal static class StudioProbeAnswers
{
    // Takes entries wherever they stand, not a strict JSON document: text
    // copied out of Studio's Output window can carry a time on every line.
    private static readonly Regex Entry = new("\"([A-Za-z0-9_]+)\"\\s*:\\s*\\[([^\\]]*)\\]");

    /// <summary>
    /// The numbers by case name. A <c>null</c> in the text, which the probe
    /// writes for a NaN or an infinity, comes back as NaN. A case Roblox
    /// could not work out has no numbers.
    /// </summary>
    public static Dictionary<string, float[]> Parse(string text)
    {
        var answers = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (Match match in Entry.Matches(text))
        {
            string[] parts = match.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            answers[match.Groups[1].Value] = Array.ConvertAll(parts, Number);
        }

        return answers;
    }

    private static float Number(string text) =>
        text == "null" ? float.NaN : float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
